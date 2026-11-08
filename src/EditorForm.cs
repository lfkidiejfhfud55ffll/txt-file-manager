using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TxtManager
{
    /// <summary>就地编辑窗口：保持原编码、检测外部修改、不产生任何副本。</summary>
    internal sealed class EditorForm : Form
    {
        private readonly string _path;
        private Encoding _encoding;
        private DateTime _loadedStamp;
        private bool _dirty;
        private bool _saved;
        private bool _loading;
        private string _eol = "\r\n";

        private TextBox _editor;
        private Label _lblTitle;
        private Label _lblPath;
        private Label _lblStatus;
        private Label _lblPos;
        private ComboBox _cboEncoding;
        private Button _btnSave;

        private static readonly string[] EncodingNames =
            new string[] { "UTF-8", "UTF-8 (BOM)", "GB18030 / GBK", "UTF-16 LE" };

        private static Encoding[] Encodings()
        {
            return new Encoding[]
            {
                new UTF8Encoding(false), new UTF8Encoding(true),
                Encoding.GetEncoding(54936), Encoding.Unicode
            };
        }

        public bool Saved { get { return _saved; } }
        public string FilePath { get { return _path; } }
        public TextFileEntry Result { get; private set; }

        public EditorForm(TextFileEntry entry)
        {
            _path = entry.FullPath;
            BuildUi();
            LoadContent(entry);
        }

        private void BuildUi()
        {
            Text = "编辑 - " + Path.GetFileName(_path);
            ClientSize = new Size(1040, 720);
            MinimumSize = new Size(660, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.Ui(9F, FontStyle.Regular);
            Icon = AppIcon.Value;
            KeyPreview = true;
            ShowInTaskbar = false;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 64;
            top.BackColor = Color.White;
            top.Padding = new Padding(0);

            _lblTitle = new Label();
            _lblTitle.AutoSize = false;
            _lblTitle.AutoEllipsis = true;
            _lblTitle.Location = new Point(20, 11);
            _lblTitle.Size = new Size(600, 24);
            _lblTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblTitle.Font = Theme.Ui(11F, FontStyle.Bold);
            _lblTitle.ForeColor = Theme.Text;

            _lblPath = new Label();
            _lblPath.AutoSize = false;
            _lblPath.AutoEllipsis = true;
            _lblPath.Location = new Point(22, 37);
            _lblPath.Size = new Size(600, 18);
            _lblPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblPath.Font = Theme.Ui(8.5F, FontStyle.Regular);
            _lblPath.ForeColor = Theme.Muted;

            top.Controls.Add(_lblTitle);
            top.Controls.Add(_lblPath);
            top.Resize += delegate
            {
                _lblTitle.Width = top.Width - 40;
                _lblPath.Width = top.Width - 44;
            };

            Panel editorHost = new Panel();
            editorHost.Dock = DockStyle.Fill;
            editorHost.BackColor = Color.White;
            editorHost.Padding = new Padding(0);

            _editor = new TextBox();
            _editor.Dock = DockStyle.Fill;
            _editor.Multiline = true;
            _editor.ScrollBars = ScrollBars.Both;
            _editor.WordWrap = false;
            _editor.AcceptsTab = true;
            _editor.HideSelection = false;
            _editor.BorderStyle = BorderStyle.None;
            _editor.Font = Theme.Mono(10.5F);
            _editor.BackColor = Color.White;
            _editor.ForeColor = Color.FromArgb(24, 32, 44);
            _editor.TextChanged += delegate
            {
                if (!_dirty)
                {
                    _dirty = true;
                    UpdateTitle();
                }
                if (!_loading && _lblStatus != null)
                {
                    _lblStatus.Text = "有未保存的修改";
                    _lblStatus.ForeColor = Color.FromArgb(190, 110, 30);
                }
            };
            _editor.KeyUp += delegate { UpdateCaret(); };
            _editor.MouseUp += delegate { UpdateCaret(); };
            _editor.KeyDown += delegate { UpdateCaret(); };

            Panel editorPad = new Panel();
            editorPad.Dock = DockStyle.Fill;
            editorPad.Padding = new Padding(14, 10, 8, 6);
            editorPad.BackColor = Color.White;
            editorPad.Controls.Add(_editor);

            editorHost.Controls.Add(editorPad);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 50;
            bottom.BackColor = Color.White;

            _btnSave = Theme.PrimaryButton("保存 (Ctrl+S)", 118, 32);
            _btnSave.Location = new Point(16, 9);
            _btnSave.Click += delegate { SaveFile(); };

            Button btnRevert = Theme.GhostButton("还原", 68, 32);
            btnRevert.Location = new Point(140, 9);
            btnRevert.Click += delegate { Revert(); };

            Button btnClose = Theme.GhostButton("关闭", 68, 32);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new Point(bottom.Width - 84, 9);
            btnClose.Click += delegate { Close(); };

            Label capEnc = Theme.MakeLabel("编码", 8.5F, FontStyle.Regular, Theme.Faint);
            capEnc.Location = new Point(226, 16);

            _cboEncoding = new ComboBox();
            _cboEncoding.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboEncoding.FlatStyle = FlatStyle.Flat;
            _cboEncoding.Font = Theme.Ui(8.75F, FontStyle.Regular);
            _cboEncoding.Width = 132;
            _cboEncoding.Location = new Point(264, 12);
            _cboEncoding.Items.AddRange(EncodingNames);
            _cboEncoding.SelectedIndexChanged += delegate { if (!_loading && SelectedEncodingIsDifferent()) MarkDirty(); };

            _lblPos = Theme.MakeLabel("行 1，列 1", 8.5F, FontStyle.Regular, Theme.Faint);
            _lblPos.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblPos.AutoSize = false;
            _lblPos.Size = new Size(150, 20);
            _lblPos.TextAlign = ContentAlignment.MiddleRight;
            _lblPos.Location = new Point(bottom.Width - 242, 15);

            _lblStatus = Theme.MakeLabel("", 8.5F, FontStyle.Regular, Theme.Muted);
            _lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblStatus.AutoSize = false;
            _lblStatus.Size = new Size(300, 20);
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Location = new Point(bottom.Width - 554, 15);

            bottom.Controls.Add(_btnSave);
            bottom.Controls.Add(btnRevert);
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(capEnc);
            bottom.Controls.Add(_cboEncoding);
            bottom.Controls.Add(_lblPos);
            bottom.Controls.Add(_lblStatus);
            bottom.Resize += delegate
            {
                btnClose.Left = bottom.Width - btnClose.Width - 16;
                _lblPos.Left = bottom.Width - 242;
                _lblStatus.Left = bottom.Width - 554;
            };

            top.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1);
            };
            bottom.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            Controls.Add(editorHost);
            Controls.Add(bottom);
            Controls.Add(top);

            Load += delegate
            {
                btnClose.Left = bottom.Width - btnClose.Width - 16;
                _lblPos.Left = bottom.Width - 242;
                _lblStatus.Left = bottom.Width - 554;
            };
        }

        private bool SelectedEncodingIsDifferent()
        {
            if (_cboEncoding.SelectedIndex < 0) return false;
            return Encodings()[_cboEncoding.SelectedIndex].CodePage != _encoding.CodePage;
        }

        private void MarkDirty()
        {
            _dirty = true;
            UpdateTitle();
        }

        private void LoadContent(TextFileEntry entry)
        {
            try
            {
                LoadedText lt = TextCodec.Load(_path, int.MaxValue);
                _encoding = lt.Encoding;
                _loadedStamp = lt.Modified;
                _eol = DetectEol(lt.Text);
                _loading = true;
                _editor.Text = lt.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                _editor.SelectionStart = 0;
                _editor.SelectionLength = 0;
                int idx = IndexOfEncoding(lt.Encoding);
                _cboEncoding.SelectedIndex = idx < 0 ? 0 : idx;
                _loading = false;
                _dirty = false;
                _lblTitle.Text = Path.GetFileName(_path);
                _lblPath.Text = _path;
                _lblStatus.Text = "已读取 " + Fmt.Size(lt.TotalBytes) + " · " + TextCodec.NameOf(lt.Encoding);
            _lblStatus.ForeColor = Theme.Muted;
                UpdateTitle();
                UpdateCaret();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法读取文件：\r\n" + ex.Message, "打开失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { Close(); }));
            }
        }

        private static string DetectEol(string text)
        {
            int crlf = 0, lf = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == (char)10)
                {
                    if (i > 0 && text[i - 1] == (char)13) crlf++;
                    else lf++;
                }
            }
            if (lf > crlf) return (char)10 + "";
            return (char)13 + "" + (char)10;
        }

        private int IndexOfEncoding(Encoding e)
        {
            Encoding[] all = Encodings();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].CodePage == e.CodePage) return i;
            }
            return -1;
        }

        private Encoding CurrentEncoding()
        {
            Encoding[] all = Encodings();
            int i = _cboEncoding.SelectedIndex;
            if (i < 0 || i >= all.Length) i = 0;
            return all[i];
        }

        private void UpdateTitle()
        {
            string name = Path.GetFileName(_path);
            Text = (_dirty ? "* " : "") + "编辑 - " + name;
            _lblTitle.Text = (_dirty ? "* " : "") + name;
        }

        private void UpdateCaret()
        {
            try
            {
                int pos = _editor.SelectionStart;
                int line = _editor.GetLineFromCharIndex(pos);
                int start = _editor.GetFirstCharIndexFromLine(line);
                int col = pos - start + 1;
                _lblPos.Text = "行 " + (line + 1) + "，列 " + col;
            }
            catch (Exception) { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveFile();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Revert()
        {
            if (_dirty)
            {
                DialogResult r = MessageBox.Show(this, "放弃未保存的修改并重新从磁盘读取？", "还原",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            try
            {
                LoadedText lt = TextCodec.Load(_path, int.MaxValue);
                _encoding = lt.Encoding;
                _loadedStamp = lt.Modified;
                _eol = DetectEol(lt.Text);
                _loading = true;
                _editor.Text = lt.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                int idx = IndexOfEncoding(lt.Encoding);
                _cboEncoding.SelectedIndex = idx < 0 ? 0 : idx;
                _loading = false;
                _dirty = false;
                UpdateTitle();
                _lblStatus.Text = "已从磁盘还原";
            _lblStatus.ForeColor = Theme.Muted;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取失败：\r\n" + ex.Message, "还原",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveFile()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    MessageBox.Show(this, "文件已不存在：\r\n" + _path, "保存失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DateTime nowStamp = File.GetLastWriteTime(_path);
                if (nowStamp != _loadedStamp)
                {
                    DialogResult r = MessageBox.Show(this,
                        "文件在编辑期间被其他程序修改过。\r\n仍要用当前内容覆盖吗？",
                        "文件已变更", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r != DialogResult.Yes) return;
                }

                FileAttributes attrs = File.GetAttributes(_path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    DialogResult r = MessageBox.Show(this,
                        "该文件是只读属性，是否去掉只读属性后保存？", "只读文件",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                    File.SetAttributes(_path, attrs & ~FileAttributes.ReadOnly);
                }

                Encoding enc = CurrentEncoding();
                string outgoing = _eol == "\n" ? _editor.Text.Replace("\r\n", "\n") : _editor.Text;
                TextCodec.Save(_path, outgoing, enc);
                _encoding = enc;
                _loadedStamp = File.GetLastWriteTime(_path);
                _dirty = false;
                _saved = true;
                UpdateTitle();
                _lblStatus.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss") + " · " + Fmt.Size(new FileInfo(_path).Length);
            _lblStatus.ForeColor = Theme.Muted;
                Result = TextFileEntry.FromPath(_path);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "保存失败：\r\n" + ex.Message + "\r\n\r\n文件可能正被其他程序占用。",
                    "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, "没有权限写入该文件：\r\n" + ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：\r\n" + ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                DialogResult r = MessageBox.Show(this, "有未保存的修改，是否保存？", "关闭",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (r == DialogResult.Yes)
                {
                    SaveFile();
                    if (_dirty)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
            base.OnFormClosing(e);
        }
    }

    /// <summary>延迟加载的应用图标。</summary>
    internal static class AppIcon
    {
        private static Icon _icon;
        public static Icon Value
        {
            get
            {
                if (_icon == null)
                {
                    try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch (Exception) { }
                }
                return _icon;
            }
        }
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TxtManager
{
    /// <summary>右侧详情 + 内容预览面板。</summary>
    internal sealed class PreviewPane : Panel
    {
        private const int PreviewBytes = 256 * 1024;

        private TextFileEntry _entry;
        private Label _lblName;
        private TextBox _txtPath;
        private Label _lblEmpty;
        private Label _lblNotice;
        private TextBox _txtPreview;
        private Label _vSize, _vEncoding, _vCreated, _vModified, _vAccessed, _vLines, _vAttr, _vDisk;
        private Button _btnEdit;

        public event EventHandler EditRequested;
        public event EventHandler OpenFolderRequested;
        public event EventHandler NotepadRequested;
        public event EventHandler CopyPathRequested;

        public TextFileEntry CurrentEntry { get { return _entry; } }

        public PreviewPane()
        {
            BackColor = Color.White;
            Padding = new Padding(0);
            BuildUi();
            Clear();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.BackColor = Color.White;
            root.Padding = new Padding(0);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            // ---- 头部 ----
            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.BackColor = Color.White;
            head.Margin = new Padding(0);

            _lblName = new Label();
            _lblName.AutoSize = false;
            _lblName.AutoEllipsis = true;
            _lblName.Location = new Point(18, 12);
            _lblName.Size = new Size(200, 26);
            _lblName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblName.Font = Theme.Ui(11.5F, FontStyle.Bold);
            _lblName.ForeColor = Theme.Text;
            _lblName.Text = "";

            _btnEdit = Theme.PrimaryButton("编辑", 76, 32);
            _btnEdit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnEdit.Location = new Point(head.Width - 94, 12);
            _btnEdit.Click += delegate { Raise(EditRequested); };
            _btnEdit.Enabled = false;
            _btnEdit.Name = "btnEdit";

            _txtPath = new TextBox();
            _txtPath.BorderStyle = BorderStyle.None;
            _txtPath.ReadOnly = true;
            _txtPath.BackColor = Color.White;
            _txtPath.ForeColor = Theme.Muted;
            _txtPath.Font = Theme.Ui(8.75F, FontStyle.Regular);
            _txtPath.Location = new Point(18, 44);
            _txtPath.Size = new Size(300, 20);
            _txtPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtPath.TabStop = false;

            head.Controls.Add(_lblName);
            head.Controls.Add(_txtPath);
            head.Controls.Add(_btnEdit);
            head.Resize += delegate
            {
                _btnEdit.Left = head.Width - _btnEdit.Width - 18;
                _lblName.Width = Math.Max(60, _btnEdit.Left - _lblName.Left - 12);
                _txtPath.Width = Math.Max(80, head.Width - 36);
            };

            // ---- 信息表 ----
            TableLayoutPanel info = new TableLayoutPanel();
            info.Dock = DockStyle.Fill;
            info.ColumnCount = 4;
            info.RowCount = 4;
            info.Margin = new Padding(18, 0, 18, 10);
            info.BackColor = Color.White;
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int i = 0; i < 4; i++) info.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

            _vSize = AddInfo(info, 0, 0, "大小");
            _vEncoding = AddInfo(info, 0, 2, "编码");
            _vCreated = AddInfo(info, 1, 0, "创建");
            _vModified = AddInfo(info, 1, 2, "修改");
            _vAccessed = AddInfo(info, 2, 0, "访问");
            _vLines = AddInfo(info, 2, 2, "行数");
            _vAttr = AddInfo(info, 3, 0, "属性");
            _vDisk = AddInfo(info, 3, 2, "磁盘");

            Panel sep = new Panel();
            sep.Dock = DockStyle.Fill;
            sep.BackColor = Theme.BorderSoft;
            sep.Margin = new Padding(0);

            // ---- 预览区 ----
            Panel previewHost = new Panel();
            previewHost.Dock = DockStyle.Fill;
            previewHost.BackColor = Color.White;
            previewHost.Margin = new Padding(0);
            previewHost.Padding = new Padding(14, 8, 8, 4);

            _txtPreview = new TextBox();
            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.WordWrap = false;
            _txtPreview.ScrollBars = ScrollBars.Both;
            _txtPreview.BorderStyle = BorderStyle.None;
            _txtPreview.BackColor = Color.White;
            _txtPreview.ForeColor = Color.FromArgb(31, 41, 55);
            _txtPreview.Font = Theme.Mono(9.5F);
            _txtPreview.TabStop = false;
            previewHost.Controls.Add(_txtPreview);

            _lblNotice = new Label();
            _lblNotice.Dock = DockStyle.Fill;
            _lblNotice.TextAlign = ContentAlignment.MiddleLeft;
            _lblNotice.Padding = new Padding(16, 0, 8, 0);
            _lblNotice.Font = Theme.Ui(8.25F, FontStyle.Regular);
            _lblNotice.ForeColor = Color.FromArgb(180, 120, 40);
            _lblNotice.BackColor = Color.FromArgb(255, 251, 235);
            _lblNotice.Text = "";
            _lblNotice.Margin = new Padding(0);
            _lblNotice.Visible = false;

            // ---- 底部按钮 ----
            Panel foot = new Panel();
            foot.Dock = DockStyle.Fill;
            foot.BackColor = Color.White;
            foot.Margin = new Padding(0);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.WrapContents = false;
            flow.AutoSize = true;
            flow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            flow.Location = new Point(0, 10);
            flow.Margin = new Padding(0);
            flow.BackColor = Color.White;

            Button bCopy = Theme.GhostButton("复制路径", 84, 32);
            bCopy.Click += delegate { Raise(CopyPathRequested); };
            Button bNotepad = Theme.GhostButton("用记事本", 88, 32);
            bNotepad.Click += delegate { Raise(NotepadRequested); };
            Button bFolder = Theme.GhostButton("打开位置", 88, 32);
            bFolder.Click += delegate { Raise(OpenFolderRequested); };
            bCopy.Margin = new Padding(6, 0, 0, 0);
            bNotepad.Margin = new Padding(6, 0, 0, 0);
            bFolder.Margin = new Padding(6, 0, 0, 0);

            flow.Controls.Add(bCopy);
            flow.Controls.Add(bNotepad);
            flow.Controls.Add(bFolder);
            foot.Controls.Add(flow);
            foot.Resize += delegate
            {
                flow.Left = foot.Width - flow.Width - 16;
            };

            root.Controls.Add(head, 0, 0);
            root.Controls.Add(info, 0, 1);
            root.Controls.Add(sep, 0, 2);
            root.Controls.Add(previewHost, 0, 3);
            root.Controls.Add(_lblNotice, 0, 4);
            root.Controls.Add(foot, 0, 5);

            Controls.Add(root);

            _lblEmpty = new Label();
            _lblEmpty.Dock = DockStyle.Fill;
            _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
            _lblEmpty.Font = Theme.Ui(10F, FontStyle.Regular);
            _lblEmpty.ForeColor = Theme.Faint;
            _lblEmpty.BackColor = Color.White;
            _lblEmpty.Text = "从左侧选择一个 .txt 文件\r\n即可查看详细信息与内容";
            _lblEmpty.BringToFront();
            Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();
        }

        private Label AddInfo(TableLayoutPanel t, int row, int col, string caption)
        {
            Label cap = new Label();
            cap.Text = caption;
            cap.Dock = DockStyle.Fill;
            cap.TextAlign = ContentAlignment.MiddleLeft;
            cap.Font = Theme.Ui(8.5F, FontStyle.Regular);
            cap.ForeColor = Theme.Faint;
            cap.Margin = new Padding(0);
            t.Controls.Add(cap, col, row);

            Label val = new Label();
            val.Text = "-";
            val.Dock = DockStyle.Fill;
            val.TextAlign = ContentAlignment.MiddleLeft;
            val.AutoEllipsis = true;
            val.Font = Theme.Ui(9F, FontStyle.Regular);
            val.ForeColor = Theme.Text;
            val.Margin = new Padding(0);
            t.Controls.Add(val, col + 1, row);
            return val;
        }

        private void Raise(EventHandler h)
        {
            if (h != null && _entry != null) h(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _entry = null;
            _txtPreview.Text = "";
            _txtPath.Text = "";
            _lblName.Text = "";
            _vSize.Text = "-";
            _vEncoding.Text = "-";
            _vCreated.Text = "-";
            _vModified.Text = "-";
            _vAccessed.Text = "-";
            _vLines.Text = "-";
            _vAttr.Text = "-";
            _vDisk.Text = "-";
            _btnEdit.Enabled = false;
            _lblNotice.Visible = false;
            _lblEmpty.Visible = true;
            _lblEmpty.BringToFront();
        }

        public void ShowEntry(TextFileEntry e)
        {
            if (e == null)
            {
                Clear();
                return;
            }
            _entry = e;
            _lblEmpty.Visible = false;
            _btnEdit.Enabled = true;

            _lblName.Text = e.FileName;
            _txtPath.Text = e.FullPath;
            _vSize.Text = Fmt.Size(e.Length) + "  ·  " + Fmt.Number(e.Length) + " 字节";
            _vCreated.Text = Fmt.Time(e.Created);
            _vModified.Text = Fmt.Time(e.Modified);
            _vAccessed.Text = Fmt.Time(e.Accessed);
            _vAttr.Text = e.AttributesText();
            try
            {
                _vDisk.Text = Path.GetPathRoot(e.FullPath);
                DriveInfo di = new DriveInfo(_vDisk.Text);
                if (di.IsReady) _vDisk.Text = _vDisk.Text + "  ·  剩余 " + Fmt.Size(di.AvailableFreeSpace);
            }
            catch (Exception)
            {
                _vDisk.Text = "-";
            }

            try
            {
                LoadedText lt = TextCodec.Load(e.FullPath, PreviewBytes);
                _vEncoding.Text = lt.EncodingName;
                _vLines.Text = Fmt.Number(lt.LineCount) + " 行";
                _txtPreview.Text = lt.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (lt.Truncated)
                {
                    _lblNotice.Text = "文件较大（" + Fmt.Size(lt.TotalBytes) + "），此处仅预览前 "
                                      + Fmt.Size(PreviewBytes) + "，点击「编辑」查看全文";
                    _lblNotice.Visible = true;
                }
                else
                {
                    _lblNotice.Visible = false;
                }
                _txtPreview.SelectionStart = 0;
                _txtPreview.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                _vEncoding.Text = "读取失败";
                _vLines.Text = "-";
                _txtPreview.Text = "无法读取该文件：\r\n" + ex.Message;
                _lblNotice.Visible = false;
            }
        }
    }
}

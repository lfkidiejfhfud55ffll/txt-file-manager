using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TxtManager
{
    internal sealed class MainForm : Form
    {
        private const long BigFileWarn = 4L * 1024 * 1024;

        private ScanEngine _engine = new ScanEngine();
        private List<TextFileEntry> _all = new List<TextFileEntry>();
        private List<TextFileEntry> _view = new List<TextFileEntry>();
        private int _scanCursor;
        private bool _finalized = true;
        private string _filter = "";
        private int _sortCol = 2;
        private bool _sortAsc = false;
        private bool _updatingGrid;

        private System.Windows.Forms.Timer _refresh;
        private System.Windows.Forms.Timer _debounce;

        private DataGridView _grid;
        private CueTextBox _search;
        private Button _btnScan;
        private Button _btnStop;
        private CheckBox _chkSystem;
        private SplitContainer _split;
        private PreviewPane _preview;
        private Label _lblCount;
        private Label _lblSize;
        private Label _lblMsg;
        private ThinProgress _progress;
        private Label _gridEmpty;

        public MainForm()
        {
            BuildUi();
            InitTimers();
        }

        // ------------------------------------------------------------------ UI
        private void BuildUi()
        {
            Text = "TXT 文件管理器";
            // 按当前屏幕工作区自适应，避免在小屏/高分屏下超出可视范围
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int wantW = Math.Min(1560, work.Width - 40);
            int wantH = Math.Min(1000, work.Height - 40);
            if (wantW < 900) wantW = Math.Max(860, work.Width - 20);
            if (wantH < 620) wantH = Math.Max(600, work.Height - 20);
            ClientSize = new Size(wantW, wantH);
            MinimumSize = new Size(880, 580);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Ui(9F, FontStyle.Regular);
            Icon = AppIcon.Value;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            KeyPreview = true;

            // ---------------- 头部 ----------------
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 78;
            header.BackColor = Color.White;

            Label title = Theme.MakeLabel("TXT 文件管理器", 15F, FontStyle.Bold, Theme.Text);
            title.Location = new Point(24, 12);
            Label sub = Theme.MakeLabel("全盘索引 · 只读查看 · 就地编辑 · 不复制任何文件", 8.75F, FontStyle.Regular, Theme.Muted);
            sub.Location = new Point(26, 46);

            Label badge = Theme.MakeLabel("本机", 8F, FontStyle.Bold, Color.White);
            badge.AutoSize = false;
            badge.Size = new Size(46, 22);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.BackColor = Theme.Primary;
            badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            header.Controls.Add(title);
            header.Controls.Add(sub);
            header.Controls.Add(badge);
            header.Resize += delegate { badge.Location = new Point(header.Width - 74, 16); };
            header.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            // ---------------- 工具栏 ----------------
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 60;
            bar.BackColor = Color.White;

            _btnScan = Theme.PrimaryButton("开始扫描", 104, 34);
            _btnScan.Location = new Point(24, 13);
            _btnScan.Click += delegate { StartScan(); };

            _btnStop = Theme.GhostButton("停止", 68, 34);
            _btnStop.Location = new Point(138, 13);
            _btnStop.Enabled = false;
            _btnStop.Click += delegate
            {
                _engine.Cancel();
                SetMessage("正在停止扫描…");
            };

            Panel searchWrap = new Panel();
            searchWrap.Location = new Point(228, 13);
            searchWrap.Size = new Size(340, 34);
            searchWrap.BackColor = Color.FromArgb(246, 247, 249);
            searchWrap.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawRectangle(pen, 0, 0, searchWrap.Width - 1, searchWrap.Height - 1);
            };

            _search = new CueTextBox();
            _search.BorderStyle = BorderStyle.None;
            _search.BackColor = Color.FromArgb(246, 247, 249);
            _search.ForeColor = Theme.Text;
            _search.Font = Theme.Ui(9.25F, FontStyle.Regular);
            _search.Location = new Point(10, 8);
            _search.Width = 316;
            _search.Cue = "搜索文件名或路径…";
            _search.TextChanged += delegate { RestartDebounce(); };
            searchWrap.Controls.Add(_search);
            searchWrap.Resize += delegate { _search.Width = searchWrap.Width - 20; };

            _chkSystem = new CheckBox();
            _chkSystem.Text = "包含系统目录";
            _chkSystem.AutoSize = true;
            _chkSystem.Font = Theme.Ui(8.75F, FontStyle.Regular);
            _chkSystem.ForeColor = Theme.Muted;
            _chkSystem.BackColor = Color.White;
            _chkSystem.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _chkSystem.Location = new Point(bar.Width - 140, 24);
            _chkSystem.CheckedChanged += delegate
            {
                SetMessage("设置已更改，点击「开始扫描」后生效");
            };

            bar.Controls.Add(_btnScan);
            bar.Controls.Add(_btnStop);
            bar.Controls.Add(searchWrap);
            bar.Controls.Add(_chkSystem);
            bar.Resize += delegate
            {
                _chkSystem.Location = new Point(bar.Width - _chkSystem.Width - 24, 24);
                searchWrap.Width = Math.Max(200, bar.Width - 228 - _chkSystem.Width - 60);
                _search.Width = searchWrap.Width - 20;
            };
            bar.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            // ---------------- 底部状态栏 ----------------
            Panel status = new Panel();
            status.Dock = DockStyle.Bottom;
            status.Height = 38;
            status.BackColor = Color.White;
            status.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.Border))
                    pe.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
            };

            _lblCount = Theme.MakeLabel("0 个文件", 8.75F, FontStyle.Bold, Theme.Text);
            _lblCount.Location = new Point(24, 11);
            _lblSize = Theme.MakeLabel("合计 0 B", 8.75F, FontStyle.Regular, Theme.Muted);
            _lblSize.Location = new Point(120, 11);

            _lblMsg = Theme.MakeLabel("正在准备…", 8.75F, FontStyle.Regular, Theme.Muted);
            _lblMsg.AutoSize = false;
            _lblMsg.TextAlign = ContentAlignment.MiddleRight;
            _lblMsg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblMsg.Location = new Point(status.Width - 760, 10);
            _lblMsg.Size = new Size(560, 18);

            _progress = new ThinProgress();
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _progress.Size = new Size(150, 6);
            _progress.Location = new Point(status.Width - 190, 16);
            _progress.Visible = false;

            status.Controls.Add(_lblCount);
            status.Controls.Add(_lblSize);
            status.Controls.Add(_lblMsg);
            status.Controls.Add(_progress);
            status.Resize += delegate
            {
                _progress.Location = new Point(status.Width - 190, 16);
                _lblMsg.Location = new Point(status.Width - 780, 10);
            };

            // ---------------- 主区域 ----------------
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Bg;
            body.Padding = new Padding(16, 14, 16, 12);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.SplitterWidth = 12;
            _split.BackColor = Theme.Bg;
            _split.BorderStyle = BorderStyle.None;
            // 必须先给足尺寸，否则设置 Panel2MinSize 会因默认 150px 宽度校验失败
            _split.Size = new Size(1200, 700);
            _split.Panel1MinSize = 420;
            _split.Panel2MinSize = 340;
            _split.FixedPanel = FixedPanel.Panel2;

            Card gridCard = new Card();
            gridCard.Dock = DockStyle.Fill;
            gridCard.Padding = new Padding(1);

            _grid = BuildGrid();
            gridCard.Controls.Add(_grid);

            _gridEmpty = Theme.MakeLabel("尚未扫描", 10F, FontStyle.Regular, Theme.Faint);
            _gridEmpty.AutoSize = false;
            _gridEmpty.Dock = DockStyle.Fill;
            _gridEmpty.TextAlign = ContentAlignment.MiddleCenter;
            _gridEmpty.BackColor = Color.White;
            _gridEmpty.BringToFront();
            gridCard.Controls.Add(_gridEmpty);
            _gridEmpty.BringToFront();

            _split.Panel1.Controls.Add(gridCard);

            Card previewCard = new Card();
            previewCard.Dock = DockStyle.Fill;
            previewCard.Padding = new Padding(1);

            _preview = new PreviewPane();
            _preview.Dock = DockStyle.Fill;
            _preview.EditRequested += delegate { EditCurrent(); };
            _preview.OpenFolderRequested += delegate
            {
                if (_preview.CurrentEntry != null) OpenInExplorer(_preview.CurrentEntry.FullPath);
            };
            _preview.NotepadRequested += delegate
            {
                if (_preview.CurrentEntry != null) OpenWithNotepad(_preview.CurrentEntry.FullPath);
            };
            _preview.CopyPathRequested += delegate
            {
                if (_preview.CurrentEntry != null) CopyText(_preview.CurrentEntry.FullPath);
            };
            previewCard.Controls.Add(_preview);

            _split.Panel2.Controls.Add(previewCard);

            body.Controls.Add(_split);

            Controls.Add(body);
            Controls.Add(status);
            Controls.Add(bar);
            Controls.Add(header);

            Shown += delegate
            {
                ApplySplitter();
                StartScan();
            };

            Load += delegate { ApplySplitter(); };
        }

        /// <summary>主区左右比例：列表约 60%，详情约 40%（详情固定宽度，窗口变宽时由列表吸收）。</summary>
        private void ApplySplitter()
        {
            try
            {
                int w = _split.Width;
                if (w <= 0) return;
                int preview = (int)(w * 0.36);
                if (preview < 420) preview = 420;
                if (preview > w - 460) preview = w - 460;
                int d = w - preview - _split.SplitterWidth;
                if (d < _split.Panel1MinSize) d = _split.Panel1MinSize;
                if (d != _split.SplitterDistance) _split.SplitterDistance = d;
            }
            catch (Exception) { }
        }

        private DataGridView BuildGrid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.VirtualMode = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToOrderColumns = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = true;
            g.ReadOnly = true;
            g.BorderStyle = BorderStyle.None;
            g.BackgroundColor = Color.White;
            g.GridColor = Theme.BorderSoft;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersHeight = 36;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.RowTemplate.Height = 30;
            g.ScrollBars = ScrollBars.Vertical;
            g.StandardTab = true;

            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.HeaderRow;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(88, 96, 108);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.HeaderRow;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.Ui(8.75F, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);

            g.DefaultCellStyle.BackColor = Color.White;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.SelectionBackColor = Theme.Selection;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.DefaultCellStyle.Font = Theme.Ui(9F, FontStyle.Regular);
            g.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.AltRow;
            g.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.Selection;
            g.AlternatingRowsDefaultCellStyle.SelectionForeColor = Theme.Text;

            g.Columns.Add(Col("文件名", 24, false, 180));
            g.Columns.Add(Col("大小", 8, true, 78));
            g.Columns.Add(Col("修改时间", 17, false, 148));
            g.Columns.Add(Col("创建时间", 17, false, 148));
            g.Columns.Add(Col("所在目录", 34, false, 220));

            g.CellValueNeeded += Grid_CellValueNeeded;
            g.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            g.SelectionChanged += Grid_SelectionChanged;
            g.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) EditCurrent();
            };
            g.KeyDown += Grid_KeyDown;
            g.DataError += delegate { };

            EnableDoubleBuffer(g);
            return g;
        }

        private static DataGridViewTextBoxColumn Col(string header, int weight, bool right, int min)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = header;
            c.FillWeight = weight;
            c.MinimumWidth = min;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.SortMode = DataGridViewColumnSortMode.Programmatic;
            c.Resizable = DataGridViewTriState.True;
            c.DefaultCellStyle.Alignment = right
                ? DataGridViewContentAlignment.MiddleRight
                : DataGridViewContentAlignment.MiddleLeft;
            return c;
        }

        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(c, true, null);
            }
            catch (Exception) { }
        }

        private void InitTimers()
        {
            _refresh = new System.Windows.Forms.Timer();
            _refresh.Interval = 260;
            _refresh.Tick += Refresh_Tick;

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 240;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                _filter = _search.Text.Trim();
                ApplyView(true);
            };
        }

        private void RestartDebounce()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        // ------------------------------------------------------------- scanning
        private void StartScan()
        {
            _engine.Cancel();
            _finalized = false;
            _all = new List<TextFileEntry>();
            _view = new List<TextFileEntry>();
            _scanCursor = 0;
            _updatingGrid = true;
            _grid.RowCount = 0;
            _updatingGrid = false;
            _preview.Clear();
            _gridEmpty.Visible = true;
            _gridEmpty.Text = "正在扫描…";
            _gridEmpty.BringToFront();

            _engine = new ScanEngine();
            _engine.IncludeSystemDirs = _chkSystem.Checked;
            _engine.Start(ScanEngine.GetRoots());

            _btnScan.Enabled = false;
            _btnStop.Enabled = true;
            _progress.Visible = true;
            _progress.Marquee = true;
            _refresh.Start();
            SetMessage("正在扫描全部固定磁盘…");
            UpdateSummary();
        }

        private void Refresh_Tick(object sender, EventArgs e)
        {
            if (_engine.IsRunning)
            {
                List<TextFileEntry> fresh = _engine.TakeNew(_scanCursor);
                if (fresh.Count > 0)
                {
                    _scanCursor += fresh.Count;
                    _all.AddRange(fresh);
                    int before = _view.Count;
                    for (int i = 0; i < fresh.Count; i++)
                    {
                        if (Matches(fresh[i])) _view.Add(fresh[i]);
                    }
                    if (_view.Count != before)
                    {
                        _updatingGrid = true;
                        _grid.RowCount = _view.Count;
                        _updatingGrid = false;
                        _gridEmpty.Visible = false;
                    }
                    UpdateSummary();
                }
                string dir = _engine.CurrentDir;
                if (dir.Length > 64) dir = "…" + dir.Substring(dir.Length - 63);
                SetMessage("正在扫描  " + dir + "    目录 " + Fmt.Number(_engine.DirsVisited)
                           + " · 文件 " + Fmt.Number(_all.Count) + " · " + Fmt.Elapsed(_engine.Elapsed));
            }
            else if (_engine.IsFinished && !_finalized)
            {
                FinalizeScan();
            }
        }

        private void FinalizeScan()
        {
            _finalized = true;
            _refresh.Stop();
            List<TextFileEntry> rest = _engine.TakeNew(_scanCursor);
            if (rest.Count > 0)
            {
                _scanCursor += rest.Count;
                _all.AddRange(rest);
            }

            ApplyView(true, true);

            _btnScan.Enabled = true;
            _btnStop.Enabled = false;
            _progress.Marquee = false;
            _progress.Visible = false;
            _gridEmpty.Visible = _view.Count == 0;
            if (_view.Count == 0)
            {
                _gridEmpty.Text = _engine.Cancelled ? "扫描已停止，没有找到 .txt 文件" : "没有找到 .txt 文件";
                _gridEmpty.BringToFront();
            }

            string msg;
            if (_engine.Cancelled)
                msg = "扫描已停止 · 已找到 " + Fmt.Number(_all.Count) + " 个 .txt · 用时 " + Fmt.Elapsed(_engine.Elapsed);
            else
                msg = "扫描完成 · 共 " + Fmt.Number(_all.Count) + " 个 .txt · 用时 " + Fmt.Elapsed(_engine.Elapsed)
                      + " · 跳过 " + Fmt.Number(_engine.DirsSkipped) + " 个系统/无权限目录";
            SetMessage(msg);
            UpdateSummary();

            if (_view.Count > 0)
            {
                _updatingGrid = true;
                try
                {
                    _grid.ClearSelection();
                    _grid.CurrentCell = _grid.Rows[0].Cells[0];
                    _grid.Rows[0].Selected = true;
                    _grid.FirstDisplayedScrollingRowIndex = 0;
                }
                catch (Exception) { }
                _updatingGrid = false;
                if (_preview.CurrentEntry != _view[0]) _preview.ShowEntry(_view[0]);
            }

            if (!string.IsNullOrEmpty(Program.AutoShotPath))
            {
                string path = Program.AutoShotPath;
                BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(Width, Height))
                        {
                            DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, Width, Height));
                            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    catch (Exception) { }
                    Close();
                });
            }
        }

        // --------------------------------------------------------------- view
        private bool Matches(TextFileEntry e)
        {
            if (_filter.Length == 0) return true;
            return e.FileName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || e.DirectoryPath.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyView(bool keepSelection)
        {
            ApplyView(keepSelection, false);
        }

        // rebuild=true 时强制重建行：虚拟模式下 RowCount 不变时单元格会沿用旧值缓存，
        // 排序/过滤之后必须重建一次，屏幕上才会立刻变成新顺序。
        private void ApplyView(bool keepSelection, bool rebuild)
        {
            string keepPath = null;
            if (keepSelection)
            {
                try
                {
                    if (_grid.CurrentRow != null && _grid.CurrentRow.Index < _view.Count)
                        keepPath = _view[_grid.CurrentRow.Index].FullPath;
                }
                catch (Exception) { }
            }

            List<TextFileEntry> list;
            if (_filter.Length == 0)
            {
                list = new List<TextFileEntry>(_all);
            }
            else
            {
                list = new List<TextFileEntry>();
                for (int i = 0; i < _all.Count; i++)
                {
                    if (Matches(_all[i])) list.Add(_all[i]);
                }
            }

            Comparison<TextFileEntry> cmp = ComparerFor(_sortCol);
            list.Sort(cmp);
            if (!_sortAsc) list.Reverse();
            _view = list;

            _updatingGrid = true;
            if (rebuild && _grid.RowCount == _view.Count)
            {
                _grid.RowCount = 0;
            }
            _grid.RowCount = _view.Count;
            _updatingGrid = false;
            _grid.Refresh();

            for (int i = 0; i < _grid.Columns.Count; i++)
            {
                _grid.Columns[i].HeaderCell.SortGlyphDirection =
                    i == _sortCol ? (_sortAsc ? SortOrder.Ascending : SortOrder.Descending) : SortOrder.None;
            }

            _gridEmpty.Visible = _view.Count == 0;
            if (_view.Count == 0)
            {
                _gridEmpty.Text = _all.Count == 0
                    ? "没有找到 .txt 文件，点击左上角「开始扫描」重新检索"
                    : "没有匹配「" + _filter + "」的文件";
                _gridEmpty.BringToFront();
            }

            if (keepPath != null)
            {
                int idx = _view.FindIndex(delegate(TextFileEntry x)
                {
                    return string.Equals(x.FullPath, keepPath, StringComparison.OrdinalIgnoreCase);
                });
                if (idx >= 0)
                {
                    _updatingGrid = true;
                    try
                    {
                        _grid.ClearSelection();
                        _grid.Rows[idx].Selected = true;
                        _grid.CurrentCell = _grid.Rows[idx].Cells[0];
                        _grid.FirstDisplayedScrollingRowIndex = idx;
                    }
                    catch (Exception) { }
                    _updatingGrid = false;
                }
            }

            // 重新排序/过滤后回到顶部，否则会停在"旧选中行在新顺序里的位置"，
            // 看起来像是没排序（其实是滚到中间了）。
            if (rebuild || !keepSelection)
            {
                try { _grid.FirstDisplayedScrollingRowIndex = 0; }
                catch (Exception) { }
            }

            UpdateSummary();
        }

        private static Comparison<TextFileEntry> ComparerFor(int col)
        {
            switch (col)
            {
                case 0:
                    return delegate(TextFileEntry a, TextFileEntry b)
                    {
                        return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                    };
                case 1:
                    return delegate(TextFileEntry a, TextFileEntry b) { return a.Length.CompareTo(b.Length); };
                case 2:
                    return delegate(TextFileEntry a, TextFileEntry b) { return a.Modified.CompareTo(b.Modified); };
                case 3:
                    return delegate(TextFileEntry a, TextFileEntry b) { return a.Created.CompareTo(b.Created); };
                default:
                    return delegate(TextFileEntry a, TextFileEntry b)
                    {
                        return string.Compare(a.DirectoryPath, b.DirectoryPath, StringComparison.OrdinalIgnoreCase);
                    };
            }
        }

        private void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            TextFileEntry it = _view[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = it.FileName; break;
                case 1: e.Value = Fmt.Size(it.Length); break;
                case 2: e.Value = Fmt.TimeShort(it.Modified); break;
                case 3: e.Value = Fmt.TimeShort(it.Created); break;
                case 4: e.Value = it.DirectoryPath; break;
            }
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            if (e.ColumnIndex == _sortCol) _sortAsc = !_sortAsc;
            else
            {
                _sortCol = e.ColumnIndex;
                _sortAsc = e.ColumnIndex == 0 || e.ColumnIndex == 4;
            }
            ApplyView(true, true);
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_updatingGrid) return;
            try
            {
                if (_grid.CurrentRow == null) return;
                int idx = _grid.CurrentRow.Index;
                if (idx >= 0 && idx < _view.Count) _preview.ShowEntry(_view[idx]);
            }
            catch (Exception) { }
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditCurrent();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedPaths();
                e.Handled = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                StartScan();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                _search.Focus();
                _search.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CopySelectedPaths()
        {
            try
            {
                if (_grid.SelectedRows.Count == 0) return;
                List<string> paths = new List<string>();
                for (int i = 0; i < _grid.SelectedRows.Count; i++)
                {
                    int idx = _grid.SelectedRows[i].Index;
                    if (idx >= 0 && idx < _view.Count) paths.Add(_view[idx].FullPath);
                }
                if (paths.Count > 0) CopyText(string.Join("\r\n", paths.ToArray()));
            }
            catch (Exception) { }
        }

        private void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                SetMessage("已复制到剪贴板");
            }
            catch (Exception) { SetMessage("复制失败（剪贴板被占用）"); }
        }

        // ------------------------------------------------------------- actions
        private void EditCurrent()
        {
            TextFileEntry e = CurrentEntry();
            if (e == null) return;
            try
            {
                if (!File.Exists(e.FullPath))
                {
                    MessageBox.Show(this, "文件已不存在：\r\n" + e.FullPath, "打开失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (e.Length > BigFileWarn)
                {
                    DialogResult r = MessageBox.Show(this,
                        "该文件约 " + Fmt.Size(e.Length) + "，用内置编辑器打开可能较慢。\r\n是否继续？",
                        "大文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                }
                using (EditorForm f = new EditorForm(e))
                {
                    f.ShowDialog(this);
                    if (f.Saved && f.Result != null) ReplaceEntry(f.Result);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开编辑器：\r\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private TextFileEntry CurrentEntry()
        {
            try
            {
                if (_grid.CurrentRow == null) return null;
                int idx = _grid.CurrentRow.Index;
                if (idx < 0 || idx >= _view.Count) return null;
                return _view[idx];
            }
            catch (Exception) { return null; }
        }

        private void ReplaceEntry(TextFileEntry fresh)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].FullPath, fresh.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _all[i] = fresh;
                    break;
                }
            }
            for (int i = 0; i < _view.Count; i++)
            {
                if (string.Equals(_view[i].FullPath, fresh.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _view[i] = fresh;
                    _grid.InvalidateRow(i);
                    break;
                }
            }
            _preview.ShowEntry(fresh);
            SetMessage("已保存：" + fresh.FileName);
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开资源管理器：\r\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenWithNotepad(string path)
        {
            try
            {
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法启动记事本：\r\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetMessage(string text)
        {
            if (IsDisposed) return;
            _lblMsg.Text = text;
        }

        private void UpdateSummary()
        {
            long total = 0;
            for (int i = 0; i < _view.Count; i++) total += _view[i].Length;
            _lblCount.Text = Fmt.Number(_view.Count) + " 个文件";
            _lblSize.Text = "合计 " + Fmt.Size(total)
                            + (_view.Count != _all.Count ? "  /  索引 " + Fmt.Number(_all.Count) : "");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _engine.Cancel(); } catch (Exception) { }
            base.OnFormClosing(e);
        }
    }
}

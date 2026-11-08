using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TxtManager
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(245, 246, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 229, 234);
        public static readonly Color BorderSoft = Color.FromArgb(240, 242, 245);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(110, 118, 129);
        public static readonly Color Faint = Color.FromArgb(156, 163, 175);
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color Selection = Color.FromArgb(233, 240, 254);
        public static readonly Color HeaderRow = Color.FromArgb(249, 250, 251);
        public static readonly Color AltRow = Color.FromArgb(252, 253, 254);

        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Button PrimaryButton(string text, int w, int h)
        {
            Button b = BaseButton(text, Theme.Primary, Color.White, w, h);
            b.Font = Ui(9F, FontStyle.Bold);
            return b;
        }

        public static Button GhostButton(string text, int w, int h)
        {
            Button b = BaseButton(text, Color.White, Theme.Text, w, h);
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 248, 251);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(238, 241, 246);
            return b;
        }

        private static Button BaseButton(string text, Color back, Color fore, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = w;
            b.Height = h;
            b.BackColor = back;
            b.ForeColor = fore;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Mix(back, Color.Black, 0.07);
            b.FlatAppearance.MouseDownBackColor = Mix(back, Color.Black, 0.14);
            b.Font = Ui(9F, FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.TabStop = false;
            return b;
        }

        public static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Ui(size, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }
    }

    /// <summary>白底卡片，带 1px 边框。</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>1px 分隔线。</summary>
    internal sealed class Separator : Panel
    {
        public Separator()
        {
            Height = 1;
            Dock = DockStyle.Top;
            BackColor = Theme.Border;
            TabStop = false;
        }
    }

    /// <summary>带占位提示的输入框（EM_SETCUEBANNER）。</summary>
    internal sealed class CueTextBox : TextBox
    {
        private const int EM_SETCUEBANNER = 0x1501;
        private string _cue = "";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string Cue
        {
            get { return _cue; }
            set
            {
                _cue = value;
                ApplyCue();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCue();
        }

        private void ApplyCue()
        {
            if (IsHandleCreated && !string.IsNullOrEmpty(_cue))
            {
                SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, _cue);
            }
        }
    }

    /// <summary>细进度条（自绘，避免系统主题的粗边框）。</summary>
    internal sealed class ThinProgress : Control
    {
        private int _value;
        private bool _marquee;
        private int _offset;
        private readonly Timer _timer;

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 6;
            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate
            {
                _offset += 6;
                if (_offset > Width + 120) _offset = -120;
                Invalidate();
            };
        }

        public bool Marquee
        {
            get { return _marquee; }
            set
            {
                _marquee = value;
                if (value) _timer.Start();
                else _timer.Stop();
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                _value = value < 0 ? 0 : (value > 100 ? 100 : value);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(232, 236, 242)))
                g.FillRectangle(bg, r);
            if (_marquee)
            {
                int w = Math.Max(70, Width / 4);
                int x = _offset;
                using (SolidBrush fg = new SolidBrush(Theme.Primary))
                    g.FillRectangle(fg, new Rectangle(x, 0, w, Height));
            }
            else if (_value > 0)
            {
                int w = (int)(Width * (_value / 100.0));
                using (SolidBrush fg = new SolidBrush(Theme.Primary))
                    g.FillRectangle(fg, new Rectangle(0, 0, w, Height));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace KnobRing
{
    // Colours and fonts for the settings window (G HUB-style dark)
    internal static class Th
    {
        public static readonly Color Bg = Hex("#0A0B0E"), Bar = Hex("#0E1014"), Line = Hex("#1C1F27"),
            Field = Hex("#14161C"), Btn = Hex("#1A1D25"), BtnHot = Hex("#232733"), BtnLine = Hex("#2A2E38"),
            Text = Hex("#E6E8EE"), Muted = Hex("#8A90A0"), Sub = Hex("#B8BDC9"), Soft = Hex("#C9CDD8"),
            Danger = Hex("#F2A7A7"), DangerLine = Hex("#3A2A2E"), RowSel = Hex("#191C25"), Hover = Hex("#151820"),
            Good = Hex("#2BD67B"), Warn = Hex("#F2B441"), Bad = Hex("#E5484D");

        public static Color Hex(string h) { return ColorTranslator.FromHtml(h); }

        static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        static string _head;

        public static string HeadFamilyName { get { return HeadFamily; } }

        static string HeadFamily
        {
            get
            {
                if (_head == null)
                {
                    _head = "Segoe UI Semibold";
                    try
                    {
                        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        using (var fc = new InstalledFontCollection())
                            foreach (var f in fc.Families) names.Add(f.Name);
                        foreach (var n in new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift SemiBold", "Bahnschrift" })
                            if (names.Contains(n)) { _head = n; break; }
                    }
                    catch { }
                }
                return _head;
            }
        }

        static Font F(string family, float px, FontStyle st)
        {
            string key = family + "|" + px + "|" + st;
            Font f;
            if (!_fonts.TryGetValue(key, out f))
            {
                try { f = new Font(family, px, st, GraphicsUnit.Pixel); }
                catch { f = new Font("Segoe UI", px, st, GraphicsUnit.Pixel); }
                _fonts[key] = f;
            }
            return f;
        }

        public static Font Head(float px) { return F(HeadFamily, px, FontStyle.Regular); }
        public static Font Body(float px) { return F("Segoe UI", px, FontStyle.Regular); }
        public static Font Bold(float px) { return F("Segoe UI Semibold", px, FontStyle.Regular); }
        public static Font Icon(float px) { return F(Icons.Family, px, FontStyle.Regular); }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    // A control that paints the whole UI each frame and keeps a list of clickable areas.
    internal sealed class Surface : Control
    {
        struct Hit { public RectangleF R; public Action Click; public Action Right; }

        public Action<Ui> Painter;
        public Action<PointF, int> Wheel;
        public Func<PointF, bool> DragStart;
        public Action<PointF> DragMove;
        public Func<Keys, bool> KeyHandler;
        public float S = 1f;
        public PointF MouseLogical;
        public bool HasMouse;

        readonly List<Hit> _hits = new List<Hit>();
        bool _dragging;
        int _pressed = -1;
        RectangleF _pressedRect, _hoverRect;

        public Surface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Th.Bg;
        }

        internal void AddHit(RectangleF r, Action click, Action right)
        {
            _hits.Add(new Hit { R = r, Click = click, Right = right });
        }

        int Find(PointF p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].R.Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Th.Bg);
            g.ScaleTransform(S, S);
            _hits.Clear();
            if (Painter != null)
            {
                try { Painter(new Ui(this, g)); }
                catch (Exception ex) { Log.Write(ex); }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MouseLogical = new PointF(e.X / S, e.Y / S);
            HasMouse = true;
            if (_dragging) { if (DragMove != null) DragMove(MouseLogical); return; }
            int i = Find(MouseLogical);
            var r = i >= 0 ? _hits[i].R : RectangleF.Empty;
            Cursor = i >= 0 && _hits[i].Click != null ? Cursors.Hand : Cursors.Default;
            if (r != _hoverRect) { _hoverRect = r; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HasMouse = false;
            _hoverRect = RectangleF.Empty;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            MouseLogical = new PointF(e.X / S, e.Y / S);
            if (e.Button == MouseButtons.Left && DragStart != null && DragStart(MouseLogical))
            {
                _dragging = true;
                Capture = true;
                return;
            }
            _pressed = Find(MouseLogical);
            _pressedRect = _pressed >= 0 ? _hits[_pressed].R : RectangleF.Empty;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging) { _dragging = false; Capture = false; Invalidate(); return; }
            MouseLogical = new PointF(e.X / S, e.Y / S);
            int i = Find(MouseLogical);
            if (i >= 0 && _hits[i].R == _pressedRect)
            {
                var h = _hits[i];
                if (e.Button == MouseButtons.Left && h.Click != null) h.Click();
                else if (e.Button == MouseButtons.Right && h.Right != null) h.Right();
            }
            _pressed = -1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Wheel != null) Wheel(new PointF(e.X / S, e.Y / S), e.Delta);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (KeyHandler != null && KeyHandler(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool IsInputKey(Keys keyData) { return KeyHandler != null || base.IsInputKey(keyData); }
    }

    // Drawing helpers, used during one paint
    internal sealed class Ui
    {
        public readonly Graphics G;
        readonly Surface _s;
        public static Color Accent = Color.FromArgb(47, 107, 255);

        public Ui(Surface s, Graphics g) { _s = s; G = g; }

        public PointF Mouse { get { return _s.MouseLogical; } }
        public bool Hot(RectangleF r) { return _s.HasMouse && r.Contains(_s.MouseLogical); }
        public void Hit(RectangleF r, Action click, Action right = null) { _s.AddHit(r, click, right); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Fill(RectangleF r, Color c, float rad = 0)
        {
            using (var b = new SolidBrush(c))
            {
                if (rad <= 0) G.FillRectangle(b, r);
                else using (var p = Round(r, rad)) G.FillPath(b, p);
            }
        }

        public void Stroke(RectangleF r, Color c, float rad = 0, float w = 1, bool dash = false)
        {
            var rr = new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w);
            using (var pen = new Pen(c, w))
            {
                if (dash) pen.DashPattern = new float[] { 4, 3 };
                using (var p = Round(rr, rad)) G.DrawPath(pen, p);
            }
        }

        public void Circle(float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c)) G.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        public void Line(float x1, float y1, float x2, float y2, Color c, float w = 1)
        {
            using (var pen = new Pen(c, w)) G.DrawLine(pen, x1, y1, x2, y2);
        }

        public void Text(string t, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near,
                         StringAlignment v = StringAlignment.Center, bool wrap = false)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat())
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) sf.FormatFlags |= StringFormatFlags.NoWrap;
                G.DrawString(t ?? "", f, b, r, sf);
            }
        }

        public void TextAt(string t, Font f, Color c, float x, float y)
        {
            using (var b = new SolidBrush(c)) G.DrawString(t ?? "", f, b, x, y);
        }

        public SizeF Measure(string t, Font f)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                return G.MeasureString(t ?? "", f, 10000, sf);
            }
        }

        public void Glyph(string name, float px, Color c, RectangleF r)
        {
            if (VectorIcons.Draw(G, name, r.X + r.Width / 2f, r.Y + r.Height / 2f, px * 1.3f, c, 1.7f)) return;
            char ch;
            if (!Icons.Map.TryGetValue(name ?? "", out ch)) return;
            Text(ch.ToString(), Th.Icon(px), c, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), StringAlignment.Center);
        }

        // Label in small caps style above a field
        public void Caption(string t, float x, float y, float w = 300)
        {
            Text(t.ToUpperInvariant(), Th.Bold(11.5f), Th.Muted, new RectangleF(x, y, w, 18), StringAlignment.Near, StringAlignment.Center);
        }

        public enum B { Normal, Primary, Ghost, Danger, Toggle }

        public void Button(RectangleF r, string text, B kind, Action click, bool on = false, string glyph = null, float rad = 8)
        {
            bool hot = Hot(r) && click != null;
            Color fill, line, fg;
            switch (kind)
            {
                case B.Primary:
                    fill = hot ? Th.Mix(Accent, Color.White, 0.12f) : Accent; line = fill; fg = Color.White; break;
                case B.Ghost:
                    fill = hot ? Th.Hover : Color.Transparent; line = Th.Hex("#343946"); fg = Th.Sub; break;
                case B.Danger:
                    fill = hot ? Th.Hex("#1E1416") : Color.Transparent; line = Th.DangerLine; fg = Th.Danger; break;
                case B.Toggle:
                    if (on) { fill = Color.FromArgb(hot ? 60 : 38, Accent); line = Accent; fg = Color.White; }
                    else { fill = hot ? Th.Btn : Th.Field; line = hot ? Th.Hex("#3A4050") : Th.BtnLine; fg = Th.Soft; }
                    break;
                default:
                    fill = hot ? Th.BtnHot : Th.Btn; line = Th.BtnLine; fg = Th.Text; break;
            }
            if (click == null && kind != B.Toggle) fg = Th.Muted;
            if (fill.A > 0) Fill(r, fill, rad);
            Stroke(r, line, rad, 1, kind == B.Ghost);

            var font = Th.Bold(13.5f);
            if (glyph != null)
            {
                float tw = string.IsNullOrEmpty(text) ? 0 : Measure(text, font).Width + 8;
                float total = 18 + tw;
                float x = r.X + (r.Width - total) / 2;
                Glyph(glyph, 15, fg, new RectangleF(x, r.Y, 18, r.Height));
                if (tw > 0) Text(text, font, fg, new RectangleF(x + 26, r.Y, tw + 4, r.Height));
            }
            else Text(text, font, fg, r, StringAlignment.Center);
            if (click != null) Hit(r, click);
        }

        // On/off switch, drawn at (x, y) 44x24
        public void Switch(float x, float y, bool on)
        {
            Fill(new RectangleF(x, y, 44, 24), on ? Accent : Th.BtnLine, 12);
            Circle(on ? x + 32 : x + 12, y + 12, 9, Color.White);
        }
    }

    // TextBox styled for the dark UI; the Surface draws the rounded box around it
    internal sealed class DarkTextBox : TextBox
    {
        string _cue = "";
        public DarkTextBox()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Th.Field;
            ForeColor = Th.Text;
        }

        public string Cue
        {
            get { return _cue; }
            set { _cue = value ?? ""; if (IsHandleCreated) ApplyCue(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCue();
        }

        void ApplyCue()
        {
            try { Native.SendMessage(Handle, Native.EM_SETCUEBANNER, new IntPtr(1), _cue); } catch { }
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Th.Text : Th.Muted;
            base.OnRenderItemText(e);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Th.Hex("#16181E"); } }
            public override Color ImageMarginGradientBegin { get { return Th.Hex("#16181E"); } }
            public override Color ImageMarginGradientMiddle { get { return Th.Hex("#16181E"); } }
            public override Color ImageMarginGradientEnd { get { return Th.Hex("#16181E"); } }
            public override Color MenuBorder { get { return Th.BtnLine; } }
            public override Color MenuItemBorder { get { return Th.Hex("#2A2E38"); } }
            public override Color MenuItemSelected { get { return Th.Hex("#232733"); } }
            public override Color MenuItemSelectedGradientBegin { get { return Th.Hex("#232733"); } }
            public override Color MenuItemSelectedGradientEnd { get { return Th.Hex("#232733"); } }
            public override Color SeparatorDark { get { return Th.BtnLine; } }
            public override Color SeparatorLight { get { return Th.Hex("#16181E"); } }
        }
    }
}

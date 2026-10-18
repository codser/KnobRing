using System;
using System.Drawing;
using System.Windows.Forms;

namespace KnobRing
{
    // A see-through, always-on-top window that never takes focus, so keys still go to the app behind it.
    internal sealed class RingWindow : Form
    {
        public readonly RingRenderer Renderer = new RingRenderer();
        readonly Timer _anim = new Timer { Interval = 8 };
        DateTime _phaseStart;
        bool _closing;
        Action _afterClose;
        int _x, _y;
        Point _mouseAt;          // last mouse position that counted
        bool _mouseActive;       // has the mouse really moved since the ring opened / the knob was turned?

        public bool IsShown { get; private set; }

        public RingWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(1, 1);
            _anim.Tick += OnTick;
            var h = Handle;   // create the window now
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        public Rectangle ScreenBounds { get { int s = Renderer.PixelSize; return new Rectangle(_x, _y, s, s); } }

        // Opens the ring centred on a screen point
        public void ShowAt(Point center)
        {
            _closing = false;
            _afterClose = null;
            Renderer.Scale = Native.DpiScaleAt(center.X, center.Y) * (float)Program.Config.Size;
            Renderer.ResetAnimation();
            Renderer.Open = 1f;
            Renderer.IntroT = 0f;
            Renderer.HubHover = false;
            int size = Renderer.PixelSize;
            var wa = Screen.FromPoint(center).WorkingArea;
            _x = Math.Max(wa.Left, Math.Min(wa.Right - size, center.X - size / 2));
            _y = Math.Max(wa.Top, Math.Min(wa.Bottom - size, center.Y - size / 2));
            _mouseAt = Cursor.Position;
            _mouseActive = false;
            Present();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _x, _y, size, size, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            IsShown = true;
            Native.FastTimers(true);
            _phaseStart = DateTime.Now;
            _anim.Start();
        }

        // Swap in new items in place (sub-rings), replaying the pop-out
        public void Replay()
        {
            Renderer.ResetAnimation();
            Renderer.Open = 1f;
            Renderer.IntroT = 0f;
            _phaseStart = DateTime.Now;
            _closing = false;
            _anim.Start();
        }

        public void Refresh2() { if (IsShown) _anim.Start(); }

        // The knob was used: it owns the highlight until the mouse really moves again.
        public void KnobUsed()
        {
            _mouseAt = Cursor.Position;
            _mouseActive = false;
            Renderer.HubHover = false;
        }

        public void CloseRing(bool fast, Action afterClose)
        {
            if (!IsShown) { if (afterClose != null) afterClose(); return; }
            _afterClose = afterClose;
            if (fast) { Finish(); return; }
            _closing = true;
            _phaseStart = DateTime.Now;
            _anim.Start();
        }

        void Finish()
        {
            _anim.Stop();
            Native.FastTimers(false);
            IsShown = false;
            _closing = false;
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE | 0x80 /*HIDE*/);
            var a = _afterClose; _afterClose = null;
            if (a != null) a();
        }

        void OnTick(object sender, EventArgs e)
        {
            double ms = (DateTime.Now - _phaseStart).TotalMilliseconds;
            if (_closing)
            {
                Renderer.Open = (float)Math.Max(0, 1 - ms / 120.0);
                Present((byte)(255 * Renderer.Open));
                if (Renderer.Open <= 0) Finish();
                return;
            }
            Renderer.IntroT = (float)(ms / 1000.0);
            Renderer.Open = 1f;
            bool moving = Renderer.Step();
            Present();
            if (!moving && Renderer.IntroT >= Renderer.IntroDuration) _anim.Stop();
        }

        void Present(byte alpha = 255)
        {
            using (var bmp = Renderer.Render())
            {
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                IntPtr memDc = Native.CreateCompatibleDC(screenDc);
                IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
                try
                {
                    hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                    old = Native.SelectObject(memDc, hBmp);
                    var size = new Native.SIZE(bmp.Width, bmp.Height);
                    var src = new Native.POINT();
                    var dst = new Native.POINT { X = _x, Y = _y };
                    var blend = new Native.BLENDFUNCTION
                    {
                        BlendOp = Native.AC_SRC_OVER, BlendFlags = 0,
                        SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA
                    };
                    Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
                    if (hBmp != IntPtr.Zero) { Native.SelectObject(memDc, old); Native.DeleteObject(hBmp); }
                    Native.DeleteDC(memDc);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim.Dispose(); Renderer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

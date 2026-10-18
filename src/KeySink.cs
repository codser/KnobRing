using System;
using System.Drawing;
using System.Windows.Forms;

namespace KnobRing
{
    // An invisible window that takes keyboard focus while the ring is open. Windows sends volume and media
    // commands (WM_APPCOMMAND) to the window in front; this one swallows them, so the knob can't touch the volume.
    internal sealed class KeySink : Form
    {
        const int WM_APPCOMMAND = 0x0319, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        int _logged;

        public KeySink()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
            Text = "KnobRing";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;   // not in Alt+Tab
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_APPCOMMAND)
            {
                if (_logged++ < 10) Log.Write("Ring open: blocked app command " + ((m.LParam.ToInt64() >> 16) & 0x0FFF));
                m.Result = new IntPtr(1);   // handled: Windows won't change the volume
                return;
            }
            if (m.Msg == WM_KEYDOWN || m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYDOWN || m.Msg == WM_SYSKEYUP)
            {
                long vk = m.WParam.ToInt64();
                if (vk >= 0xAD && vk <= 0xB3) { m.Result = IntPtr.Zero; return; }   // volume and media keys
            }
            base.WndProc(ref m);
        }
    }
}

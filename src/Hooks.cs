using System;
using System.Runtime.InteropServices;

namespace KnobRing
{
    // Low-level keyboard hook. The handler returns true to swallow the key.
    internal sealed class KeyboardHook : IDisposable
    {
        public delegate bool KeyHandler(int vk, bool down);
        readonly KeyHandler _handler;
        readonly Native.HookProc _proc;   // keep a reference so the GC doesn't collect it
        IntPtr _hook = IntPtr.Zero;

        public KeyboardHook(KeyHandler handler)
        {
            _handler = handler;
            _proc = Callback;
        }

        public bool Install()
        {
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            return _hook != IntPtr.Zero;
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if ((k.flags & Native.LLKHF_INJECTED) == 0)   // ignore keys we send ourselves
                {
                    int msg = wParam.ToInt32();
                    bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                    bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                    if (down || up)
                    {
                        bool swallow = false;
                        try { swallow = _handler((int)k.vkCode, down); } catch (Exception ex) { Log.Write(ex); }
                        if (swallow) return new IntPtr(1);
                    }
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }

    // Low-level mouse hook, installed while the ring is open. The handler returns true to swallow the event.
    internal sealed class MouseHook : IDisposable
    {
        public delegate bool MouseHandler(int msg, int x, int y, int wheel);
        readonly MouseHandler _handler;
        readonly Native.HookProc _proc;
        IntPtr _hook = IntPtr.Zero;

        public MouseHook(MouseHandler handler) { _handler = handler; _proc = Callback; }

        public bool Installed { get { return _hook != IntPtr.Zero; } }

        public void Install()
        {
            if (_hook == IntPtr.Zero)
                _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        }

        public void Uninstall()
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if ((m.flags & Native.LLMHF_INJECTED) == 0)
                {
                    bool swallow = false;
                    try { swallow = _handler(wParam.ToInt32(), m.pt.X, m.pt.Y, (short)(m.mouseData >> 16)); }
                    catch (Exception ex) { Log.Write(ex); }
                    if (swallow) return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose() { Uninstall(); }
    }

    internal static class Log
    {
        public static void Write(object o)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppConfig.Folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppConfig.Folder, "log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + o + Environment.NewLine);
            }
            catch { }
        }
    }
}

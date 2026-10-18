using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KnobRing
{
    // Listens to "consumer control" HID reports (volume / mute / media keys) from every keyboard,
    // even in the background. Many keyboards send their knob this way, which a normal keyboard hook never sees.
    // Raw input can't block the key, so Windows still acts on it; AudioGuard undoes volume/mute changes.
    internal sealed class RawKnob : NativeWindow, IDisposable
    {
        public delegate void UsageHandler(int code, bool down);
        readonly UsageHandler _handler;
        readonly Dictionary<IntPtr, IntPtr> _preparsed = new Dictionary<IntPtr, IntPtr>();
        readonly Dictionary<IntPtr, HashSet<ushort>> _pressed = new Dictionary<IntPtr, HashSet<ushort>>();
        public bool Registered { get; private set; }

        const int WM_INPUT = 0x00FF;
        const uint RIDEV_INPUTSINK = 0x100;
        const uint RID_INPUT = 0x10000003;
        const uint RIDI_PREPARSEDDATA = 0x20000005;
        const uint RIM_TYPEHID = 2;
        const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, wParam; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll")]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("hid.dll")]
        static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [In, Out] ushort[] usages,
                                         ref uint usageLength, IntPtr preparsed, byte[] report, uint reportLength);

        public RawKnob(UsageHandler handler)
        {
            _handler = handler;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });   // message-only window
            var dev = new[] { new RAWINPUTDEVICE { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK, Target = Handle } };
            Registered = RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            if (!Registered) Log.Write("Raw input registration failed: " + Marshal.GetLastWin32Error());
        }

        // Consumer-control usage -> the key code KnobRing uses for it (matches the normal VK codes where one exists)
        public static int CodeFor(ushort usage)
        {
            switch (usage)
            {
                case 0xE9: return 0xAF;   // volume up
                case 0xEA: return 0xAE;   // volume down
                case 0xE2: return 0xAD;   // mute
                case 0xCD: return 0xB3;   // play/pause
                case 0xB5: return 0xB0;   // next
                case 0xB6: return 0xB1;   // previous
                case 0xB7: return 0xB2;   // stop
                default: return 0x1000 | usage;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { OnRawInput(m.LParam); } catch (Exception ex) { Log.Write(ex); }
            }
            base.WndProc(ref m);
        }

        void OnRawInput(IntPtr hRaw)
        {
            uint hdr = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(hRaw, RID_INPUT, buf, ref size, hdr) != size) return;
                var h = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (h.Type != RIM_TYPEHID) return;
                IntPtr p = new IntPtr(buf.ToInt64() + hdr);
                int sizeHid = Marshal.ReadInt32(p);
                int count = Marshal.ReadInt32(p, 4);
                IntPtr data = new IntPtr(p.ToInt64() + 8);
                IntPtr pre = Preparsed(h.Device);

                for (int r = 0; r < count; r++)
                {
                    var report = new byte[sizeHid];
                    Marshal.Copy(new IntPtr(data.ToInt64() + r * sizeHid), report, 0, sizeHid);
                    var now = new HashSet<ushort>();
                    if (pre != IntPtr.Zero)
                    {
                        var usages = new ushort[32];
                        uint len = (uint)usages.Length;
                        if (HidP_GetUsages(0, 0x0C, 0, usages, ref len, pre, report, (uint)report.Length) == HIDP_STATUS_SUCCESS)
                            for (int i = 0; i < len; i++) if (usages[i] != 0) now.Add(usages[i]);
                    }
                    else if (sizeHid >= 3)
                    {
                        // No descriptor info: assume the common layout [reportId, usage lo, usage hi]
                        ushort u = (ushort)(report[1] | (report[2] << 8));
                        if (u != 0) now.Add(u);
                    }
                    Diff(h.Device, now);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void Diff(IntPtr device, HashSet<ushort> now)
        {
            HashSet<ushort> before;
            if (!_pressed.TryGetValue(device, out before)) before = new HashSet<ushort>();
            foreach (var u in now) if (!before.Contains(u)) _handler(CodeFor(u), true);
            foreach (var u in before) if (!now.Contains(u)) _handler(CodeFor(u), false);
            _pressed[device] = now;
        }

        IntPtr Preparsed(IntPtr device)
        {
            IntPtr pre;
            if (_preparsed.TryGetValue(device, out pre)) return pre;
            pre = IntPtr.Zero;
            try
            {
                uint size = 0;
                GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
                if (size > 0)
                {
                    pre = Marshal.AllocHGlobal((int)size);
                    if ((int)GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, pre, ref size) <= 0)
                    {
                        Marshal.FreeHGlobal(pre);
                        pre = IntPtr.Zero;
                    }
                }
            }
            catch { pre = IntPtr.Zero; }
            _preparsed[device] = pre;
            return pre;
        }

        public void Dispose()
        {
            foreach (var p in _preparsed.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            _preparsed.Clear();
            DestroyHandle();
        }
    }

    // Windows still changes the volume / mute for knob events that arrive as media keys.
    // While KnobRing is using the knob, this puts volume and mute back to what they were just before.
    internal sealed class AudioGuard : IDisposable
    {
        struct Sample { public DateTime At; public float Vol; public bool Mute; }

        readonly List<Sample> _history = new List<Sample>();
        readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer { Interval = 80 };
        IAudioEndpointVolume _ep;
        bool _holding;
        Sample _snap;
        DateTime _holdUntil;
        readonly Func<bool> _keepHolding;

        public AudioGuard(Func<bool> keepHolding)
        {
            _keepHolding = keepHolding;
            _poll.Tick += (s, e) => Tick();
            _poll.Start();
        }

        IAudioEndpointVolume Ep
        {
            get
            {
                if (_ep == null)
                {
                    try
                    {
                        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                        IMMDevice dev;
                        if (en.GetDefaultAudioEndpoint(0, 1, out dev) == 0 && dev != null)
                        {
                            var iid = typeof(IAudioEndpointVolume).GUID;
                            object o;
                            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) _ep = (IAudioEndpointVolume)o;
                        }
                    }
                    catch (Exception ex) { Log.Write(ex); }
                }
                return _ep;
            }
        }

        bool Read(out Sample s)
        {
            s = new Sample { At = DateTime.Now };
            try
            {
                var ep = Ep;
                if (ep == null) return false;
                float v; bool m;
                if (ep.GetMasterVolumeLevelScalar(out v) != 0 || ep.GetMute(out m) != 0) { _ep = null; return false; }
                s.Vol = v; s.Mute = m;
                return true;
            }
            catch { _ep = null; return false; }
        }

        void Tick()
        {
            if (_holding)
            {
                if (_keepHolding() || DateTime.Now < _holdUntil) { Restore(); return; }
                _holding = false;
                _history.Clear();
            }
            Sample s;
            if (!Read(out s)) return;
            _history.Add(s);
            if (_history.Count > 20) _history.RemoveAt(0);
        }

        // Call when KnobRing has used a knob event that Windows will also act on.
        public void Hold(DateTime eventAt)
        {
            if (!_holding)
            {
                // the newest reading taken before the event
                bool found = false;
                for (int i = _history.Count - 1; i >= 0; i--)
                    if (_history[i].At <= eventAt.AddMilliseconds(-15)) { _snap = _history[i]; found = true; break; }
                if (!found && !Read(out _snap)) return;
                _holding = true;
            }
            _holdUntil = DateTime.Now.AddMilliseconds(700);
            Restore();
            foreach (int ms in new[] { 40, 120, 300 })
            {
                var t = new System.Windows.Forms.Timer { Interval = ms };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (_holding) Restore(); };
                t.Start();
            }
        }

        public void Release() { _holding = false; _holdUntil = DateTime.MinValue; _history.Clear(); }

        void Restore()
        {
            try
            {
                var ep = Ep;
                if (ep == null) return;
                float v; bool m;
                var ctx = Guid.Empty;
                if (ep.GetMasterVolumeLevelScalar(out v) == 0 && Math.Abs(v - _snap.Vol) > 0.001f) ep.SetMasterVolumeLevelScalar(_snap.Vol, ref ctx);
                if (ep.GetMute(out m) == 0 && m != _snap.Mute) ep.SetMute(_snap.Mute, ref ctx);
            }
            catch { _ep = null; }
        }

        public void Dispose() { _poll.Dispose(); }

        // ---- Core Audio COM interop
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}

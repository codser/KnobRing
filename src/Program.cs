using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KnobRing
{
    internal static class Program
    {
        public static AppConfig Config;
        public static DateTime LastSelfSave = DateTime.MinValue;
        public const string OpenEventName = "KnobRing_AULA_F75_open_window";

        [STAThread]
        static void Main(string[] args)
        {
            bool afterUpdate = Array.Exists(args, a => a.Equals("--after-update", StringComparison.OrdinalIgnoreCase));
            using (var mutex = new Mutex(false, "KnobRing_AULA_F75_single"))
            {
                bool owned;
                // right after an update the old copy may still be closing: wait for it instead of quitting
                try { owned = mutex.WaitOne(afterUpdate ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    // Already running: ask that copy to show its window, then quit.
                    try { using (var ev = EventWaitHandle.OpenExisting(OpenEventName)) ev.Set(); } catch { }
                    return;
                }
                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write(e.ExceptionObject);
                bool atStartup = Array.Exists(args, a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
                try { Application.Run(new KnobRingApp(!atStartup)); }
                finally { try { mutex.ReleaseMutex(); } catch { } }
            }
        }
    }

    internal sealed class KnobRingApp : ApplicationContext
    {
        const int VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF, VK_ESCAPE = 0x1B, VK_RETURN = 0x0D;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly NotifyIcon _tray;
        readonly RingWindow _ring;
        readonly KeyboardHook _keys;
        readonly RawKnob _raw;
        readonly AudioGuard _guard;
        int _rawLogged;
        readonly Dictionary<int, DateTime> _lastAt = new Dictionary<int, DateTime>();
        readonly Dictionary<int, bool> _lastResult = new Dictionary<int, bool>();
        readonly Dictionary<int, bool> _lastRaw = new Dictionary<int, bool>();
        readonly MouseHook _mouse;
        readonly FileSystemWatcher _watcher;
        readonly System.Windows.Forms.Timer _reloadDebounce = new System.Windows.Forms.Timer { Interval = 400 };
        readonly System.Windows.Forms.Timer _idle = new System.Windows.Forms.Timer { Interval = 250 };
        readonly ToolStripMenuItem _pauseItem;
        readonly EventWaitHandle _openEvent;
        SettingsForm _settings;
        FileSystemWatcher _updWatcher;

        readonly Stack<Ring> _stack = new Stack<Ring>();  // parents when inside a sub-ring
        Ring _current;
        List<RingItem> _items = new List<RingItem>();
        int _selected;
        readonly Dictionary<string, int> _lastUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool _muteDown, _paused, _pressSwallowing;
        DateTime _firstTap = DateTime.MinValue;
        const double DoubleTapMs = 420;
        KeySink _sink;
        IntPtr _prevFg = IntPtr.Zero;
        int _updateTries;
        bool _swallowLeftUp, _swallowOtherUp, _unhookPending, _mouseActive;
        Point _mouseOrigin;
        DateTime _lastInput;

        public bool Paused { get { return _paused; } }

        public KnobRingApp(bool showWindow)
        {
            bool firstRun = !File.Exists(AppConfig.FilePath);
            string err;
            Program.Config = AppConfig.LoadOrCreate(out err);
            Ui.Accent = Program.Config.Accent;

            _ring = new RingWindow();

            // tray
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
            menu.BackColor = Th.Hex("#16181E");
            menu.ForeColor = Th.Text;
            var open = new ToolStripMenuItem("Open KnobRing", null, (s, e) => OpenSettings());
            open.Font = new Font(open.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            _pauseItem = new ToolStripMenuItem("Pause (knob works as normal)", null, (s, e) => SetPaused(!_paused));
            menu.Items.Add(_pauseItem);
            menu.Items.Add("Edit rings file (advanced)", null, (s, e) => EditConfig());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            menu.Opening += (s, e) => { _pauseItem.Checked = _paused; };

            _tray = new NotifyIcon
            {
                Icon = MakeTrayIcon(Program.Config.Accent),
                Text = "KnobRing",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

            // hooks
            _keys = new KeyboardHook((vk, down) => Dispatch(vk, down, false));
            if (!_keys.Install())
                Balloon("KnobRing couldn't hook the keyboard", "Try running it as administrator.", ToolTipIcon.Error);
            _guard = new AudioGuard(() => _ring.IsShown || (_settings != null && !_settings.IsDisposed && _settings.Learning));
            _raw = new RawKnob((code, down) =>
            {
                if (_rawLogged < 30) { _rawLogged++; Log.Write("Media key from keyboard: 0x" + code.ToString("X2") + (down ? " down" : " up")); }
                if (Dispatch(code, down, true) && down) _guard.Hold(DateTime.Now);
            });
            _mouse = new MouseHook(OnMouse);
            Log.Write("KnobRing started. Media-key listener " + (_raw.Registered ? "on" : "FAILED"));

            // watch rings.json for hand edits
            _watcher = new FileSystemWatcher(AppConfig.Folder, "rings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler changed = (s, e) => _ring.BeginInvoke((Action)(() => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }));
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (s, e) => changed(s, null);
            _reloadDebounce.Tick += (s, e) => { _reloadDebounce.Stop(); Reload(); };

            _idle.Tick += (s, e) =>
            {
                double t = Program.Config.TimeoutSeconds;
                if (_ring.IsShown && t > 0 && (DateTime.Now - _lastInput).TotalSeconds > t)
                    CloseRing(false);
            };

            // a second launch of the exe opens this window
            _openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.OpenEventName);
            var waiter = new Thread(() =>
            {
                while (true)
                {
                    try { _openEvent.WaitOne(); } catch { return; }
                    try { _ring.BeginInvoke((Action)OpenSettings); } catch { return; }
                }
            }) { IsBackground = true };
            waiter.Start();

            WatchForUpdate();

            if (firstRun) SetStartup(true);
            else if (IsStartup()) SetStartup(true);   // refresh the path / flag
            if (err != null) Balloon("rings.json has a problem", err, ToolTipIcon.Warning);
            if (showWindow || firstRun) _ring.BeginInvoke((Action)OpenSettings);
        }

        // ------------------------------------------------------------ self-update
        // A newer build dropped next to the exe as "KnobRing.update.exe" replaces this one and restarts.
        void WatchForUpdate()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                CleanupOld(dir);
                _updWatcher = new FileSystemWatcher(dir, "KnobRing.update.exe")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                var debounce = new System.Windows.Forms.Timer { Interval = 2500 };
                debounce.Tick += (s, e) => { debounce.Stop(); ApplyUpdate(); };
                FileSystemEventHandler h = (s, e) => _ring.BeginInvoke((Action)(() => { debounce.Stop(); debounce.Start(); }));
                _updWatcher.Created += h;
                _updWatcher.Changed += h;
                _updWatcher.Renamed += (s, e) => h(s, null);
                // also look every 20 s, in case a file event was missed
                var poll = new System.Windows.Forms.Timer { Interval = 20000 };
                poll.Tick += (s, e) => { if (File.Exists(Path.Combine(dir, "KnobRing.update.exe")) && !debounce.Enabled) debounce.Start(); };
                poll.Start();
                if (File.Exists(Path.Combine(dir, "KnobRing.update.exe"))) debounce.Start();
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        static void CleanupOld(string dir)
        {
            string old = Path.Combine(dir, "KnobRing.old.exe");
            if (!File.Exists(old)) return;
            var t = new System.Windows.Forms.Timer { Interval = 3000 };
            int tries = 0;
            t.Tick += (s, e) =>
            {
                try { File.Delete(old); t.Stop(); t.Dispose(); }
                catch { if (++tries > 10) { t.Stop(); t.Dispose(); } }
            };
            t.Start();
        }

        // Windows lets a running exe be renamed (not overwritten): move ourselves aside, put the new build in our place,
        // start it and quit. If anything fails, the update file is set aside so it can never loop.
        bool _updating;
        void ApplyUpdate()
        {
            if (_updating) return;
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string upd = Path.Combine(dir, "KnobRing.update.exe");
            string old = Path.Combine(dir, "KnobRing.old.exe");
            if (!File.Exists(upd)) return;
            _updating = true;
            Log.Write("Updating KnobRing from " + upd);
            bool movedSelf = false;
            try
            {
                if (new FileInfo(upd).Length < 20000) throw new Exception("update file looks incomplete");
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(exe, old);
                movedSelf = true;
                File.Move(upd, exe);
                bool reopen = _settings != null && !_settings.IsDisposed && _settings.Visible;
                Process.Start(new ProcessStartInfo(exe, "--after-update" + (reopen ? "" : " --startup")) { UseShellExecute = false });
                ExitApp();
            }
            catch (Exception ex)
            {
                Log.Write("Update failed (try " + (_updateTries + 1) + "): " + ex.Message);
                if (movedSelf && !File.Exists(exe)) { try { File.Move(old, exe); } catch { } }
                if (++_updateTries < 4 && File.Exists(upd))
                {
                    // usually the file is still being written or scanned: wait and try again
                    _updating = false;
                    var retry = new System.Windows.Forms.Timer { Interval = 3000 * _updateTries };
                    retry.Tick += (s, e) => { retry.Stop(); retry.Dispose(); ApplyUpdate(); };
                    retry.Start();
                    return;
                }
                _updateTries = 0;
                try
                {
                    string failed = Path.Combine(dir, "KnobRing.update.failed.exe");
                    if (File.Exists(failed)) File.Delete(failed);
                    if (File.Exists(upd)) File.Move(upd, failed);
                }
                catch { }
                Balloon("KnobRing couldn't update itself", "Close KnobRing and replace KnobRing.exe by hand.", ToolTipIcon.Warning);
                _updating = false;
            }
        }

        // ------------------------------------------------------------ settings window
        public void OpenSettings()
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(this);
                _settings.FormClosed += (s, e) => _settings = null;
                _settings.Show();
            }
            if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            _settings.BringToFront();
        }

        public void ConfigApplied()
        {
            Ui.Accent = Program.Config.Accent;
            var old = _tray.Icon;
            _tray.Icon = MakeTrayIcon(Program.Config.Accent);
            if (old != null) old.Dispose();
        }

        bool SettingsInFront
        {
            get
            {
                return _settings != null && !_settings.IsDisposed && _settings.IsHandleCreated &&
                       Native.GetForegroundWindow() == _settings.Handle;
            }
        }

        // ------------------------------------------------------------ keyboard
        // The same knob event can arrive twice: once from the keyboard hook and once as a raw media-key report.
        bool Dispatch(int code, bool down, bool raw)
        {
            int k = code * 2 + (down ? 1 : 0);
            DateTime t; bool wasRaw;
            if (_lastAt.TryGetValue(k, out t) && _lastRaw.TryGetValue(k, out wasRaw) && wasRaw != raw &&
                (DateTime.Now - t).TotalMilliseconds < 80)
                return _lastResult[k];
            bool r = OnKey(code, down);
            _lastAt[k] = DateTime.Now;
            _lastRaw[k] = raw;
            _lastResult[k] = r;
            return r;
        }

        // Runs inside the hook: decide fast, do the work afterwards on the UI loop.
        bool OnKey(int vk, bool down)
        {
            // "Learn my knob" in the settings window: report every key, let nothing through.
            var lf = _settings;
            if (lf != null && !lf.IsDisposed && lf.Learning)
            {
                if (down)
                {
                    if (vk == VK_ESCAPE) Post(() => lf.CancelLearn());
                    else Post(() => lf.OnLearnKey(vk));
                }
                return true;
            }

            if (_paused) return false;
            var c = Program.Config;
            int VK_PRESS = c.KnobPressVk, VK_RIGHT = c.KnobRightVk, VK_LEFT = c.KnobLeftVk;
            bool knob = vk == VK_PRESS || vk == VK_RIGHT || vk == VK_LEFT;

            // The KnobRing window is in front: the knob drives the editor instead.
            if (knob && !_ring.IsShown && SettingsInFront)
            {
                if (vk == VK_PRESS)
                {
                    if (down && !_muteDown) { _muteDown = true; var f = _settings; Post(() => f.OnKnob(0)); }
                    else if (!down) _muteDown = false;
                }
                else if (down) { int d = vk == VK_RIGHT ? 1 : -1; var f = _settings; Post(() => f.OnKnob(d)); }
                return true;
            }

            if (vk == VK_PRESS && (c.OpenWith != "off" || _ring.IsShown))
            {
                if (!down) { _muteDown = false; return _pressSwallowing; }
                if (_muteDown) return _pressSwallowing;          // key repeat
                _muteDown = true;
                if (_ring.IsShown) { _pressSwallowing = true; Post(Select); return true; }
                if (c.OpenWith == "single") { _pressSwallowing = true; Post(OpenRing); return true; }
                // double press: the first press is a normal mute, the second opens the ring
                var now = DateTime.Now;
                if ((now - _firstTap).TotalMilliseconds <= DoubleTapMs)
                {
                    var first = _firstTap;
                    _firstTap = DateTime.MinValue;
                    _pressSwallowing = true;
                    _guard.Hold(first);                          // put mute back to how it was before the first press
                    Post(OpenRing);
                    return true;
                }
                _firstTap = now;
                _pressSwallowing = false;
                return false;
            }
            if (!_ring.IsShown) return false;

            if (vk == VK_RIGHT || vk == VK_LEFT)
            {
                if (down) { int d = vk == VK_RIGHT ? 1 : -1; Post(() => Move(d)); }
                return true;
            }
            if (vk == VK_ESCAPE) { if (down) Post(() => CloseRing(false)); return true; }
            if (vk == VK_RETURN) { if (down) Post(Select); return true; }
            return false;
        }

        void Post(Action a) { _ring.BeginInvoke(a); }

        // While the ring is open the mouse points at slots by direction, and clicks never reach the app underneath.
        const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204,
                  WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A,
                  WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;

        bool OnMouse(int msg, int x, int y, int wheel)
        {
            // swallow the button-up that belongs to a click we already took, even if the ring has just closed
            if (msg == WM_LBUTTONUP && _swallowLeftUp) { _swallowLeftUp = false; MaybeUnhook(); return true; }
            if ((msg == WM_RBUTTONUP || msg == WM_MBUTTONUP || msg == WM_XBUTTONUP) && _swallowOtherUp)
            { _swallowOtherUp = false; MaybeUnhook(); return true; }
            if (!_ring.IsShown) return false;

            switch (msg)
            {
                case WM_MOUSEMOVE:
                    Post(() => MouseMoved(x, y));
                    return false;
                case WM_LBUTTONDOWN:
                    _swallowLeftUp = true;
                    Post(() => { MouseMoved(x, y); Select(); });
                    return true;
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                case WM_XBUTTONDOWN:
                    _swallowOtherUp = true;
                    Post(() => CloseRing(false));
                    return true;
                case WM_MOUSEWHEEL:
                    int d = wheel < 0 ? 1 : -1;
                    Post(() => Move(d));
                    return true;
            }
            return false;
        }

        void MaybeUnhook()
        {
            if (_unhookPending && !_swallowLeftUp && !_swallowOtherUp && !_ring.IsShown)
            {
                _unhookPending = false;
                Post(() => { if (!_ring.IsShown) _mouse.Uninstall(); });
            }
        }

        void MouseMoved(int x, int y)
        {
            if (!_ring.IsShown || _items.Count == 0) return;
            if (!_mouseActive)
            {
                int dx0 = x - _mouseOrigin.X, dy0 = y - _mouseOrigin.Y;
                if (dx0 * dx0 + dy0 * dy0 < 14 * 14) return;   // small nudges don't steal the highlight from the knob
                _mouseActive = true;
            }
            var b = _ring.ScreenBounds;
            int slot = _ring.Renderer.SlotAtOffset(x - (b.X + b.Width / 2f), y - (b.Y + b.Height / 2f));
            bool hub = slot < 0;
            bool changed = hub != _ring.Renderer.HubHover;
            _ring.Renderer.HubHover = hub;
            if (!hub && slot != _selected) { _selected = slot; changed = true; }
            if (changed) { Touch(); UpdateRing(); }
        }

        // ------------------------------------------------------------ ring flow
        void KnobPressed()
        {
            if (_ring.IsShown) Select();
            else OpenRing();
        }

        void OpenRing()
        {
            string exe = ForegroundExe();
            var ring = Program.Config.FindForApp(exe);
            if (ring == null) return;
            _stack.Clear();
            SetCurrent(ring);
            Native.POINT p;
            Native.GetCursorPos(out p);
            var at = new Point(p.X, p.Y);
            if (Program.Config.Position == "center")
            {
                var wa = Screen.FromPoint(at).WorkingArea;
                at = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
            }
            _ring.Renderer.ShowLabels = Program.Config.ShowLabels;
            _ring.Renderer.Liquid = (float)Program.Config.Liquid;
            _ring.Renderer.EaseAmount = (float)Program.Config.Ease;
            _ring.ShowAt(at);
            // take keyboard focus with an invisible window, so volume/media commands from the knob come to us
            try
            {
                _prevFg = Native.GetForegroundWindow();
                if (_sink == null || _sink.IsDisposed) _sink = new KeySink();
                _sink.Show();
                Native.ForceForeground(_sink.Handle);
            }
            catch (Exception ex) { Log.Write(ex); }
            _mouseOrigin = new Point(p.X, p.Y);
            _mouseActive = false;
            _unhookPending = false;
            _mouse.Install();
            Touch();
            _idle.Start();
        }

        void SetCurrent(Ring r)
        {
            _current = r;
            _items = new List<RingItem>(r.Items);
            if (_stack.Count > 0) _items.Add(new RingItem { Label = "Back", Icon = "back", Type = "__back" });
            int last;
            _selected = Program.Config.RememberLast && _lastUsed.TryGetValue(r.Name, out last) && last < _items.Count ? last : 0;
            _ring.Renderer.Items = _items;
            _ring.Renderer.Title = r.Name;
            _ring.Renderer.Accent = Program.Config.Accent;
            _ring.Renderer.Selected = _selected;
        }

        void Move(int dir)
        {
            if (!_ring.IsShown || _items.Count == 0) return;
            _ring.KnobUsed();
            Native.POINT cp;
            Native.GetCursorPos(out cp);
            _mouseOrigin = new Point(cp.X, cp.Y);
            _mouseActive = false;
            _selected = ((_selected + dir) % _items.Count + _items.Count) % _items.Count;
            Touch();
            UpdateRing();
        }

        void UpdateRing()
        {
            _ring.Renderer.Selected = _selected;
            _ring.Refresh2();
        }

        void Select()
        {
            if (!_ring.IsShown || _items.Count == 0) return;
            if (_ring.Renderer.HubHover) { CloseRing(false); return; }
            var it = _items[_selected];
            Touch();

            if (it.Type == "__back")
            {
                SetCurrent(_stack.Pop());
                _ring.Replay();
                return;
            }
            if (it.Type == "ring")
            {
                var sub = Program.Config.FindByName(it.Value);
                if (sub == null || sub == _current) { Balloon("No sub-ring named \"" + it.Value + "\"", "Check it in the KnobRing window.", ToolTipIcon.Warning); return; }
                _lastUsed[_current.Name] = _selected;
                _stack.Push(_current);
                SetCurrent(sub);
                _ring.Replay();
                return;
            }

            _lastUsed[_current.Name] = _selected;
            // Hide first and give focus back, so the keys land in the app behind, then run the action.
            CloseRing(true, () =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 70 };
                t.Tick += (s, e) =>
                {
                    t.Stop(); t.Dispose();
                    if (it.Type == "media") _guard.Release();   // let our own volume/mute action stick
                    try { Actions.Execute(it); }
                    catch (Exception ex)
                    {
                        Log.Write(ex);
                        Balloon("Couldn't run \"" + it.Label + "\"", ex.Message, ToolTipIcon.Warning);
                    }
                };
                t.Start();
            });
        }

        void CloseRing(bool fast, Action after = null)
        {
            if (_swallowLeftUp || _swallowOtherUp) _unhookPending = true;   // wait for the button to come back up
            else _mouse.Uninstall();
            _idle.Stop();
            ReleaseFocus();
            _ring.CloseRing(fast, after);
        }

        void ReleaseFocus()
        {
            try
            {
                if (_sink != null && !_sink.IsDisposed && _sink.Visible)
                {
                    if (_prevFg != IntPtr.Zero && Native.GetForegroundWindow() == _sink.Handle) Native.ForceForeground(_prevFg);
                    _sink.Hide();
                }
            }
            catch (Exception ex) { Log.Write(ex); }
            _prevFg = IntPtr.Zero;
        }

        void Touch() { _lastInput = DateTime.Now; }

        static string ForegroundExe()
        {
            try
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return "";
                try
                {
                    var sb = new System.Text.StringBuilder(1024);
                    int size = sb.Capacity;
                    if (Native.QueryFullProcessImageName(h, 0, sb, ref size)) return Path.GetFileName(sb.ToString());
                }
                finally { Native.CloseHandle(h); }
            }
            catch (Exception ex) { Log.Write(ex); }
            return "";
        }

        // ------------------------------------------------------------ tray actions
        void EditConfig()
        {
            try { Process.Start("notepad.exe", "\"" + AppConfig.FilePath + "\""); }
            catch (Exception ex) { Balloon("Couldn't open rings.json", ex.Message, ToolTipIcon.Error); }
        }

        void Reload()
        {
            if ((DateTime.Now - Program.LastSelfSave).TotalSeconds < 2) return;   // our own save
            try
            {
                var cfg = AppConfig.Parse(File.ReadAllText(AppConfig.FilePath));
                foreach (var r in cfg.Rings)
                    foreach (var it in r.Items)
                        if (it.Type == "keys")
                        {
                            try { Actions.Parse(it.Value); }
                            catch (Exception ex) { throw new Exception(r.Name + " › " + it.Label + ": " + ex.Message); }
                        }
                Program.Config = cfg;
                ConfigApplied();
                if (_settings != null && !_settings.IsDisposed) _settings.ReloadFrom(cfg);
            }
            catch (Exception ex)
            {
                Balloon("rings.json has a problem — kept the previous rings", ex.Message, ToolTipIcon.Warning);
            }
        }

        public void SetPaused(bool p)
        {
            _paused = p;
            if (p && _ring.IsShown) CloseRing(true);
            _tray.Text = p ? "KnobRing (paused)" : "KnobRing";
        }

        public static bool IsStartup()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("KnobRing") != null; }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("KnobRing", "\"" + Application.ExecutablePath + "\" --startup");
                    else k.DeleteValue("KnobRing", false);
                }
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            if (_tray == null) return;
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(4000);
        }

        void ExitApp()
        {
            if (_settings != null && !_settings.IsDisposed) _settings.Close();
            if (_updWatcher != null) _updWatcher.Dispose();
            _keys.Dispose();
            _raw.Dispose();
            _guard.Dispose();
            _mouse.Dispose();
            _watcher.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _ring.Dispose();
            ExitThread();
        }

        // A small ring of dots, drawn in code
        static Icon MakeTrayIcon(Color accent)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 4;
                        float x = 16 + (float)Math.Cos(a) * 11, y = 16 + (float)Math.Sin(a) * 11;
                        float r = i == 0 ? 4.6f : 3.4f;
                        using (var b = new SolidBrush(i == 0 ? accent : Color.FromArgb(235, 235, 240)))
                            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(235, 235, 240)))
                        g.FillEllipse(b, 12.5f, 12.5f, 7, 7);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}

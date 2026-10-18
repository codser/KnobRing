using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace KnobRing
{
    // The main KnobRing window: edit rings per app, and app settings
    internal sealed class SettingsForm : DarkForm
    {
        const float HeaderH = 60, SideW = 250, PanelW = 390;

        static readonly string[,] Types = { { "keys", "Shortcut" }, { "run", "Open" }, { "text", "Type text" }, { "media", "Media" }, { "ring", "Sub-ring" } };
        static readonly string[,] Media = { { "playpause", "Play / Pause" }, { "next", "Next track" }, { "prev", "Previous track" },
                                            { "mute", "Mute" }, { "volup", "Volume up" }, { "voldown", "Volume down" } };
        static readonly string[] Swatches = { "#2F6BFF", "#00B4D8", "#7C5CFF", "#FF4D8D", "#FF8A1F", "#2BD67B" };
        static readonly string[,] Tiles = { { "#26224A", "#C9C2FF" }, { "#13293F", "#9FD3FF" }, { "#2E2416", "#F5CF8E" },
                                            { "#2B2A17", "#F0E08A" }, { "#1B2E24", "#9FE6BE" }, { "#331C26", "#FFB3CF" }, { "#2A2048", "#D5C6FF" } };
        static readonly Dictionary<string, string> KnownBadges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"adobe premiere pro.exe","Pr"}, {"afterfx.exe","Ae"}, {"photoshop.exe","Ps"}, {"illustrator.exe","Ai"},
            {"resolve.exe","DR"}, {"explorer.exe","Ex"}, {"chrome.exe","Ch"}, {"msedge.exe","Ed"}, {"firefox.exe","Ff"},
            {"blender.exe","Bl"}, {"figma.exe","Fi"}, {"code.exe","VS"}, {"lightroom.exe","Lr"}, {"indesign.exe","Id"}
        };

        readonly KnobRingApp _app;
        AppConfig _cfg;
        string _page = "rings";
        Ring _ring;
        int _sel;
        bool _recording, _syncing, _dirty, _keysInvalid;
        string _toast = "", _saveState = "All changes saved";
        DateTime _toastUntil, _recStart;
        float _sideScroll, _sideContentH;
        readonly DateTime[] _knobFlash = new DateTime[3];
        int _learnStep, _lp, _lr;
        DateTime _learnStart;
        string _learnMsg = "";

        readonly RingRenderer _pr = new RingRenderer();
        Bitmap _prBmp;
        bool _prDirty = true;
        RectangleF _ringRect, _sliderRect, _liquidRect, _easeRect;
        int _drag;   // 1 = size slider, 2 = liquid slider, 3 = ease slider
        readonly RingRenderer _mini = new RingRenderer();
        Bitmap _miniBmp;
        readonly Timer _miniCycle = new Timer { Interval = 1100 };
        readonly Timer _anim = new Timer { Interval = 10 };
        readonly Timer _save = new Timer { Interval = 500 };
        readonly Timer _tick = new Timer { Interval = 50 };

        readonly DarkTextBox _tbName, _tbKeys, _tbRun, _tbArgs, _tbText, _tbSub, _tbLetters;

        public SettingsForm(KnobRingApp app) : base(1400, 880)
        {
            _app = app;
            Text = "KnobRing";
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Height > wa.Height) Height = wa.Height;
            if (Width > wa.Width) Width = wa.Width;
            int extraW = Width - ClientSize.Width, extraH = Height - ClientSize.Height;
            MinimumSize = new Size((int)(1180 * S) + extraW, (int)(860 * S) + extraH);
            if (MinimumSize.Height > wa.Height) MinimumSize = new Size(MinimumSize.Width, wa.Height);

            _cfg = Program.Config.Clone();
            Ui.Accent = _cfg.Accent;
            _ring = _cfg.DefaultRing;
            _pr.Open = 1f;

            _tbName = MakeBox(15);
            _tbKeys = MakeBox(14); _tbKeys.Cue = "Or type it, e.g. Ctrl+Shift+S";
            _tbRun = MakeBox(14); _tbRun.Cue = @"C:\Program Files\…  or  https://…";
            _tbArgs = MakeBox(14); _tbArgs.Cue = "Optional";
            _tbText = MakeBox(14); _tbText.Multiline = true; _tbText.AcceptsReturn = true; _tbText.WordWrap = true;
            _tbSub = MakeBox(14); _tbSub.Cue = "e.g. Effects";
            _tbLetters = MakeBox(15); _tbLetters.Cue = "Aa"; _tbLetters.MaxLength = 3; _tbLetters.TextAlign = HorizontalAlignment.Center;
            _tbLetters.Font = new Font("Segoe UI Semibold", 15 * S, GraphicsUnit.Pixel);

            _tbName.TextChanged += (s, e) => Edit(it => it.Label = _tbName.Text, false);
            _tbKeys.TextChanged += (s, e) => OnKeysTyped();
            _tbRun.TextChanged += (s, e) => Edit(it => it.Value = _tbRun.Text.Trim(), false);
            _tbArgs.TextChanged += (s, e) => Edit(it => it.Args = _tbArgs.Text, false);
            _tbText.TextChanged += (s, e) => Edit(it => it.Value = _tbText.Text, false);
            _tbSub.TextChanged += (s, e) => Edit(it => it.Value = _tbSub.Text.Trim(), false);
            _tbLetters.TextChanged += (s, e) => Edit(it =>
            {
                var t = _tbLetters.Text.Trim();
                it.Icon = t.Length > 0 ? t : "add";
            }, false);
            foreach (Control c in Sf.Controls)
            {
                c.GotFocus += (s, e) => Sf.Invalidate();
                c.LostFocus += (s, e) => Sf.Invalidate();
            }

            Sf.Painter = Draw;
            Sf.Wheel = OnWheel;
            Sf.KeyHandler = OnKey;
            Sf.DragStart = p =>
            {
                if (_page != "settings") return false;
                if (RectangleF.Inflate(_sliderRect, 4, 14).Contains(p)) _drag = 1;
                else if (RectangleF.Inflate(_liquidRect, 4, 14).Contains(p)) _drag = 2;
                else if (RectangleF.Inflate(_easeRect, 4, 14).Contains(p)) _drag = 3;
                else return false;
                SlideTo(p.X); return true;
            };
            Sf.DragMove = p => SlideTo(p.X);
            Sf.Resize += (s, e) => { _prDirty = true; PlaceBoxes(); };

            _anim.Tick += (s, e) =>
            {
                _pr.Liquid = (float)_cfg.Liquid;
                _mini.Liquid = (float)_cfg.Liquid;
                _pr.EaseAmount = (float)_cfg.Ease;
                _mini.EaseAmount = (float)_cfg.Ease;
                bool a = _pr.Step();
                bool b = _page == "settings" && _mini.Step();
                _prDirty = true;
                if (!a && !b) _anim.Stop();
                Sf.Invalidate();
            };
            // little demo ring next to the "Liquid feel" slider
            foreach (var k in new[] { "copy", "paste", "cut", "undo", "redo", "play" })
                _mini.Items.Add(new RingItem { Label = "", Icon = k });
            _mini.ShowLabels = false;
            _mini.Open = 1f;
            _mini.ResetAnimation();
            _miniCycle.Tick += (s, e) =>
            {
                if (_page != "settings" || !Visible || WindowState == FormWindowState.Minimized) return;
                _mini.Selected = (_mini.Selected + 1) % _mini.Items.Count;
                _anim.Start();
            };
            _miniCycle.Start();
            _save.Tick += (s, e) => { _save.Stop(); Save(); };
            _tick.Tick += (s, e) =>
            {
                if (_learnStep > 0 && (DateTime.Now - _learnStart).TotalSeconds > 12)
                {
                    _learnStep = 0;
                    _learnMsg = "Nothing arrived from the knob. If turning it changes the keyboard lighting, hold the knob down for a few seconds until the lights flash, then try again.";
                }
                bool live = _recording || (_toast.Length > 0) || AnyFlash() || _learnStep > 0;
                if (_toast.Length > 0 && DateTime.Now > _toastUntil) _toast = "";
                if (live) Sf.Invalidate();
            };
            _tick.Start();

            SelectRing(_ring);
        }

        // ================================================================ state
        RingItem Cur { get { return _ring.Items[Math.Min(_sel, _ring.Items.Count - 1)]; } }

        IEnumerable<Ring> Profiles
        {
            get
            {
                var d = _cfg.DefaultRing;
                yield return d;
                foreach (var r in _cfg.Rings) if (r != d && r.Apps.Count > 0) yield return r;
            }
        }

        IEnumerable<Ring> SubRings
        {
            get { foreach (var r in _cfg.Rings) if (_cfg.IsSubRing(r)) yield return r; }
        }

        void SelectRing(Ring r)
        {
            _ring = r;
            _sel = 0;
            _recording = false;
            _pr.Items = r.Items;
            _pr.Selected = 0;
            _pr.ResetAnimation();
            _prDirty = true;
            SyncFields();
            PlaceBoxes();
            Sf.Invalidate();
        }

        void Select(int i)
        {
            if (_ring.Items.Count == 0) return;
            _sel = ((i % _ring.Items.Count) + _ring.Items.Count) % _ring.Items.Count;
            _recording = false;
            _pr.Selected = _sel;
            _anim.Start();
            SyncFields();
            PlaceBoxes();
            Sf.Invalidate();
        }

        void SyncFields()
        {
            _syncing = true;
            var it = Cur;
            _tbName.Text = it.Label;
            _tbKeys.Text = it.Type == "keys" ? it.Value : "";
            _tbRun.Text = it.Type == "run" ? it.Value : "";
            _tbArgs.Text = it.Args ?? "";
            _tbText.Text = it.Type == "text" ? (it.Value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            _tbSub.Text = it.Type == "ring" ? it.Value : "";
            _tbLetters.Text = !Icons.IsGlyph(it.Icon) && !Icons.IsImage(it.Icon) ? it.Icon : "";
            _keysInvalid = false;
            _syncing = false;
        }

        void Edit(Action<RingItem> change, bool relayout)
        {
            if (_syncing) return;
            change(Cur);
            Changed(relayout);
        }

        void Changed(bool relayout = false)
        {
            _dirty = true;
            _prDirty = true;
            _saveState = "Saving…";
            _save.Stop(); _save.Start();
            if (relayout) PlaceBoxes();
            Sf.Invalidate();
        }

        void OnKeysTyped()
        {
            if (_syncing) return;
            var t = _tbKeys.Text.Trim();
            try
            {
                Actions.Parse(t);
                _keysInvalid = false;
                Edit(it => it.Value = t, false);
            }
            catch { _keysInvalid = true; Sf.Invalidate(); }
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.Folder);
                Program.LastSelfSave = DateTime.Now;
                File.WriteAllText(AppConfig.FilePath, _cfg.ToJson());
                Program.Config = _cfg.Clone();
                if (_app != null) _app.ConfigApplied();
                _dirty = false;
                _saveState = "All changes saved";
            }
            catch (Exception ex)
            {
                _saveState = "Couldn't save: " + ex.Message;
                Log.Write(ex);
            }
            Sf.Invalidate();
        }

        void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = DateTime.Now.AddSeconds(2.6);
            Sf.Invalidate();
        }

        bool AnyFlash()
        {
            foreach (var t in _knobFlash) if ((DateTime.Now - t).TotalMilliseconds < 450) return true;
            return false;
        }

        // Called by the app when the knob is used while this window is in front: -1 left, 0 press, 1 right
        public void OnKnob(int kind)
        {
            _knobFlash[kind + 1] = DateTime.Now;
            if (_page == "rings")
            {
                if (kind == 0) Toast("Pressing now would run “" + Cur.Label + "” — " + Summary(Cur));
                else Select(_sel + kind);
            }
            Sf.Invalidate();
        }

        // ---- "Learn my knob": the app sends us every key while this is on
        public bool Learning { get { return _learnStep > 0; } }

        void StartLearn()
        {
            _page = "settings";
            _learnStep = 1;
            _learnStart = DateTime.Now;
            _learnMsg = "";
            PlaceBoxes();
            Sf.Invalidate();
        }

        public void CancelLearn()
        {
            _learnStep = 0;
            _learnMsg = "Cancelled — nothing changed.";
            Sf.Invalidate();
        }

        public void OnLearnKey(int vk)
        {
            Log.Write("Learn my knob, step " + _learnStep + ": key " + VkName(vk) + " (0x" + vk.ToString("X2") + ")");
            if (_learnStep == 1) { _lp = vk; _learnStep = 2; }
            else if (_learnStep == 2) { if (vk == _lp) return; _lr = vk; _learnStep = 3; }
            else if (_learnStep == 3)
            {
                if (vk == _lp || vk == _lr) return;
                _cfg.KnobPressVk = _lp;
                _cfg.KnobRightVk = _lr;
                _cfg.KnobLeftVk = vk;
                _learnStep = 0;
                _learnMsg = "Got it. Press = " + VkName(_lp) + ", right = " + VkName(_lr) + ", left = " + VkName(vk) + ". Try the knob in any app now.";
                Changed();
                _save.Stop();
                Save();
            }
            _learnStart = DateTime.Now;
            Sf.Invalidate();
        }

        public static string VkName(int vk)
        {
            switch (vk)
            {
                case 0xAD: return "Mute";
                case 0xAE: return "Volume down";
                case 0xAF: return "Volume up";
                case 0xB0: return "Next track";
                case 0xB1: return "Previous track";
                case 0xB2: return "Stop";
                case 0xB3: return "Play/Pause";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x0D: return "Enter";
                case 0x20: return "Space";
            }
            if (vk >= 0x1000) return "media key 0x" + (vk & 0xFFF).ToString("X2");
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            return "key 0x" + vk.ToString("X2");
        }

        public void ReloadFrom(AppConfig cfg)
        {
            string name = _ring != null ? _ring.Name : null;
            _cfg = cfg.Clone();
            Ui.Accent = _cfg.Accent;
            SelectRing(_cfg.FindByName(name) ?? _cfg.DefaultRing);
        }

        static string Summary(RingItem it)
        {
            switch (it.Type)
            {
                case "keys": return string.IsNullOrEmpty(it.Value) ? "No shortcut yet" : it.Value;
                case "run": return string.IsNullOrEmpty(it.Value) ? "Nothing to open yet" : "Opens " + Path.GetFileName(it.Value.TrimEnd('\\', '/'));
                case "text": return "Types text";
                case "media":
                    for (int i = 0; i < Media.GetLength(0); i++) if (Media[i, 0] == it.Value) return Media[i, 1];
                    return "Media key";
                case "ring": return "Opens " + (string.IsNullOrEmpty(it.Value) ? "a sub-ring" : it.Value);
            }
            return "";
        }

        // ================================================================ input
        void OnWheel(PointF p, int delta)
        {
            if (_page == "rings" && p.X < SideW && p.Y > HeaderH)
            {
                float max = Math.Max(0, _sideContentH - (H - HeaderH - 20));
                _sideScroll = Math.Max(0, Math.Min(max, _sideScroll - delta / 2f));
                Sf.Invalidate();
            }
            else if (_page == "rings" && _ringRect.Contains(p))
                Select(_sel + (delta < 0 ? 1 : -1));
        }

        bool OnKey(Keys keyData)
        {
            if (!_recording) return false;
            var code = keyData & Keys.KeyCode;
            var mods = keyData & Keys.Modifiers;
            if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
                return true;
            if (code == Keys.Escape && mods == Keys.None) { _recording = false; Sf.Invalidate(); return true; }
            string name = KeyName(code);
            if (name == null) return true;
            var parts = new List<string>();
            if ((mods & Keys.Control) != 0) parts.Add("Ctrl");
            if ((mods & Keys.Shift) != 0) parts.Add("Shift");
            if ((mods & Keys.Alt) != 0) parts.Add("Alt");
            if (Native.GetAsyncKeyState(0x5B) < 0 || Native.GetAsyncKeyState(0x5C) < 0) parts.Add("Win");
            parts.Add(name);
            string v = string.Join("+", parts.ToArray());
            _recording = false;
            Cur.Value = v;
            SyncFields();
            Changed();
            return true;
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return ((char)k).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F24) return "F" + (k - Keys.F1 + 1);
            switch (k)
            {
                case Keys.Space: return "Space";
                case Keys.Return: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Escape: return "Esc";
                case Keys.PrintScreen: return "PrintScreen";
                case Keys.Apps: return "Menu";
                case Keys.Multiply: return "NumMultiply";
                case Keys.Add: return "NumPlus";
                case Keys.Subtract: return "NumMinus";
                case Keys.Divide: return "NumDivide";
                case Keys.Decimal: return "NumDot";
            }
            uint ch = Native.MapVirtualKey((uint)k, 2) & 0x7FFF;
            if (ch == 0) return null;
            char c = (char)ch;
            if (c == ',') return "Comma";
            if (c == '+') return "Plus";
            return char.ToUpperInvariant(c).ToString();
        }

        // ================================================================ layout of text boxes
        void PlaceBoxes()
        {
            bool rings = _page == "rings";
            float px = W - PanelW + 20, pw = PanelW - 40;
            string t = Cur.Type;
            Place(_tbName, new RectangleF(px, 152, pw, 40), rings);
            Place(_tbKeys, new RectangleF(px, 452, pw, 38), rings && t == "keys");
            Place(_tbRun, new RectangleF(px, 348, pw - 90, 40), rings && t == "run");
            Place(_tbArgs, new RectangleF(px, 420, pw, 38), rings && t == "run");
            Place(_tbText, new RectangleF(px, 348, pw, 120), rings && t == "text");
            Place(_tbSub, new RectangleF(px, 348, pw, 40), rings && t == "ring");
            Place(_tbLetters, LettersBox(px, pw), rings);
        }

        static RectangleF LettersBox(float px, float pw) { return new RectangleF(px + pw - 82 - 8 - 62, 498, 62, 34); }

        // ================================================================ painting
        void Draw(Ui ui)
        {
            Ui.Accent = _cfg.Accent;
            PaintHeader(ui);
            if (_page == "rings") PaintRings(ui);
            else PaintSettings(ui);
        }

        void PaintHeader(Ui ui)
        {
            ui.Fill(new RectangleF(0, 0, W, HeaderH), Th.Bar);
            ui.Line(0, HeaderH - 0.5f, W, HeaderH - 0.5f, Th.Line);
            DrawLogo(ui, 20, 15, 30);
            ui.Text("KNOBRING", Th.Head(23), Th.Text, new RectangleF(60, 0, 140, HeaderH));

            float x = 196;
            foreach (var tab in new[] { "rings", "settings" })
            {
                string label = tab.ToUpperInvariant();
                float w = ui.Measure(label, Th.Head(17)).Width + 30;
                var r = new RectangleF(x, 0, w, HeaderH);
                bool on = _page == tab;
                ui.Text(label, Th.Head(17), on ? Color.White : (ui.Hot(r) ? Th.Sub : Th.Muted), r, StringAlignment.Center);
                if (on) ui.Fill(new RectangleF(x + 10, HeaderH - 3, w - 20, 3), Ui.Accent);
                string target = tab;
                ui.Hit(r, () => { _page = target; _recording = false; PlaceBoxes(); Sf.Focus(); Sf.Invalidate(); });
                x += w + 4;
            }

            // pause + status
            bool paused = _app != null && _app.Paused;
            var pb = new RectangleF(W - 20 - 96, 10, 96, 40);
            ui.Button(pb, paused ? "Resume" : "Pause", Ui.B.Normal, () => { if (_app != null) _app.SetPaused(!_app.Paused); Sf.Invalidate(); });
            string status = paused ? "Paused" : "Listening for the knob";
            float sw = ui.Measure("AULA F75", Th.Bold(14)).Width + ui.Measure(status, Th.Body(13.5f)).Width + 58;
            var pill = new RectangleF(pb.X - 12 - sw, 12, sw, 36);
            ui.Fill(pill, Th.Field, 18);
            ui.Stroke(pill, Th.Hex("#22252E"), 18);
            var dot = paused ? Th.Warn : Th.Good;
            ui.Circle(pill.X + 18, pill.Y + 18, 7, Color.FromArgb(50, dot));
            ui.Circle(pill.X + 18, pill.Y + 18, 4, dot);
            float tw = ui.Measure("AULA F75", Th.Bold(14)).Width;
            ui.Text("AULA F75", Th.Bold(14), Th.Text, new RectangleF(pill.X + 32, pill.Y, tw + 6, 36));
            ui.Text(status, Th.Body(13.5f), Th.Muted, new RectangleF(pill.X + 40 + tw, pill.Y, sw - tw - 40, 36));
        }

        static void DrawLogo(Ui ui, float x, float y, float size)
        {
            float c = size / 2, R = size * 0.34f;
            ui.Circle(x + c, y + c, c, Th.Hex("#16181E"));
            for (int i = 0; i < 8; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 4;
                ui.Circle(x + c + (float)Math.Cos(a) * R, y + c + (float)Math.Sin(a) * R, i == 0 ? size * 0.1f : size * 0.072f,
                          i == 0 ? Ui.Accent : Th.Hex("#D8DBE3"));
            }
            ui.Circle(x + c, y + c, size * 0.11f, Th.Hex("#D8DBE3"));
        }

        // ---------------------------------------------------------------- rings page
        void PaintRings(Ui ui)
        {
            PaintSidebar(ui);
            PaintStage(ui);
            PaintPanel(ui);
        }

        void Tile(Ui ui, RectangleF r, Ring ring)
        {
            int h = 0;
            foreach (char ch in ring.Name) h = h * 31 + ch;
            int idx = Math.Abs(h) % Tiles.GetLength(0);
            if (ring == _cfg.DefaultRing)
            {
                ui.Fill(r, Th.Hex("#232733"), 9);
                ui.Glyph("star", 16, Th.Text, r);
                return;
            }
            if (_cfg.IsSubRing(ring))
            {
                ui.Fill(r, Th.Hex("#1C2029"), 9);
                ui.Glyph("more", 16, Th.Sub, r);
                return;
            }
            string badge;
            if (ring.Apps.Count == 0 || !KnownBadges.TryGetValue(ring.Apps[0], out badge)) badge = AddAppForm.Badge(ring.Name);
            ui.Fill(r, Th.Hex(Tiles[idx, 0]), 9);
            ui.Text(badge, Th.Head(17), Th.Hex(Tiles[idx, 1]), r, StringAlignment.Center);
        }

        void PaintSidebar(Ui ui)
        {
            var side = new RectangleF(0, HeaderH, SideW, H - HeaderH);
            ui.Fill(side, Th.Bar);
            ui.Line(SideW - 0.5f, HeaderH, SideW - 0.5f, H, Th.Line);
            ui.G.SetClip(side);

            float y = HeaderH + 20 - _sideScroll, top = y;
            ui.Text("APP PROFILES", Th.Head(14), Th.Muted, new RectangleF(22, y, 200, 18));
            y += 28;
            foreach (var r in Profiles) { SideRow(ui, r, y, 56); y += 62; }

            var subs = new List<Ring>(SubRings);
            if (subs.Count > 0)
            {
                y += 10;
                ui.Text("SUB-RINGS", Th.Head(14), Th.Muted, new RectangleF(22, y, 200, 18));
                y += 28;
                foreach (var r in subs) { SideRow(ui, r, y, 50); y += 56; }
            }

            y += 6;
            ui.Button(new RectangleF(14, y, SideW - 28, 46), "Add app", Ui.B.Ghost, AddApp, false, "add", 10);
            y += 58;
            _sideContentH = y - top + 20;

            float hintY = Math.Max(y + 10, H - 70);
            ui.Text("The ring follows the app in front. Apps without a profile use Default. Right-click a profile to rename or remove it.",
                Th.Body(12), Th.Hex("#6E7484"), new RectangleF(22, hintY, SideW - 40, 60), StringAlignment.Near, StringAlignment.Near, true);
            ui.G.ResetClip();
        }

        void SideRow(Ui ui, Ring r, float y, float h)
        {
            var rect = new RectangleF(14, y, SideW - 28, h);
            if (rect.Bottom < HeaderH || rect.Top > H) return;
            bool on = r == _ring, hot = ui.Hot(rect);
            if (on) { ui.Fill(rect, Th.RowSel, 10); ui.Stroke(rect, Ui.Accent, 10); }
            else if (hot) ui.Fill(rect, Th.Hover, 10);
            float ts = h - 20;
            Tile(ui, new RectangleF(rect.X + 10, rect.Y + 10, ts, ts), r);
            string sub = r == _cfg.DefaultRing ? "Every other app" : _cfg.IsSubRing(r) ? "Sub-ring" : string.Join(", ", r.Apps.ToArray());
            float tx = rect.X + 20 + ts;
            ui.Text(r.Name, Th.Bold(15), Th.Text, new RectangleF(tx, rect.Y + (h > 52 ? 9 : 6), rect.Right - tx - 8, 22));
            ui.Text(sub, Th.Body(12), Th.Muted, new RectangleF(tx, rect.Y + (h > 52 ? 31 : 27), rect.Right - tx - 8, 18));
            var visible = RectangleF.Intersect(rect, new RectangleF(0, HeaderH, SideW, H - HeaderH));
            ui.Hit(visible, () => SelectRing(r), () => ProfileMenu(r));
        }

        void PaintStage(Ui ui)
        {
            float x0 = SideW, x1 = W - PanelW;
            var stage = new RectangleF(x0, HeaderH, x1 - x0, H - HeaderH);
            using (var path = new GraphicsPath())
            {
                float rr = Math.Max(stage.Width, stage.Height) * 0.75f;
                path.AddEllipse(stage.X + stage.Width / 2 - rr, stage.Y + stage.Height * 0.48f - rr, rr * 2, rr * 2);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterColor = Th.Hex("#161922");
                    b.SurroundColors = new[] { Th.Bg };
                    ui.G.SetClip(stage);
                    ui.G.FillPath(b, path);
                    ui.G.ResetClip();
                }
            }

            string sub = _ring == _cfg.DefaultRing ? "Used for every app without its own profile"
                       : _cfg.IsSubRing(_ring) ? "Opened from another ring"
                       : "Used when " + string.Join(", ", _ring.Apps.ToArray()) + " is in front";
            ui.Text(_ring.Name, Th.Head(32), Th.Text, new RectangleF(x0 + 28, HeaderH + 16, x1 - x0 - 260, 40));
            ui.Text(sub, Th.Body(13), Th.Muted, new RectangleF(x0 + 30, HeaderH + 56, x1 - x0 - 260, 20));

            // slot stepper
            var box = new RectangleF(x1 - 28 - 184, HeaderH + 20, 184, 50);
            ui.Fill(box, Th.Hex("#12141A"), 10);
            ui.Stroke(box, Th.Hex("#22252E"), 10);
            int n = _ring.Items.Count;
            ui.Button(new RectangleF(box.X + 5, box.Y + 5, 40, 40), "", Ui.B.Normal, n > 2 ? (Action)RemoveSlot : null, false, "remove");
            ui.Text(n + " slots", Th.Bold(14), Th.Text, new RectangleF(box.X + 45, box.Y, box.Width - 90, box.Height), StringAlignment.Center);
            ui.Button(new RectangleF(box.Right - 45, box.Y + 5, 40, 40), "", Ui.B.Normal, n < 12 ? (Action)AddSlot : null, false, "add");

            // the ring preview
            var area = new RectangleF(x0 + 20, HeaderH + 92, x1 - x0 - 40, H - HeaderH - 92 - 120);
            _pr.Scale = 1f;
            float baseSize = _pr.PixelSize;
            float size = Math.Max(200, Math.Min(area.Width, area.Height));
            float scale = size / baseSize;
            if (_prBmp == null || _prDirty || Math.Abs(_pr.Scale - scale * S) > 0.001f)
            {
                _pr.Scale = scale * S;
                _pr.Accent = _cfg.Accent;
                _pr.Title = _ring.Name;
                _pr.Items = _ring.Items;
                _pr.Selected = Math.Min(_sel, n - 1);
                _pr.Open = 1f;
                if (_prBmp != null) _prBmp.Dispose();
                _prBmp = _pr.Render();
                _prDirty = false;
            }
            else _pr.Scale = scale * S;
            _ringRect = new RectangleF(area.X + (area.Width - size) / 2, area.Y + (area.Height - size) / 2, size, size);
            ui.G.DrawImage(_prBmp, _ringRect);
            ui.Hit(_ringRect, () =>
            {
                var m = ui.Mouse;
                int hit = _pr.HitTest((m.X - _ringRect.X) * S, (m.Y - _ringRect.Y) * S);
                if (hit >= 0) Select(hit);
            });

            // knob simulator
            float by = H - 112;
            string lead = "Try it like the knob";
            float lw = ui.Measure(lead, Th.Body(13)).Width + 14;
            float total = lw + 140 + 10 + 96 + 10 + 140;
            float bx = x0 + (x1 - x0 - total) / 2;
            ui.Text(lead, Th.Body(13), Th.Muted, new RectangleF(bx, by, lw, 44));
            bx += lw;
            ui.Button(new RectangleF(bx, by, 140, 44), "Turn left", Ui.B.Normal, () => OnKnob(-1), false, "back", 22);
            bx += 150;
            ui.Button(new RectangleF(bx, by, 96, 44), "Press", Ui.B.Primary, () => OnKnob(0), false, null, 22);
            bx += 106;
            ui.Button(new RectangleF(bx, by, 140, 44), "Turn right", Ui.B.Normal, () => OnKnob(1), false, "forward", 22);
            float hy = by + 52;
            if (_toast.Length > 0)
            {
                float tw2 = Math.Min(x1 - x0 - 40, ui.Measure(_toast, Th.Body(13)).Width + 32);
                var tr = new RectangleF(x0 + (x1 - x0 - tw2) / 2, hy, tw2, 34);
                ui.Fill(tr, Th.Field, 8);
                ui.Stroke(tr, Th.Hex("#262A34"), 8);
                ui.Text(_toast, Th.Body(13), Th.Soft, tr, StringAlignment.Center);
            }
            else
                ui.Text("Or click this window and use the real knob — it moves the highlight here.", Th.Body(12), Th.Hex("#6E7484"),
                    new RectangleF(x0, hy, x1 - x0, 34), StringAlignment.Center);
        }

        void PaintPanel(Ui ui)
        {
            float x = W - PanelW, px = x + 20, pw = PanelW - 40;
            ui.Fill(new RectangleF(x, HeaderH, PanelW, H - HeaderH), Th.Bar);
            ui.Line(x + 0.5f, HeaderH, x + 0.5f, H, Th.Line);
            var it = Cur;
            int n = _ring.Items.Count;

            // slot header
            ui.Circle(px + 22, 98, 22, Ui.Accent);
            DrawItemIcon(ui, it, new RectangleF(px, 76, 44, 44), Color.White, 20);
            ui.Text("SLOT " + (Math.Min(_sel, n - 1) + 1) + " OF " + n, Th.Head(13.5f), Th.Muted, new RectangleF(px + 58, 76, pw - 58, 18));
            ui.Text(it.Label, Th.Bold(18), Th.Text, new RectangleF(px + 58, 94, pw - 58, 28));

            ui.Caption("Name", px, 132);
            FieldBox(ui, new RectangleF(px, 152, pw, 40), _tbName);

            ui.Caption("Action", px, 206);
            float cw = (pw - 12) / 3f;
            for (int i = 0; i < Types.GetLength(0); i++)
            {
                var r = new RectangleF(px + (i % 3) * (cw + 6), 226 + (i / 3) * 46, cw, 40);
                string id = Types[i, 0];
                ui.Button(r, Types[i, 1], Ui.B.Toggle, () => SetType(id), it.Type == id);
            }

            switch (it.Type)
            {
                case "keys": PaintKeys(ui, it, px, pw); break;
                case "run":
                    ui.Caption("App, file, folder or website", px, 328);
                    FieldBox(ui, new RectangleF(px, 348, pw - 90, 40), _tbRun);
                    ui.Button(new RectangleF(px + pw - 82, 348, 82, 40), "Browse", Ui.B.Normal, BrowseRun);
                    ui.Caption("Arguments", px, 400);
                    FieldBox(ui, new RectangleF(px, 420, pw, 38), _tbArgs);
                    break;
                case "text":
                    ui.Caption("Text to type", px, 328);
                    FieldBox(ui, new RectangleF(px, 348, pw, 120), _tbText);
                    break;
                case "media":
                    ui.Caption("Media key", px, 328);
                    float mw = (pw - 6) / 2f;
                    for (int i = 0; i < Media.GetLength(0); i++)
                    {
                        string v = Media[i, 0];
                        var r = new RectangleF(px + (i % 2) * (mw + 6), 348 + (i / 2) * 46, mw, 40);
                        ui.Button(r, Media[i, 1], Ui.B.Toggle, () => Edit(item => item.Value = v, false), it.Value == v);
                    }
                    break;
                case "ring":
                    ui.Caption("Sub-ring name", px, 328);
                    FieldBox(ui, new RectangleF(px, 348, pw, 40), _tbSub);
                    var target = _cfg.FindByName(it.Value);
                    var br = new RectangleF(px, 398, pw, 40);
                    if (string.IsNullOrWhiteSpace(it.Value)) ui.Button(br, "Type a name first", Ui.B.Normal, null);
                    else if (target != null && target != _ring) ui.Button(br, "Edit this sub-ring", Ui.B.Normal, () => SelectRing(target), false, "forward");
                    else if (target == _ring) ui.Button(br, "A ring can't open itself", Ui.B.Normal, null);
                    else ui.Button(br, "Create sub-ring “" + it.Value + "”", Ui.B.Primary, () => CreateSubRing(it.Value), false, "add");
                    ui.Text("Opens another ring of up to 12 actions. A Back slot is added to it automatically.", Th.Body(12), Th.Muted,
                        new RectangleF(px, 446, pw, 40), StringAlignment.Near, StringAlignment.Near, true);
                    break;
            }

            // icon picker
            ui.Caption("Icon", px, 506);
            var lb = LettersBox(px, pw);
            float ow = ui.Measure("or letters", Th.Body(12.5f)).Width + 4;
            ui.Text("or letters", Th.Body(12.5f), Th.Muted, new RectangleF(lb.X - ow - 8, lb.Y, ow + 4, lb.Height));
            FieldBox(ui, lb, _tbLetters);
            ui.Button(new RectangleF(px + pw - 82, 498, 82, 34), "Image…", Ui.B.Normal, PickImage);
            float gw = (pw - 7 * 5) / 8f;
            for (int i = 0; i < Icons.Grid.Length; i++)
            {
                string key = Icons.Grid[i];
                var r = new RectangleF(px + (i % 8) * (gw + 5), 542 + (i / 8) * 41, gw, 36);
                bool on = string.Equals(it.Icon, key, StringComparison.OrdinalIgnoreCase);
                bool hot = ui.Hot(r);
                ui.Fill(r, on ? Ui.Accent : (hot ? Th.Btn : Th.Field), 8);
                ui.Stroke(r, on ? Ui.Accent : (hot ? Th.Hex("#3A4050") : Th.Hex("#22252E")), 8);
                ui.Glyph(key, 16, on ? Color.White : Th.Soft, r);
                ui.Hit(r, () => { Edit(item => item.Icon = key, false); _syncing = true; _tbLetters.Text = ""; _syncing = false; });
            }

            // footer
            float fy = H - 64;
            bool saving = _dirty;
            var dot = _saveState.StartsWith("Couldn't") ? Th.Bad : saving ? Th.Warn : Th.Good;
            ui.Circle(px + 5, fy - 20, 4, dot);
            ui.Text(_saveState, Th.Body(12.5f), Th.Muted, new RectangleF(px + 16, fy - 30, pw - 16, 20));
            float half = (pw - 8) / 2f;
            ui.Button(new RectangleF(px, fy, half, 44), "Clear slot", Ui.B.Danger, ClearSlot);
            ui.Button(new RectangleF(px + half + 8, fy, half, 44), "Remove slot", Ui.B.Normal, n > 2 ? (Action)RemoveSlot : null);
        }

        void PaintKeys(Ui ui, RingItem it, float px, float pw)
        {
            ui.Caption("Shortcut", px, 328);
            var box = new RectangleF(px, 348, pw, 48);
            ui.Fill(box, Th.Field, 8);
            ui.Stroke(box, _recording ? Ui.Accent : Th.BtnLine, 8, _recording ? 1.5f : 1f);
            if (_recording)
            {
                double t = (DateTime.Now - _recStart).TotalSeconds;
                int a = (int)(150 + 105 * Math.Cos(t * 5.5));
                ui.Text("Press the keys now…  (Esc cancels)", Th.Bold(14), Color.FromArgb(a, Ui.Accent),
                    new RectangleF(box.X + 14, box.Y, box.Width - 20, box.Height));
            }
            else if (string.IsNullOrEmpty(it.Value))
                ui.Text("Not set", Th.Body(14), Th.Muted, new RectangleF(box.X + 14, box.Y, box.Width - 20, box.Height));
            else
            {
                float x = box.X + 10;
                var chords = it.Value.Split(',');
                for (int c = 0; c < chords.Length; c++)
                {
                    if (c > 0)
                    {
                        float w0 = ui.Measure("then", Th.Body(12)).Width + 10;
                        ui.Text("then", Th.Body(12), Th.Muted, new RectangleF(x, box.Y, w0, box.Height), StringAlignment.Center);
                        x += w0;
                    }
                    string chord = chords[c].Trim();
                    var keys = new List<string>();
                    if (chord.EndsWith("++")) { keys.AddRange(chord.Substring(0, chord.Length - 2).Split('+')); keys.Add("+"); }
                    else keys.AddRange(chord.Split('+'));
                    foreach (var k0 in keys)
                    {
                        string k = k0.Trim();
                        if (k.Length == 0) continue;
                        float w = Math.Max(30, ui.Measure(k, Th.Bold(13.5f)).Width + 20);
                        if (x + w > box.Right - 8) break;
                        var kr = new RectangleF(x, box.Y + 9, w, 30);
                        ui.Fill(new RectangleF(kr.X, kr.Y + 2, kr.Width, kr.Height), Th.Hex("#343946"), 6);
                        ui.Fill(new RectangleF(kr.X, kr.Y, kr.Width, kr.Height - 2), Th.Hex("#20242E"), 6);
                        ui.Stroke(new RectangleF(kr.X, kr.Y, kr.Width, kr.Height - 2), Th.Hex("#343946"), 6);
                        ui.Text(k, Th.Bold(13.5f), Th.Hex("#F0F2F6"), new RectangleF(kr.X, kr.Y, kr.Width, kr.Height - 2), StringAlignment.Center);
                        x += w + 6;
                    }
                }
            }
            ui.Button(new RectangleF(px, 404, pw, 40), _recording ? "Cancel" : "Record shortcut", Ui.B.Normal, () =>
            {
                _recording = !_recording;
                _recStart = DateTime.Now;
                Sf.Focus();
                Sf.Invalidate();
            }, false, _recording ? null : "record");
            FieldBox(ui, new RectangleF(px, 452, pw, 38), _tbKeys, _keysInvalid ? (Color?)Th.Bad : null);
            if (_keysInvalid)
                ui.Text("That isn't a shortcut I understand yet", Th.Body(11.5f), Th.Bad, new RectangleF(px, 490, pw, 14));
        }

        void DrawItemIcon(Ui ui, RingItem it, RectangleF r, Color c, float px)
        {
            string icon = (it.Icon ?? "").Trim();
            if (Icons.IsGlyph(icon)) ui.Glyph(icon, px, c, r);
            else if (Icons.IsImage(icon))
            {
                try
                {
                    string full = Environment.ExpandEnvironmentVariables(icon);
                    if (!Path.IsPathRooted(full)) full = Path.Combine(AppConfig.Folder, full);
                    using (var img = Image.FromFile(full))
                        ui.G.DrawImage(img, new RectangleF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.22f, r.Width * 0.56f, r.Height * 0.56f));
                }
                catch { ui.Glyph("photo", px, c, r); }
            }
            else
            {
                string t = icon.Length == 0 ? "?" : icon.Length > 3 ? icon.Substring(0, 3) : icon;
                ui.Text(t, Th.Head(t.Length > 1 ? px * 0.85f : px * 1.05f), c, r, StringAlignment.Center);
            }
        }

        // ---------------------------------------------------------------- settings page
        void PaintSettings(Ui ui)
        {
            float cw = Math.Min(1000, W - 40), left = (W - cw) / 2, col = (cw - 16) / 2, top = HeaderH + 20;
            float rx = left + col + 16;

            // RING LOOK
            var c1 = Card(ui, left, top, col, 446, "Ring look");
            ui.Caption("Highlight colour", c1.X + 22, c1.Y + 66);
            float sx = c1.X + 22;
            bool custom = true;
            foreach (var hex in Swatches)
            {
                var col1 = Th.Hex(hex);
                bool on = col1.ToArgb() == _cfg.Accent.ToArgb();
                if (on) custom = false;
                Swatch(ui, sx, c1.Y + 90, col1, on, () => SetAccent(col1));
                sx += 52;
            }
            var cr = new RectangleF(sx, c1.Y + 90, 40, 40);
            if (custom) Swatch(ui, sx, c1.Y + 90, _cfg.Accent, true, PickAccent);
            else
            {
                ui.Fill(cr, ui.Hot(cr) ? Th.BtnHot : Th.Btn, 20);
                ui.Stroke(cr, Th.BtnLine, 20);
                ui.Glyph("color", 16, Th.Sub, cr);
                ui.Hit(cr, PickAccent);
            }

            ui.Caption("Ring size", c1.X + 22, c1.Y + 150);
            int pct = (int)Math.Round(_cfg.Size * 100);
            ui.Text(pct + "%", Th.Bold(13), Th.Text, new RectangleF(c1.Right - 122, c1.Y + 150, 100, 18), StringAlignment.Far);
            _sliderRect = new RectangleF(c1.X + 22, c1.Y + 184, col - 44, 6);
            Slider(ui, _sliderRect, (float)((_cfg.Size - 0.6) / 1.0));

            // Liquid feel + live demo ring
            float demo = 128;
            float lwid = col - 44 - demo - 18;
            ui.Caption("Liquid feel", c1.X + 22, c1.Y + 220);
            int lq = (int)Math.Round(_cfg.Liquid * 100);
            ui.Text(lq + "", Th.Bold(13), Th.Text, new RectangleF(c1.X + 22 + lwid - 60, c1.Y + 220, 60, 18), StringAlignment.Far);
            _liquidRect = new RectangleF(c1.X + 22, c1.Y + 256, lwid, 6);
            Slider(ui, _liquidRect, (float)_cfg.Liquid);
            ui.Text("Solid", Th.Body(12), Th.Muted, new RectangleF(c1.X + 22, c1.Y + 272, 80, 18));
            ui.Text("Gooey", Th.Body(12), Th.Muted, new RectangleF(c1.X + 22 + lwid - 80, c1.Y + 272, 80, 18), StringAlignment.Far);
            var demoR = new RectangleF(c1.Right - 22 - demo, c1.Y + 194, demo, demo);
            _mini.Scale = 1f;
            _mini.Scale = demo / _mini.PixelSize * S;
            _mini.Accent = _cfg.Accent;
            _mini.Liquid = (float)_cfg.Liquid;
            _mini.EaseAmount = (float)_cfg.Ease;
            if (_miniBmp != null) _miniBmp.Dispose();
            _miniBmp = _mini.Render();
            ui.G.DrawImage(_miniBmp, demoR);

            // Ease: how the highlight moves
            ui.Caption("Ease", c1.X + 22, c1.Y + 298);
            int ez = (int)Math.Round(_cfg.Ease * 100);
            ui.Text(ez + "", Th.Bold(13), Th.Text, new RectangleF(c1.X + 22 + lwid - 60, c1.Y + 298, 60, 18), StringAlignment.Far);
            _easeRect = new RectangleF(c1.X + 22, c1.Y + 330, lwid, 6);
            Slider(ui, _easeRect, (float)_cfg.Ease);
            ui.Text("Stiff", Th.Body(12), Th.Muted, new RectangleF(c1.X + 22, c1.Y + 344, 80, 18));
            ui.Text("Soft", Th.Body(12), Th.Muted, new RectangleF(c1.X + 22 + lwid - 80, c1.Y + 344, 80, 18), StringAlignment.Far);

            ui.Caption("Close the ring after", c1.X + 22, c1.Y + 370);
            var touts = new[] { 3.0, 6.0, 10.0, 0.0 };
            var tnames = new[] { "3 s", "6 s", "10 s", "Never" };
            float sw = (col - 44 - 18) / 4f;
            for (int i = 0; i < 4; i++)
            {
                double v = touts[i];
                ui.Button(new RectangleF(c1.X + 22 + i * (sw + 6), c1.Y + 390, sw, 40), tnames[i], Ui.B.Toggle,
                    () => { _cfg.TimeoutSeconds = v; Changed(); }, Math.Abs(_cfg.TimeoutSeconds - v) < 0.01);
            }


            // KNOB & BEHAVIOUR
            var c2 = Card(ui, rx, top, col, 446, "Knob & behaviour");
            bool startup = KnobRingApp.IsStartup();
            ToggleRow(ui, c2, 0, "Start with Windows", "KnobRing runs quietly in the tray after you sign in.", startup,
                () => KnobRingApp.SetStartup(!KnobRingApp.IsStartup()));
            {
                // row 1: how the knob opens the ring
                var r1 = new RectangleF(c2.X + 16, c2.Y + 60 + 1 * 70, c2.Width - 32, 66);
                ui.Text("Open the ring with", Th.Bold(15), Th.Text, new RectangleF(r1.X + 6, r1.Y + 4, 200, 22));
                var opts = new[] { "double", "single", "off" };
                var names = new[] { "Double press", "Single press", "Off" };
                float ow = (r1.Width - 12 - 12) / 3f;
                for (int k = 0; k < 3; k++)
                {
                    string v = opts[k];
                    ui.Button(new RectangleF(r1.X + 6 + k * (ow + 6), r1.Y + 28, ow, 34), names[k], Ui.B.Toggle,
                        () => { _cfg.OpenWith = v; _cfg.KnobPressOpensRing = v != "off"; Changed(); }, _cfg.OpenWith == v);
                }
                ui.Line(r1.X, r1.Bottom + 2, r1.Right, r1.Bottom + 2, Th.Hex("#1A1D25"));
            }
            ToggleRow(ui, c2, 2, "Remember the last action", "The ring opens on the slot you used last, so press–press repeats it.", _cfg.RememberLast,
                () => { _cfg.RememberLast = !_cfg.RememberLast; Changed(); });
            ToggleRow(ui, c2, 3, "Show the action name in the centre", "The middle of the ring names the highlighted slot.", _cfg.ShowLabels,
                () => { _cfg.ShowLabels = !_cfg.ShowLabels; Changed(); });
            ui.Caption("Ring opens at", c2.X + 22, c2.Y + 348);
            float hw = (col - 44 - 6) / 2f;
            ui.Button(new RectangleF(c2.X + 22, c2.Y + 370, hw, 42), "Mouse pointer", Ui.B.Toggle,
                () => { _cfg.Position = "mouse"; Changed(); }, _cfg.Position != "center");
            ui.Button(new RectangleF(c2.X + 28 + hw, c2.Y + 370, hw, 42), "Screen centre", Ui.B.Toggle,
                () => { _cfg.Position = "center"; Changed(); }, _cfg.Position == "center");

            // KEYBOARD
            float top2 = top + 446 + 14;
            var c3 = Card(ui, left, top2, col, 320, "Keyboard");
            var dev = new RectangleF(c3.X + 22, c3.Y + 64, col - 44, 70);
            ui.Fill(dev, Th.Field, 10);
            ui.Stroke(dev, Th.Hex("#22252E"), 10);
            ui.Glyph("keyboard", 28, Th.Sub, new RectangleF(dev.X + 12, dev.Y, 44, dev.Height));
            ui.Text("AULA F75 knob", Th.Bold(16), Th.Text, new RectangleF(dev.X + 66, dev.Y + 12, dev.Width - 80, 24));
            ui.Text("Press: " + VkName(_cfg.KnobPressVk) + "  ·  Right: " + VkName(_cfg.KnobRightVk) + "  ·  Left: " + VkName(_cfg.KnobLeftVk),
                Th.Body(12.5f), Th.Muted, new RectangleF(dev.X + 66, dev.Y + 36, dev.Width - 80, 20));
            var msgR = new RectangleF(c3.X + 22, c3.Y + 144, col - 44, 48);
            if (_learnStep > 0)
            {
                string prompt = _learnStep == 1 ? "Step 1 of 3: press the knob once."
                              : _learnStep == 2 ? "Step 2 of 3: turn the knob one click to the right."
                              : "Step 3 of 3: turn the knob one click to the left.";
                double t = (DateTime.Now - _learnStart).TotalSeconds;
                int a = (int)(190 + 65 * Math.Cos(t * 5));
                ui.Text(prompt, Th.Bold(15), Color.FromArgb(a, Ui.Accent), msgR, StringAlignment.Near, StringAlignment.Near, true);
            }
            else
                ui.Text(_learnMsg.Length > 0 ? _learnMsg : "If the knob doesn't open the ring, use Learn my knob. With this window in front, the lights below react to the knob.",
                    Th.Body(12.5f), _learnMsg.StartsWith("Got it") ? Th.Good : Th.Muted, msgR, StringAlignment.Near, StringAlignment.Near, true);
            var lights = new[] { "Turn left", "Press", "Turn right" };
            float lw = (col - 44 - 12) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(c3.X + 22 + i * (lw + 6), c3.Y + 200, lw, 40);
                bool lit = (DateTime.Now - _knobFlash[i]).TotalMilliseconds < 450;
                ui.Fill(r, lit ? Ui.Accent : Th.Field, 20);
                ui.Stroke(r, lit ? Ui.Accent : Th.BtnLine, 20);
                ui.Text(lights[i], Th.Bold(13.5f), lit ? Color.White : Th.Muted, r, StringAlignment.Center);
            }
            if (_learnStep > 0)
                ui.Button(new RectangleF(c3.X + 22, c3.Y + 256, col - 44, 44), "Cancel", Ui.B.Normal, CancelLearn);
            else
                ui.Button(new RectangleF(c3.X + 22, c3.Y + 256, col - 44, 44), "Learn my knob", Ui.B.Primary, StartLearn, false, "keyboard");

            // PROFILES
            var c4 = Card(ui, rx, top2, col, 320, "Profiles");
            float bw = col - 44;
            ui.Button(new RectangleF(c4.X + 22, c4.Y + 64, bw, 44), "Export profiles…", Ui.B.Normal, ExportProfiles, false, "upload");
            ui.Button(new RectangleF(c4.X + 22, c4.Y + 116, bw, 44), "Import profiles…", Ui.B.Normal, ImportProfiles, false, "download");
            ui.Button(new RectangleF(c4.X + 22, c4.Y + 168, bw, 44), "Open settings folder", Ui.B.Normal,
                () => { try { Process.Start("explorer.exe", "\"" + AppConfig.Folder + "\""); } catch { } }, false, "folder");
            ui.Button(new RectangleF(c4.X + 22, c4.Y + 220, bw, 44), "Reset everything to default", Ui.B.Danger, ResetAll);

            ui.Text(_saveState, Th.Body(12), Th.Hex("#6E7484"), new RectangleF(left, top2 + 320 + 14, cw, 20), StringAlignment.Center);
        }

        RectangleF Card(Ui ui, float x, float y, float w, float h, string title)
        {
            var r = new RectangleF(x, y, w, h);
            ui.Fill(r, Th.Bar, 14);
            ui.Stroke(r, Th.Line, 14);
            ui.Text(title.ToUpperInvariant(), Th.Head(21), Th.Text, new RectangleF(x + 22, y + 18, w - 44, 30));
            return r;
        }

        void Swatch(Ui ui, float x, float y, Color c, bool on, Action pick)
        {
            var r = new RectangleF(x, y, 40, 40);
            if (on) ui.Stroke(new RectangleF(x - 4, y - 4, 48, 48), Color.White, 24, 2);
            ui.Fill(r, c, 20);
            ui.Hit(r, pick);
        }

        void ToggleRow(Ui ui, RectangleF card, int i, string name, string desc, bool on, Action flip)
        {
            var r = new RectangleF(card.X + 16, card.Y + 60 + i * 70, card.Width - 32, 66);
            if (ui.Hot(r)) ui.Fill(r, Th.Hover, 8);
            ui.Text(name, Th.Bold(15), Th.Text, new RectangleF(r.X + 6, r.Y + 10, r.Width - 70, 22));
            ui.Text(desc, Th.Body(12.5f), Th.Muted, new RectangleF(r.X + 6, r.Y + 33, r.Width - 76, 36), StringAlignment.Near, StringAlignment.Near, true);
            ui.Switch(r.Right - 52, r.Y + 21, on);
            if (i < 3) ui.Line(r.X, r.Bottom + 2, r.Right, r.Bottom + 2, Th.Hex("#1A1D25"));
            ui.Hit(r, () => { flip(); Sf.Invalidate(); });
        }

        // ================================================================ actions
        void SetType(string type)
        {
            var it = Cur;
            if (it.Type == type) return;
            it.Type = type;
            it.Value = type == "media" ? "playpause" : "";
            it.Args = "";
            _recording = false;
            SyncFields();
            Changed(true);
        }

        void AddSlot()
        {
            if (_ring.Items.Count >= 12) return;
            _ring.Items.Add(new RingItem { Label = "New action", Icon = "add", Type = "keys" });
            _pr.ResetAnimation();
            Changed(true);
            Select(_ring.Items.Count - 1);
        }

        void RemoveSlot()
        {
            if (_ring.Items.Count <= 2) return;
            _ring.Items.RemoveAt(Math.Min(_sel, _ring.Items.Count - 1));
            _pr.ResetAnimation();
            Changed(true);
            Select(Math.Max(0, _sel - 1));
        }

        void ClearSlot()
        {
            _ring.Items[Math.Min(_sel, _ring.Items.Count - 1)] = new RingItem { Label = "Empty", Icon = "add", Type = "keys" };
            SyncFields();
            Changed(true);
        }

        void SetAccent(Color c)
        {
            _cfg.Accent = c;
            Ui.Accent = c;
            Changed();
        }

        void PickAccent()
        {
            using (var d = new ColorDialog { Color = _cfg.Accent, FullOpen = true })
                if (d.ShowDialog(this) == DialogResult.OK) SetAccent(d.Color);
        }

        void SlideTo(float x)
        {
            if (_drag == 3)
            {
                float f3 = Math.Max(0, Math.Min(1, (x - _easeRect.X) / _easeRect.Width));
                double v3 = Math.Round(f3 * 100) / 100.0;
                if (Math.Abs(v3 - _cfg.Ease) > 0.001) { _cfg.Ease = v3; Changed(); }
                return;
            }
            if (_drag == 2)
            {
                float f2 = Math.Max(0, Math.Min(1, (x - _liquidRect.X) / _liquidRect.Width));
                double v2 = Math.Round(f2 * 100) / 100.0;
                if (Math.Abs(v2 - _cfg.Liquid) > 0.001) { _cfg.Liquid = v2; Changed(); }
                return;
            }
            float f = Math.Max(0, Math.Min(1, (x - _sliderRect.X) / _sliderRect.Width));
            double v = Math.Round((0.6 + f) * 10) / 10.0;
            if (Math.Abs(v - _cfg.Size) > 0.001) { _cfg.Size = v; Changed(); }
        }

        void Slider(Ui ui, RectangleF track, float frac)
        {
            ui.Fill(track, Th.BtnLine, 3);
            ui.Fill(new RectangleF(track.X, track.Y, track.Width * frac, track.Height), Ui.Accent, 3);
            float kx = track.X + track.Width * frac;
            ui.Circle(kx, track.Y + 3, 11, Color.White);
            ui.Circle(kx, track.Y + 3, 4, Ui.Accent);
        }

        void BrowseRun()
        {
            using (var d = new OpenFileDialog { Title = "Pick an app or file to open", Filter = "All files (*.*)|*.*|Programs (*.exe)|*.exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _tbRun.Text = d.FileName;
                if (Cur.Label == "New action" || Cur.Label == "Empty") _tbName.Text = Path.GetFileNameWithoutExtension(d.FileName);
            }
        }

        void PickImage()
        {
            using (var d = new OpenFileDialog { Title = "Pick an icon image", Filter = "Images (*.png;*.ico)|*.png;*.ico" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string dir = Path.Combine(AppConfig.Folder, "icons");
                    Directory.CreateDirectory(dir);
                    string name = Path.GetFileName(d.FileName);
                    string dest = Path.Combine(dir, name);
                    if (!string.Equals(Path.GetFullPath(d.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                        File.Copy(d.FileName, dest, true);
                    Edit(it => it.Icon = "icons\\" + name, false);
                    _syncing = true; _tbLetters.Text = ""; _syncing = false;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn't use that image"); }
            }
        }

        void CreateSubRing(string name)
        {
            name = name.Trim();
            if (name.Length == 0 || _cfg.FindByName(name) != null) return;
            var r = new Ring { Name = name };
            r.Items.Add(new RingItem { Label = "New action", Icon = "add", Type = "keys" });
            r.Items.Add(new RingItem { Label = "New action", Icon = "add", Type = "keys" });
            r.Items.Add(new RingItem { Label = "New action", Icon = "add", Type = "keys" });
            r.Items.Add(new RingItem { Label = "New action", Icon = "add", Type = "keys" });
            _cfg.Rings.Add(r);
            Changed();
            SelectRing(r);
        }

        void AddApp()
        {
            var have = new List<string>();
            foreach (var r in _cfg.Rings) have.AddRange(r.Apps);
            using (var f = new AddAppForm(have))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string exe = f.PickedExe.ToLowerInvariant();
                var existing = _cfg.FindForApp(exe);
                if (existing != null && existing.Apps.Contains(exe)) { SelectRing(existing); return; }
                string name = f.PickedName;
                int k = 2;
                while (_cfg.FindByName(name) != null) name = f.PickedName + " " + k++;
                var ring = new Ring { Name = name };
                ring.Apps.Add(exe);
                foreach (var it in _cfg.DefaultRing.Items) ring.Items.Add(it.Clone());
                _cfg.Rings.Add(ring);
                Changed();
                SelectRing(ring);
            }
        }

        void ProfileMenu(Ring r)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
            menu.BackColor = Th.Hex("#16181E");
            menu.ForeColor = Th.Text;
            bool isDefault = r == _cfg.DefaultRing;
            menu.Items.Add("Rename…", null, (s, e) => RenameRing(r)).Enabled = !isDefault;
            if (!_cfg.IsSubRing(r) && !isDefault)
                menu.Items.Add("Change app (.exe)…", null, (s, e) => ChangeApp(r));
            menu.Items.Add("Duplicate", null, (s, e) => DuplicateRing(r));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete", null, (s, e) => DeleteRing(r)).Enabled = !isDefault;
            menu.Closed += (s, e) => BeginInvoke((Action)(() => menu.Dispose()));
            menu.Show(Cursor.Position);
        }

        void RenameRing(Ring r)
        {
            using (var p = new PromptForm("Rename", "New name", r.Name))
            {
                if (p.ShowDialog(this) != DialogResult.OK) return;
                string name = p.Value;
                var other = _cfg.FindByName(name);
                if (other != null && other != r) { MessageBox.Show(this, "Another ring is already called “" + name + "”.", "KnobRing"); return; }
                string old = r.Name;
                r.Name = name;
                foreach (var ring in _cfg.Rings)
                    foreach (var it in ring.Items)
                        if (it.Type == "ring" && string.Equals(it.Value, old, StringComparison.OrdinalIgnoreCase)) it.Value = name;
                SyncFields();
                Changed();
            }
        }

        void ChangeApp(Ring r)
        {
            using (var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the app's .exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                r.Apps.Clear();
                r.Apps.Add(Path.GetFileName(d.FileName).ToLowerInvariant());
                Changed();
            }
        }

        void DuplicateRing(Ring r)
        {
            string name = r.Name + " copy";
            int k = 2;
            while (_cfg.FindByName(name) != null) name = r.Name + " copy " + k++;
            var copy = new Ring { Name = name };
            foreach (var it in r.Items) copy.Items.Add(it.Clone());
            _cfg.Rings.Add(copy);   // no apps: it becomes a sub-ring until an app is set
            Changed();
            SelectRing(copy);
        }

        void DeleteRing(Ring r)
        {
            if (r == _cfg.DefaultRing) return;
            if (MessageBox.Show(this, "Delete the ring “" + r.Name + "”?", "KnobRing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _cfg.Rings.Remove(r);
            Changed();
            if (_ring == r) SelectRing(_cfg.DefaultRing); else Sf.Invalidate();
        }

        void ExportProfiles()
        {
            using (var d = new SaveFileDialog { Filter = "KnobRing profiles (*.json)|*.json", FileName = "KnobRing profiles.json" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, _cfg.ToJson()); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn't export"); }
            }
        }

        void ImportProfiles()
        {
            using (var d = new OpenFileDialog { Filter = "KnobRing profiles (*.json)|*.json" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var cfg = AppConfig.Parse(File.ReadAllText(d.FileName));
                    _cfg = cfg;
                    Ui.Accent = cfg.Accent;
                    Changed();
                    SelectRing(_cfg.DefaultRing);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "That file isn't a KnobRing profile"); }
            }
        }

        void ResetAll()
        {
            if (MessageBox.Show(this, "Put every ring and setting back to how KnobRing started? This can't be undone.", "KnobRing",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _cfg = AppConfig.Parse(DefaultJson.Text);
            Ui.Accent = _cfg.Accent;
            Changed();
            SelectRing(_cfg.DefaultRing);
        }

        // ================================================================ lifetime
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PlaceBoxes();
            Sf.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty) { _save.Stop(); Save(); }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Dispose(); _save.Dispose(); _tick.Dispose();
                _pr.Dispose();
                _mini.Dispose();
                _miniCycle.Dispose();
                if (_miniBmp != null) _miniBmp.Dispose();
                if (_prBmp != null) _prBmp.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

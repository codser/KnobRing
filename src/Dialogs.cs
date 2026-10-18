using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KnobRing
{
    internal class DarkForm : Form
    {
        protected readonly Surface Sf = new Surface();
        protected float S = 1f;

        public DarkForm(int w, int h)
        {
            using (var g = CreateGraphics()) S = g.DpiX / 96f;
            Sf.S = S;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Th.Bg;
            ForeColor = Th.Text;
            ClientSize = new Size((int)(w * S), (int)(h * S));
            Sf.Dock = DockStyle.Fill;
            Controls.Add(Sf);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        protected float W { get { return Sf.ClientSize.Width / S; } }
        protected float H { get { return Sf.ClientSize.Height / S; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected DarkTextBox MakeBox(float px)
        {
            var tb = new DarkTextBox { Font = new Font("Segoe UI", px * S, GraphicsUnit.Pixel) };
            Sf.Controls.Add(tb);
            return tb;
        }

        // Places a textbox inside a logical box (the box itself is painted)
        protected void Place(TextBox tb, RectangleF box, bool visible = true)
        {
            tb.Visible = visible;
            if (!visible) return;
            int pad = (int)(12 * S);
            if (tb.Multiline)
                tb.Bounds = new Rectangle((int)(box.X * S) + pad, (int)(box.Y * S) + (int)(10 * S),
                                          (int)(box.Width * S) - pad * 2, (int)(box.Height * S) - (int)(20 * S));
            else
            {
                int h = tb.PreferredHeight;
                tb.Bounds = new Rectangle((int)(box.X * S) + pad, (int)(box.Y * S + (box.Height * S - h) / 2),
                                          (int)(box.Width * S) - pad * 2, h);
            }
        }

        protected void FieldBox(Ui ui, RectangleF r, TextBox tb, Color? border = null)
        {
            bool focus = tb != null && tb.Focused;
            ui.Fill(r, Th.Field, 8);
            ui.Stroke(r, border ?? (focus ? Ui.Accent : Th.BtnLine), 8);
        }
    }

    // Asks for one line of text
    internal sealed class PromptForm : DarkForm
    {
        readonly DarkTextBox _tb;
        readonly string _label;
        public string Value { get { return _tb.Text.Trim(); } }

        public PromptForm(string title, string label, string initial) : base(420, 176)
        {
            Text = title;
            _label = label;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            _tb = MakeBox(15);
            _tb.Text = initial ?? "";
            _tb.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ok(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; }
            };
            Sf.Painter = Draw;
            Shown += (s, e) => { _tb.Focus(); _tb.SelectAll(); };
            Sf.Resize += (s, e) => Place(_tb, new RectangleF(20, 44, W - 40, 42));
            Place(_tb, new RectangleF(20, 44, W - 40, 42));
        }

        void Ok() { if (Value.Length > 0) DialogResult = DialogResult.OK; }

        void Draw(Ui ui)
        {
            ui.Caption(_label, 20, 18, W - 40);
            FieldBox(ui, new RectangleF(20, 44, W - 40, 42), _tb);
            ui.Button(new RectangleF(W - 20 - 110, 112, 110, 44), "OK", Ui.B.Primary, Ok);
            ui.Button(new RectangleF(W - 20 - 110 - 10 - 100, 112, 100, 44), "Cancel", Ui.B.Normal,
                () => DialogResult = DialogResult.Cancel);
        }
    }

    // Lists running apps so one can be given its own ring
    internal sealed class AddAppForm : DarkForm
    {
        class AppEntry { public string Name, Exe, Path; public Bitmap Icon; }

        readonly List<AppEntry> _apps = new List<AppEntry>();
        float _scroll;
        public string PickedName, PickedExe;

        public AddAppForm(ICollection<string> existing) : base(460, 560)
        {
            Text = "Add an app";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            LoadApps(existing);
            Sf.Painter = Draw;
            Sf.Wheel = (p, d) => { _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - d / 2f)); Sf.Invalidate(); };
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
        }

        float ListTop { get { return 96; } }
        float ListBottom { get { return H - 84; } }
        float MaxScroll { get { return Math.Max(0, _apps.Count * 60 - (ListBottom - ListTop)); } }

        void LoadApps(ICollection<string> existing)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    string path = null, exe = p.ProcessName + ".exe", name = p.ProcessName;
                    try
                    {
                        path = p.MainModule.FileName;
                        exe = System.IO.Path.GetFileName(path);
                        var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                        if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
                    }
                    catch { }
                    if (exe.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (existing.Contains(exe.ToLowerInvariant()) || !seen.Add(exe)) continue;
                    var e = new AppEntry { Name = name, Exe = exe, Path = path };
                    if (path != null)
                        try { using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path)) e.Icon = ic.ToBitmap(); } catch { }
                    _apps.Add(e);
                }
                catch { }
                finally { p.Dispose(); }
            }
            _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        void Pick(string name, string exe)
        {
            PickedName = name;
            PickedExe = exe;
            DialogResult = DialogResult.OK;
        }

        void Browse()
        {
            using (var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the app's .exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string name = System.IO.Path.GetFileNameWithoutExtension(d.FileName);
                try
                {
                    var desc = FileVersionInfo.GetVersionInfo(d.FileName).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
                }
                catch { }
                Pick(name, System.IO.Path.GetFileName(d.FileName));
            }
        }

        void Draw(Ui ui)
        {
            ui.Text("ADD AN APP", Th.Head(24), Th.Text, new RectangleF(22, 18, W - 44, 32));
            ui.Text("Apps running right now. The new ring starts as a copy of Default.", Th.Body(13), Th.Muted,
                new RectangleF(22, 54, W - 44, 30));

            var clip = new RectangleF(0, ListTop, W, ListBottom - ListTop);
            ui.G.SetClip(clip);
            float y = ListTop - _scroll;
            if (_apps.Count == 0)
                ui.Text("No other apps with a window are open. Use Browse instead.", Th.Body(13), Th.Muted,
                    new RectangleF(22, ListTop, W - 44, 40));
            foreach (var a in _apps)
            {
                var r = new RectangleF(20, y, W - 40, 54);
                if (r.Bottom > ListTop && r.Top < ListBottom)
                {
                    bool hot = ui.Hot(r) && clip.Contains(ui.Mouse);
                    ui.Fill(r, hot ? Th.BtnHot : Th.Field, 10);
                    ui.Stroke(r, Th.Hex("#22252E"), 10);
                    var tile = new RectangleF(r.X + 10, r.Y + 10, 34, 34);
                    if (a.Icon != null) ui.G.DrawImage(a.Icon, tile);
                    else
                    {
                        ui.Fill(tile, Th.Hex("#20242E"), 8);
                        ui.Text(Badge(a.Name), Th.Head(16), Th.Text, tile, StringAlignment.Center);
                    }
                    ui.Text(a.Name, Th.Bold(14.5f), Th.Text, new RectangleF(r.X + 56, r.Y + 7, r.Width - 66, 22));
                    ui.Text(a.Exe, Th.Body(12), Th.Muted, new RectangleF(r.X + 56, r.Y + 28, r.Width - 66, 18));
                    var visible = RectangleF.Intersect(r, clip);
                    var name = a.Name; var exe = a.Exe;
                    ui.Hit(visible, () => Pick(name, exe));
                }
                y += 60;
            }
            ui.G.ResetClip();
            ui.Line(0, ListBottom + 8, W, ListBottom + 8, Th.Line);
            ui.Button(new RectangleF(20, H - 64, W - 40, 46), "Browse for an .exe…", Ui.B.Ghost, Browse);
        }

        public static string Badge(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "?";
            var parts = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && char.IsLetterOrDigit(parts[1][0]))
                return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0]));
            return name.Length > 1 ? char.ToUpperInvariant(name[0]) + name.Substring(1, 1).ToLowerInvariant() : name.ToUpperInvariant();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var a in _apps) if (a.Icon != null) a.Icon.Dispose();
            base.Dispose(disposing);
        }
    }
}

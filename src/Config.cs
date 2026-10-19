using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace KnobRing
{
    public class RingItem
    {
        public string Label = "";
        public string Icon = "";
        public string Type = "keys";   // keys | run | text | media | ring | transform
        public string Value = "";
        public string Args = "";

        public RingItem Clone()
        {
            return new RingItem { Label = Label, Icon = Icon, Type = Type, Value = Value, Args = Args };
        }
    }

    public class Ring
    {
        public string Name = "";
        public List<string> Apps = new List<string>();
        public List<RingItem> Items = new List<RingItem>();
    }

    // Premiere Pro extras, edited from the KnobRing panel inside Premiere (Window > Extensions > KnobRing)
    public class PremiereSettings
    {
        public bool KnobScrub = true;       // turning the knob in Premiere moves the playhead (ring closed)
        public bool WheelScrub = true;      // so does the mouse wheel, anywhere over Premiere
        public bool Invert = false;
        public double SlowFrames = 1, NormalFrames = 5, FastFrames = 25;   // per knob click / wheel notch: Ctrl, plain, Shift
        public Dictionary<string, double[]> Steps = new Dictionary<string, double[]>();   // transform id -> normal, fine, coarse

        public double[] StepsFor(TransformProp p)
        {
            double[] v;
            if (Steps.TryGetValue(p.Id, out v) && v != null && v.Length == 3) return v;
            return new[] { p.Step, p.Fine, p.Coarse };
        }
    }

    public class AppConfig
    {
        public List<Ring> Rings = new List<Ring>();
        public double TimeoutSeconds = 6;          // 0 = never
        public Color Accent = Color.FromArgb(47, 107, 255);
        public double Size = 1.0;
        public bool KnobPressOpensRing = true;
        public string Position = "mouse";          // mouse | center
        public bool RememberLast = true;
        public bool ShowLabels = true;
        public double Liquid = 0.35;                // 0 = solid glide, 1 = very gooey
        public double Ease = 0.25;                  // 0 = stiff (no overshoot), 1 = soft and bouncy
        public bool TransformsAdded = false;
        public PremiereSettings Premiere = new PremiereSettings();        // 1.3 added a Transforms slot to the Adobe rings once
        public int KnobPressVk = 0xAD, KnobRightVk = 0xAF, KnobLeftVk = 0xAE;   // learned from the keyboard

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KnobRing"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "rings.json"); } }

        // ------------------------------------------------------------ lookups
        public Ring DefaultRing
        {
            get
            {
                foreach (var r in Rings)
                    if (r.Apps.Count == 0 && r.Name.Equals("Default", StringComparison.OrdinalIgnoreCase)) return r;
                foreach (var r in Rings)
                    if (r.Apps.Count == 0) return r;
                return Rings.Count > 0 ? Rings[0] : null;
            }
        }

        public bool IsSubRing(Ring r) { return r.Apps.Count == 0 && r != DefaultRing; }

        public Ring FindForApp(string exe)
        {
            exe = (exe ?? "").ToLowerInvariant();
            foreach (var r in Rings)
                foreach (var a in r.Apps)
                    if (a == exe) return r;
            return DefaultRing;
        }

        public Ring FindByName(string name)
        {
            foreach (var r in Rings)
                if (r.Name.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public AppConfig Clone() { return Parse(ToJson()); }

        // ------------------------------------------------------------ load
        public static AppConfig LoadOrCreate(out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Folder);
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, DefaultJson.Text);
                return Parse(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { return Parse(DefaultJson.Text); } catch { return new AppConfig(); }
            }
        }

        public static AppConfig Parse(string json)
        {
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(json) as IDictionary<string, object>;
            if (root == null) throw new Exception("rings.json is not a JSON object");
            var cfg = new AppConfig();

            var settings = Get(root, "settings") as IDictionary<string, object>;
            if (settings != null)
            {
                cfg.TimeoutSeconds = Math.Max(0, Num(Get(settings, "timeoutSeconds"), 6));
                cfg.Size = Math.Max(0.6, Math.Min(2.0, Num(Get(settings, "size"), 1.0)));
                var acc = Str(Get(settings, "accent"));
                if (acc.Length > 0) { try { cfg.Accent = ColorTranslator.FromHtml(acc); } catch { } }
                cfg.KnobPressOpensRing = Bool(Get(settings, "knobPressOpensRing"), true);
                cfg.RememberLast = Bool(Get(settings, "rememberLast"), true);
                cfg.ShowLabels = Bool(Get(settings, "showLabels"), true);
                cfg.Liquid = Math.Max(0, Math.Min(1, Num(Get(settings, "liquid"), 35) / 100.0));
                cfg.Ease = Math.Max(0, Math.Min(1, Num(Get(settings, "ease"), 25) / 100.0));
                cfg.TransformsAdded = Bool(Get(settings, "transformsAdded"), false);
                var pos = Str(Get(settings, "position")).ToLowerInvariant();
                cfg.Position = pos == "center" || pos == "centre" ? "center" : "mouse";
                var kk = Get(settings, "knobKeys") as IDictionary<string, object>;
                if (kk != null)
                {
                    cfg.KnobPressVk = (int)Num(Get(kk, "press"), 0xAD);
                    cfg.KnobRightVk = (int)Num(Get(kk, "right"), 0xAF);
                    cfg.KnobLeftVk = (int)Num(Get(kk, "left"), 0xAE);
                }
            }

            var pr = Get(root, "premiere") as IDictionary<string, object>;
            if (pr != null)
            {
                var ps = cfg.Premiere;
                ps.KnobScrub = Bool(Get(pr, "knobScrub"), true);
                ps.WheelScrub = Bool(Get(pr, "wheelScrub"), true);
                ps.Invert = Bool(Get(pr, "invert"), false);
                var sf = Get(pr, "scrubFrames") as IDictionary<string, object>;
                if (sf != null)
                {
                    ps.SlowFrames = Math.Max(0.01, Num(Get(sf, "slow"), 1));
                    ps.NormalFrames = Math.Max(0.01, Num(Get(sf, "normal"), 5));
                    ps.FastFrames = Math.Max(0.01, Num(Get(sf, "fast"), 25));
                }
                var st = Get(pr, "transformSteps") as IDictionary<string, object>;
                if (st != null)
                    foreach (var kv in st)
                    {
                        var arr = new List<double>();
                        foreach (var o in List(kv.Value)) arr.Add(Math.Abs(Num(o, 0)));
                        if (arr.Count == 3 && arr[0] > 0 && arr[1] > 0 && arr[2] > 0) ps.Steps[kv.Key] = arr.ToArray();
                    }
            }

            foreach (var ro in List(Get(root, "rings")))
            {
                var rd = ro as IDictionary<string, object>;
                if (rd == null) continue;
                var ring = new Ring { Name = Str(Get(rd, "name")) };
                foreach (var a in List(Get(rd, "apps")))
                {
                    var s = Str(a).Trim().ToLowerInvariant();
                    if (s.Length > 0) ring.Apps.Add(s.EndsWith(".exe") ? s : s + ".exe");
                }
                foreach (var io in List(Get(rd, "items")))
                {
                    var id = io as IDictionary<string, object>;
                    if (id == null) continue;
                    var it = new RingItem
                    {
                        Label = Str(Get(id, "label")),
                        Icon = Str(Get(id, "icon")),
                        Type = Str(Get(id, "type")).ToLowerInvariant(),
                        Value = Str(Get(id, "value")),
                        Args = Str(Get(id, "args")),
                    };
                    if (it.Type.Length == 0) it.Type = "keys";
                    ring.Items.Add(it);
                    if (ring.Items.Count >= 12) break;
                }
                if (ring.Items.Count > 0) cfg.Rings.Add(ring);
            }
            if (cfg.Rings.Count == 0) throw new Exception("rings.json has no rings with items");
            return cfg;
        }

        // ------------------------------------------------------------ save
        static readonly string[] Help =
        {
            "KnobRing settings. Easiest to change from the KnobRing window (double-click the tray icon).",
            "If you edit this file by hand, save it and the app reloads it automatically.",
            "apps: the .exe names a ring is for. The ring named Default (no apps) is used for every other app; other rings with no apps are sub-rings.",
            "type keys: value like Ctrl+Shift+E (several in a row: Ctrl+K, Ctrl+C). run: value is an app, file, folder or website, args optional.",
            "type text: value is text to type. media: playpause, next, prev, stop, mute, volup, voldown. ring: value is the name of a sub-ring.",
            "type transform: opens Position X/Y, Scale, Rotation and Opacity for the selected clips in Premiere Pro or layers in After Effects.",
            "icon: an icon name, 1-3 letters, or a path to a .png file."
        };

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"_help\": [\n");
            for (int i = 0; i < Help.Length; i++)
                sb.Append("    ").Append(Q(Help[i])).Append(i < Help.Length - 1 ? ",\n" : "\n");
            sb.Append("  ],\n  \"settings\": {\n");
            sb.Append("    \"accent\": ").Append(Q(string.Format("#{0:X2}{1:X2}{2:X2}", Accent.R, Accent.G, Accent.B))).Append(",\n");
            sb.Append("    \"size\": ").Append(Size.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("    \"timeoutSeconds\": ").Append(TimeoutSeconds.ToString("0.##", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("    \"position\": ").Append(Q(Position)).Append(",\n");
            sb.Append("    \"knobPressOpensRing\": ").Append(KnobPressOpensRing ? "true" : "false").Append(",\n");
            sb.Append("    \"rememberLast\": ").Append(RememberLast ? "true" : "false").Append(",\n");
            sb.Append("    \"showLabels\": ").Append(ShowLabels ? "true" : "false").Append(",\n");
            sb.Append("    \"liquid\": ").Append((int)Math.Round(Liquid * 100)).Append(",\n");
            sb.Append("    \"ease\": ").Append((int)Math.Round(Ease * 100)).Append(",\n");
            sb.Append("    \"transformsAdded\": ").Append(TransformsAdded ? "true" : "false").Append(",\n");
            sb.Append("    \"knobKeys\": { \"press\": ").Append(KnobPressVk).Append(", \"right\": ").Append(KnobRightVk)
              .Append(", \"left\": ").Append(KnobLeftVk).Append(" }\n");
            sb.Append("  },\n  \"premiere\": {\n");
            var P = Premiere;
            sb.Append("    \"knobScrub\": ").Append(P.KnobScrub ? "true" : "false").Append(",\n");
            sb.Append("    \"wheelScrub\": ").Append(P.WheelScrub ? "true" : "false").Append(",\n");
            sb.Append("    \"invert\": ").Append(P.Invert ? "true" : "false").Append(",\n");
            sb.Append("    \"scrubFrames\": { \"slow\": ").Append(N(P.SlowFrames)).Append(", \"normal\": ").Append(N(P.NormalFrames))
              .Append(", \"fast\": ").Append(N(P.FastFrames)).Append(" },\n");
            sb.Append("    \"transformSteps\": {");
            int si = 0;
            foreach (var kv in P.Steps)
                sb.Append(si++ > 0 ? ", " : " ").Append(Q(kv.Key)).Append(": [").Append(N(kv.Value[0])).Append(", ")
                  .Append(N(kv.Value[1])).Append(", ").Append(N(kv.Value[2])).Append("]");
            sb.Append(si > 0 ? " }\n" : "}\n");
            sb.Append("  },\n  \"rings\": [\n");
            for (int r = 0; r < Rings.Count; r++)
            {
                var ring = Rings[r];
                sb.Append("    {\n      \"name\": ").Append(Q(ring.Name)).Append(",\n      \"apps\": [");
                for (int a = 0; a < ring.Apps.Count; a++) sb.Append(a > 0 ? ", " : "").Append(Q(ring.Apps[a]));
                sb.Append("],\n      \"items\": [\n");
                for (int i = 0; i < ring.Items.Count; i++)
                {
                    var it = ring.Items[i];
                    sb.Append("        { \"label\": ").Append(Q(it.Label))
                      .Append(", \"icon\": ").Append(Q(it.Icon))
                      .Append(", \"type\": ").Append(Q(it.Type))
                      .Append(", \"value\": ").Append(Q(it.Value));
                    if (!string.IsNullOrEmpty(it.Args)) sb.Append(", \"args\": ").Append(Q(it.Args));
                    sb.Append(" }").Append(i < ring.Items.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("      ]\n    }").Append(r < Rings.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        static string N(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c); else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ------------------------------------------------------------ helpers
        static object Get(IDictionary<string, object> d, string key)
        {
            foreach (var kv in d)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
        static string Str(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture); }
        static double Num(object o, double def)
        {
            if (o == null) return def;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return def; }
        }
        static bool Bool(object o, bool def) { return o is bool ? (bool)o : def; }
        static IEnumerable List(object o)
        {
            if (o is string || o == null) return new object[0];
            var e = o as IEnumerable;
            return e ?? new object[0];
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace KnobRing
{
    // Talks to the "KnobRing Bridge" extension that runs inside Premiere Pro / After Effects.
    // Knob turns are added up while a request is on its way, so fast spinning never queues up a backlog.
    internal static class Bridge
    {
        public const string ExtensionFolderName = "KnobRingBridge";
        public static readonly string[] Files = { "manifest.xml", "index.html", "main.js", "host.jsx", "panel/index.html", "panel/panel.js", "panel/icons.js" };

        public class Result { public bool Ok; public string Value = ""; public string Msg = ""; }

        public static int PortFor(string exe)
        {
            return (exe ?? "").ToLowerInvariant() == "afterfx.exe" ? 47322 : 47321;
        }

        public static bool IsSupportedApp(string exe)
        {
            exe = (exe ?? "").ToLowerInvariant();
            return exe == "adobe premiere pro.exe" || exe == "afterfx.exe";
        }

        // ------------------------------------------------------------ install
        public static string ExtensionDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "Adobe", "CEP", "extensions", ExtensionFolderName);
            }
        }

        static byte[] Res(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("missing resource " + name);
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }

        static bool WriteIfChanged(string dest, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (File.Exists(dest) && Same(File.ReadAllBytes(dest), data)) return false;
            File.WriteAllBytes(dest, data);
            return true;
        }

        // Copies the extension (the background bridge plus the "KnobRing" settings panel) into the user's CEP folder
        // and allows unsigned extensions. Returns true when something changed (the Adobe apps then need a restart).
        public static bool EnsureInstalled()
        {
            bool changed = false;
            try
            {
                foreach (var f in Files)
                {
                    string rel = f == "manifest.xml" ? Path.Combine("CSXS", f) : f.Replace('/', Path.DirectorySeparatorChar);
                    changed |= WriteIfChanged(Path.Combine(ExtensionDir, rel), Res("bridge." + f.Replace('/', '.')));
                }
                // CEP only loads unsigned extensions in debug mode. One value per CEP version.
                for (int v = 9; v <= 14; v++)
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Adobe\CSXS." + v))
                    {
                        if (k == null) continue;
                        if (Convert.ToString(k.GetValue("PlayerDebugMode")) != "1")
                        {
                            k.SetValue("PlayerDebugMode", "1", RegistryValueKind.String);
                            changed = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Bridge install: " + ex.Message); }
            return changed;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ------------------------------------------------------------ requests
        static readonly object Lock = new object();
        static double _pending;
        static string _pendingProp, _pendingOp = "nudge";
        static int _pendingPort;
        static bool _busy;
        static Action<Result> _onResult;

        // Read the current value (async). The callback runs on a worker thread.
        public static void Get(int port, string prop, Action<Result> done)
        {
            ThreadPool.QueueUserWorkItem(_ => done(Send(port, "get", prop, 0)));
        }

        // Add delta to the property of everything selected. Turns that arrive while busy are merged.
        public static void Nudge(int port, string prop, double delta, Action<Result> done, string op = "nudge")
        {
            lock (Lock)
            {
                if (_pendingProp != prop || _pendingPort != port || _pendingOp != op) { _pending = 0; }
                _pendingProp = prop;
                _pendingOp = op;
                _pendingPort = port;
                _pending += delta;
                _onResult = done;
                if (_busy) return;
                _busy = true;
            }
            ThreadPool.QueueUserWorkItem(_ => Pump());
        }

        static void Pump()
        {
            while (true)
            {
                double d; string prop, op; int port; Action<Result> cb;
                lock (Lock)
                {
                    if (Math.Abs(_pending) < 1e-9) { _busy = false; return; }
                    d = _pending; prop = _pendingProp; op = _pendingOp; port = _pendingPort; cb = _onResult;
                    _pending = 0;
                }
                var r = Send(port, op, prop, d);
                if (cb != null) { try { cb(r); } catch (Exception ex) { Log.Write(ex); } }
            }
        }

        static Result Send(int port, string op, string prop, double delta)
        {
            var res = new Result();
            try
            {
                string body = "{\"op\":\"" + op + "\",\"prop\":\"" + prop + "\",\"delta\":" +
                              delta.ToString("R", CultureInfo.InvariantCulture) + "}";
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/");
                req.Method = "POST";
                req.Proxy = null;
                req.Timeout = 2500;
                req.ReadWriteTimeout = 2500;
                req.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                string text;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    text = rd.ReadToEnd();
                var d = new JavaScriptSerializer().DeserializeObject(text) as IDictionary<string, object>;
                if (d == null) { res.Msg = "Odd reply from the bridge"; return res; }
                object o;
                res.Ok = d.TryGetValue("ok", out o) && o is bool && (bool)o;
                if (d.TryGetValue("value", out o) && o != null) res.Value = Convert.ToString(o, CultureInfo.InvariantCulture);
                if (d.TryGetValue("msg", out o) && o != null) res.Msg = Convert.ToString(o, CultureInfo.InvariantCulture);
            }
            catch (WebException)
            {
                res.Ok = false;
                res.Msg = "Not connected";
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Msg = ex.Message;
            }
            return res;
        }
    }

    // The properties the Transforms ring can change, with knob step sizes (normal / Ctrl = fine / Shift = coarse).
    public class TransformProp
    {
        public string Id, Label, Icon;
        public double Step, Fine, Coarse;

        public static readonly TransformProp[] All =
        {
            new TransformProp { Id = "posx",     Label = "Position X", Icon = "movex",    Step = 5, Fine = 1,   Coarse = 25 },
            new TransformProp { Id = "posy",     Label = "Position Y", Icon = "movey",    Step = 5, Fine = 1,   Coarse = 25 },
            new TransformProp { Id = "scale",    Label = "Scale",      Icon = "scale",    Step = 1, Fine = 0.1, Coarse = 10 },
            new TransformProp { Id = "rotation", Label = "Rotation",   Icon = "rotate",   Step = 1, Fine = 0.1, Coarse = 10 },
            new TransformProp { Id = "opacity",  Label = "Opacity",    Icon = "opacity",  Step = 1, Fine = 0.1, Coarse = 10 },
        };

        public static TransformProp Find(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }
}

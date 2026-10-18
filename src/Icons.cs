using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace KnobRing
{
    internal static class DefaultJson
    {
        static string _text;
        public static string Text
        {
            get
            {
                if (_text == null)
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("rings.default.json"))
                    using (var r = new StreamReader(s)) _text = r.ReadToEnd();
                }
                return _text;
            }
        }
    }

    // Glyph code points shared by Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10)
    internal static class Icons
    {
        public static readonly Dictionary<string, char> Map = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            {"copy",''}, {"paste",''}, {"cut",''}, {"undo",''}, {"redo",''},
            {"save",''}, {"delete",''}, {"play",''}, {"pause",''}, {"stop",''},
            {"record",''}, {"prev",''}, {"next",''}, {"volume",''}, {"mute",''},
            {"zoomin",''}, {"zoomout",''}, {"fullscreen",''}, {"crop",''},
            {"brush",''}, {"eraser",''}, {"pen",''}, {"marker",''}, {"color",''},
            {"search",''}, {"folder",''}, {"newfolder",''}, {"refresh",''},
            {"back",''}, {"forward",''}, {"up",''}, {"down",''}, {"home",''},
            {"settings",''}, {"camera",''}, {"screenshot",''}, {"photo",''},
            {"video",''}, {"music",''}, {"mic",''}, {"rename",''}, {"share",''},
            {"mail",''}, {"globe",''}, {"link",''}, {"lock",''}, {"add",''},
            {"remove",''}, {"close",''}, {"check",''}, {"view",''}, {"layers",''},
            {"filter",''}, {"sort",''}, {"flag",''}, {"tag",''}, {"calendar",''},
            {"download",''}, {"upload",''}, {"print",''}, {"brightness",''},
            {"pin",''}, {"taskview",''}, {"monitor",''}, {"lightbulb",''},
            {"trim",''}, {"rotate",''}, {"font",''}, {"selectall",''},
            {"keyboard",''}, {"mouse",''}, {"info",''}, {"star",''}, {"heart",''},
            {"emoji",''}, {"list",''}, {"grid",''}, {"document",''}, {"more",''},
            {"ring",''},
        };

        // The icons offered in the settings window's picker
        public static readonly string[] Grid =
        {
            "copy","paste","cut","undo","redo","play","pause","mute","volume","taskview","screenshot","brush",
            "eraser","pen","color","crop","rotate","zoomin","zoomout","layers","add","upload","save","delete",
            "newfolder","folder","rename","info","up","back","globe","music"
        };

        static string _family;
        public static string Family
        {
            get
            {
                if (_family == null)
                {
                    _family = "Segoe MDL2 Assets";
                    try
                    {
                        using (var fc = new System.Drawing.Text.InstalledFontCollection())
                            foreach (var f in fc.Families)
                                if (f.Name == "Segoe Fluent Icons") { _family = f.Name; break; }
                    }
                    catch { }
                }
                return _family;
            }
        }

        public static bool IsGlyph(string icon) { return !string.IsNullOrEmpty(icon) && (Map.ContainsKey(icon.Trim()) || VectorIcons.Has(icon)); }
        public static bool IsImage(string icon)
        {
            icon = (icon ?? "").Trim();
            return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
        }

        public static string NamesList()
        {
            var names = new List<string>(Map.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names.ToArray());
        }
    }
}

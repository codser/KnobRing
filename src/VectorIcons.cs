using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KnobRing
{
    // KnobRing's own minimal line icons: SVG-style paths on a 24x24 grid, drawn with round strokes.
    internal static class VectorIcons
    {
        const string Circle9 = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18";

        static readonly Dictionary<string, string> Src = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"copy", "M9 8h10a1.5 1.5 0 0 1 1.5 1.5v10A1.5 1.5 0 0 1 19 21H9a1.5 1.5 0 0 1-1.5-1.5v-10A1.5 1.5 0 0 1 9 8z M4.5 16V5A1.5 1.5 0 0 1 6 3.5h10"},
            {"paste", "M9 3.5h6v3H9z M7 5H6a1.5 1.5 0 0 0-1.5 1.5v13A1.5 1.5 0 0 0 6 21h12a1.5 1.5 0 0 0 1.5-1.5v-13A1.5 1.5 0 0 0 18 5h-1"},
            {"cut", "M6 4a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M6 15a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M8.1 7.9L20 19 M8.1 16.1L20 5"},
            {"trim", "M6 4a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M6 15a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M8.1 7.9L20 19 M8.1 16.1L20 5"},
            {"undo", "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 0 11H11"},
            {"redo", "M15 14l5-5-5-5 M20 9H9.5a5.5 5.5 0 0 0 0 11H13"},
            {"play", "M7.5 4.8v14.4a.8.8 0 0 0 1.2.7l11.3-7.2a.8.8 0 0 0 0-1.4L8.7 4.1a.8.8 0 0 0-1.2.7z"},
            {"pause", "M8 5v14 M16 5v14"},
            {"stop", "M7 6h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1z"},
            {"prev", "M18 6v12l-9-6z M6 6v12"},
            {"next", "M6 6v12l9-6z M18 6v12"},
            {"mute", "M11 5L6.5 9H3.5v6h3l4.5 4z M21.5 9.5l-5 5 M16.5 9.5l5 5"},
            {"volume", "M11 5L6.5 9H3.5v6h3l4.5 4z M15.5 9a4.2 4.2 0 0 1 0 6 M18.5 6a8.5 8.5 0 0 1 0 12"},
            {"taskview", "M4.5 4.5h5.5v6.5H4.5z M14 4.5h5.5v6.5H14z M4.5 13h5.5v6.5H4.5z M14 13h5.5v6.5H14z"},
            {"grid", "M4.5 4.5h5.5v6.5H4.5z M14 4.5h5.5v6.5H14z M4.5 13h5.5v6.5H4.5z M14 13h5.5v6.5H14z"},
            {"screenshot", "M3.5 8V6A2.5 2.5 0 0 1 6 3.5h2 M16 3.5h2A2.5 2.5 0 0 1 20.5 6v2 M20.5 16v2a2.5 2.5 0 0 1-2.5 2.5h-2 M8 20.5H6A2.5 2.5 0 0 1 3.5 18v-2 M9 12h6 M12 9v6"},
            {"selectall", "M3.5 8V6A2.5 2.5 0 0 1 6 3.5h2 M16 3.5h2A2.5 2.5 0 0 1 20.5 6v2 M20.5 16v2a2.5 2.5 0 0 1-2.5 2.5h-2 M8 20.5H6A2.5 2.5 0 0 1 3.5 18v-2 M9 9h6v6H9z"},
            {"brush", "M18.5 3l2.5 2.5-8.8 8.8-2.5-2.5z M9.7 11.8c-2.6 0-4.4 1.8-4.4 4.4 0 1.2-.8 2.3-2.3 2.8 2.6 1.6 6.4 1.4 8.3-.5 1.3-1.3 1.6-2.9 1.4-4.2"},
            {"eraser", "M8 20.5h12 M4.6 15.4l9.9-9.9a1.5 1.5 0 0 1 2.1 0l2.9 2.9a1.5 1.5 0 0 1 0 2.1l-8.9 8.9a1.5 1.5 0 0 1-1.1.5H8.4a1.5 1.5 0 0 1-1.1-.5l-2.7-2.7a.9.9 0 0 1 0-1.3z M9.8 10.2l5 5"},
            {"pen", "M4 20h4L19 9a2.1 2.1 0 0 0-3-3L5 17z M14 7l3 3"},
            {"color", "M17 3.5a2.1 2.1 0 0 1 3 3l-2.6 2.6-3-3z M14 6l4 4 M15.6 8.4L6.2 17.8 3.5 20.5 M6.2 17.8l-.7-2.9 9.3-9.3"},
            {"crop", "M6.5 2.5v13a2 2 0 0 0 2 2h13 M17.5 21.5v-13a2 2 0 0 0-2-2h-13"},
            {"rotate", "M4 12a8 8 0 1 0 2.4-5.7 M4 4v4.5h4.5"},
            {"refresh", "M20 12a8 8 0 1 1-2.4-5.7 M20 4v4.5h-4.5"},
            {"zoomin", "M10.5 3.5a7 7 0 1 0 0 14a7 7 0 1 0 0-14 M20.5 20.5l-5-5 M10.5 7.5v6 M7.5 10.5h6"},
            {"zoomout", "M10.5 3.5a7 7 0 1 0 0 14a7 7 0 1 0 0-14 M20.5 20.5l-5-5 M7.5 10.5h6"},
            {"search", "M10.5 3.5a7 7 0 1 0 0 14a7 7 0 1 0 0-14 M20.5 20.5l-5-5"},
            {"layers", "M12 3.5l8.5 4.5L12 12.5 3.5 8z M3.5 12.5L12 17l8.5-4.5 M3.5 16.5L12 21l8.5-4.5"},
            {"add", "M12 5v14 M5 12h14"},
            {"remove", "M5 12h14"},
            {"close", "M6.5 6.5l11 11 M17.5 6.5l-11 11"},
            {"check", "M5 12.5l4.5 4.5L19 7.5"},
            {"upload", "M12 15V4 M7.5 8.5L12 4l4.5 4.5 M4.5 14.5v3.5a2.5 2.5 0 0 0 2.5 2.5h10a2.5 2.5 0 0 0 2.5-2.5v-3.5"},
            {"download", "M12 4v11 M7.5 10.5L12 15l4.5-4.5 M4.5 14.5v3.5a2.5 2.5 0 0 0 2.5 2.5h10a2.5 2.5 0 0 0 2.5-2.5v-3.5"},
            {"save", "M5.5 3.5h10l4 4v11a2 2 0 0 1-2 2h-12a2 2 0 0 1-2-2v-13a2 2 0 0 1 2-2z M8 3.5v4.5h6.5V3.5 M7.5 20.5v-6h9v6"},
            {"delete", "M4 7h16 M10 11v6 M14 11v6 M6 7l1 12.5a1.5 1.5 0 0 0 1.5 1.5h7a1.5 1.5 0 0 0 1.5-1.5L18 7 M9 7V4.5A1 1 0 0 1 10 3.5h4a1 1 0 0 1 1 1V7"},
            {"folder", "M3.5 7A2 2 0 0 1 5.5 5h3.8l2 2h7.2a2 2 0 0 1 2 2v8.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"},
            {"newfolder", "M3.5 7A2 2 0 0 1 5.5 5h3.8l2 2h7.2a2 2 0 0 1 2 2v8.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z M12 10.5v5.5 M9.25 13.25h5.5"},
            {"rename", "M4 6.5h10 M4 11.5h6 M4 16.5h4 M13 20.5v-2.6l6.3-6.3a1.8 1.8 0 0 1 2.6 2.6l-6.3 6.3z"},
            {"info", Circle9 + " M12 11v5.5 M12 7.8v.01"},
            {"up", "M12 19.5v-15 M6 10.5l6-6 6 6"},
            {"down", "M12 4.5v15 M6 13.5l6 6 6-6"},
            {"back", "M19.5 12h-15 M10.5 6l-6 6 6 6"},
            {"forward", "M4.5 12h15 M13.5 6l6 6-6 6"},
            {"globe", Circle9 + " M3 12h18 M12 3c3.2 3.4 3.2 14.6 0 18 M12 3c-3.2 3.4-3.2 14.6 0 18"},
            {"music", "M9 18V5.5l11-2V16 M6.5 15.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M17.5 13.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5"},
            {"record", Circle9 + " M12 8.5a3.5 3.5 0 1 0 0 7a3.5 3.5 0 1 0 0-7"},
            {"keyboard", "M4 6h16a1.5 1.5 0 0 1 1.5 1.5v9A1.5 1.5 0 0 1 20 18H4a1.5 1.5 0 0 1-1.5-1.5v-9A1.5 1.5 0 0 1 4 6z M6.5 10h.01 M10 10h.01 M13.5 10h.01 M17 10h.01 M7.5 14h9"},
            {"star", "M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.9z"},
            {"more", "M5.5 12h.01 M12 12h.01 M18.5 12h.01"},
            {"ring", "M12 4.5h.01 M17.3 6.7h.01 M19.5 12h.01 M17.3 17.3h.01 M12 19.5h.01 M6.7 17.3h.01 M4.5 12h.01 M6.7 6.7h.01"},
            {"photo", "M5 4.5h14A1.5 1.5 0 0 1 20.5 6v12a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 18V6A1.5 1.5 0 0 1 5 4.5z M3.5 16l4.5-4.5 4 4 3-3 5.5 5.5 M15.5 7.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3"},
            {"camera", "M4.5 7.5h3l1.8-2.5h5.4l1.8 2.5h3a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V9a1.5 1.5 0 0 1 1.5-1.5z M12 9.5a3.5 3.5 0 1 0 0 7a3.5 3.5 0 1 0 0-7"},
            {"video", "M4.5 6.5h9a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 3 16V8a1.5 1.5 0 0 1 1.5-1.5z M15 10.5l5.5-3v9l-5.5-3"},
            {"mic", "M12 3.5a3 3 0 0 0-3 3v5.5a3 3 0 0 0 6 0V6.5a3 3 0 0 0-3-3z M5.5 11.5a6.5 6.5 0 0 0 13 0 M12 18v2.5"},
            {"monitor", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v8.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 14.5V6a1.5 1.5 0 0 1 1.5-1.5z M8.5 20h7 M12 16v4"},
            {"home", "M4 10.5l8-6.5 8 6.5 M6 9v10.5h12V9 M10 19.5v-5h4v5"},
            {"mail", "M4.5 5.5h15A1.5 1.5 0 0 1 21 7v10a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17V7a1.5 1.5 0 0 1 1.5-1.5z M3.5 7l8.5 6 8.5-6"},
            {"link", "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1 M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"},
            {"lock", "M6.5 10.5h11a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 19v-7a1.5 1.5 0 0 1 1.5-1.5z M8.5 10.5V7.5a3.5 3.5 0 0 1 7 0v3"},
            {"heart", "M12 19.5s-7.5-4.6-7.5-10.2A4.2 4.2 0 0 1 12 6.8a4.2 4.2 0 0 1 7.5 2.5c0 5.6-7.5 10.2-7.5 10.2z"},
            {"document", "M6.5 3.5h7l4.5 4.5v11a1.5 1.5 0 0 1-1.5 1.5h-10A1.5 1.5 0 0 1 5 19V5a1.5 1.5 0 0 1 1.5-1.5z M13.5 3.5V8H18 M8.5 13h7 M8.5 16.5h5"},
            {"list", "M9 6.5h11 M9 12h11 M9 17.5h11 M4.5 6.5h.01 M4.5 12h.01 M4.5 17.5h.01"},
            {"pin", "M12 16.5V21 M8.5 3.5h7l-1 5.5 3 3v2h-11v-2l3-3z"},
            {"font", "M5 5.5h14 M12 5.5v13 M9 18.5h6"},
            {"flag", "M5.5 21V4 M5.5 4.5h11.5l-2.2 4 2.2 4H5.5"},
            {"tag", "M3.5 11.5V4.5a1 1 0 0 1 1-1h7l8.6 8.6a1.5 1.5 0 0 1 0 2.1l-6 6a1.5 1.5 0 0 1-2.1 0z M8 8h.01"},
            {"calendar", "M5 5h14a1.5 1.5 0 0 1 1.5 1.5v12.5a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 19V6.5A1.5 1.5 0 0 1 5 5z M3.5 10h17 M8 3v4 M16 3v4"},
            {"share", "M17.5 3.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M6.5 9.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M17.5 15.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M8.7 10.8l6.6-3.6 M8.7 13.2l6.6 3.6"},
            {"brightness", "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M12 2.5v2 M12 19.5v2 M5.3 5.3l1.4 1.4 M17.3 17.3l1.4 1.4 M2.5 12h2 M19.5 12h2 M5.3 18.7l1.4-1.4 M17.3 6.7l1.4-1.4"},
            {"settings", "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6 M12 3v2.5 M12 18.5V21 M5.6 5.6l1.8 1.8 M16.6 16.6l1.8 1.8 M3 12h2.5 M18.5 12H21 M5.6 18.4l1.8-1.8 M16.6 7.4l1.8-1.8"},
            {"lightbulb", "M9.5 18h5 M10.5 21h3 M12 3a6 6 0 0 0-3.5 10.9V15.5h7v-1.6A6 6 0 0 0 12 3z"},
            {"view", "M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6"},
            {"print", "M7 8.5V3.5h10v5 M7 17H5a1.5 1.5 0 0 1-1.5-1.5V10A1.5 1.5 0 0 1 5 8.5h14a1.5 1.5 0 0 1 1.5 1.5v5.5A1.5 1.5 0 0 1 19 17h-2 M7 13.5h10v7H7z"},
            {"emoji", Circle9 + " M8.5 14a4 4 0 0 0 7 0 M9 9.5h.01 M15 9.5h.01"},
            {"mouse", "M12 3a6 6 0 0 0-6 6v6a6 6 0 0 0 12 0V9a6 6 0 0 0-6-6z M12 7v3"},
            {"filter", "M4 5h16l-6 7.5V19l-4-2v-4.5z"},
            {"sort", "M7 4v16 M3.5 16.5L7 20l3.5-3.5 M17 20V4 M13.5 7.5L17 4l3.5 3.5"},
            {"transform", "M6.5 6.5h11v11h-11z M4.5 4.5h3v3h-3z M16.5 4.5h3v3h-3z M4.5 16.5h3v3h-3z M16.5 16.5h3v3h-3z"},
            {"movex", "M3.5 12h17 M7 8.5L3.5 12 7 15.5 M17 8.5l3.5 3.5-3.5 3.5"},
            {"movey", "M12 3.5v17 M8.5 7L12 3.5 15.5 7 M8.5 17l3.5 3.5 3.5-3.5"},
            {"scale", "M4 13v7h7 M4 20l6.5-6.5 M13 4h7v7 M20 4l-6.5 6.5"},
            {"opacity", Circle9 + " M12 3v18 M12 7h4.6 M12 12h6 M12 17h4.6"},
        };

        static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>(StringComparer.OrdinalIgnoreCase);

        public static bool Has(string name) { return !string.IsNullOrEmpty(name) && Src.ContainsKey(name.Trim()); }

        // Draws the icon centred in `box`, `size` pixels square.
        public static bool Draw(Graphics g, string name, float cx, float cy, float size, Color color, float stroke = 1.75f)
        {
            GraphicsPath p = Get(name);
            if (p == null) return false;
            float s = size / 24f;
            using (var m = new Matrix())
            using (var copy = (GraphicsPath)p.Clone())
            using (var pen = new Pen(color, stroke * s))
            {
                m.Translate(cx - size / 2f, cy - size / 2f);
                m.Scale(s, s);
                copy.Transform(m);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                var oldSmooth = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, copy);
                g.SmoothingMode = oldSmooth;
            }
            return true;
        }

        static GraphicsPath Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Trim();
            GraphicsPath p;
            if (Cache.TryGetValue(name, out p)) return p;
            string d;
            if (!Src.TryGetValue(name, out d)) return null;
            try { p = Parse(d); } catch (Exception ex) { Log.Write("Icon " + name + ": " + ex.Message); p = null; }
            Cache[name] = p;
            return p;
        }

        // ---- a small SVG path-data parser (M L H V C S Q A Z, absolute and relative)
        static readonly Regex Tok = new Regex(@"[MmLlHhVvCcSsQqAaZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?");

        static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath(FillMode.Winding);
            var toks = new List<string>();
            foreach (Match m in Tok.Matches(d)) toks.Add(m.Value);
            int i = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            char lastCmd = ' ';
            Func<float> num = () => float.Parse(toks[i++], CultureInfo.InvariantCulture);

            while (i < toks.Count)
            {
                if (char.IsLetter(toks[i][0])) cmd = toks[i++][0];
                bool rel = char.IsLower(cmd);
                char C = char.ToUpperInvariant(cmd);
                switch (C)
                {
                    case 'M':
                    {
                        float x = num(), y = num();
                        cur = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.StartFigure();
                        start = cur;
                        cmd = rel ? 'l' : 'L';   // further pairs are lines
                        lastCmd = 'M';
                        continue;
                    }
                    case 'L':
                    {
                        float x = num(), y = num();
                        var p = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.AddLine(cur, p); cur = p; break;
                    }
                    case 'H':
                    {
                        float x = num();
                        var p = new PointF(rel ? cur.X + x : x, cur.Y);
                        path.AddLine(cur, p); cur = p; break;
                    }
                    case 'V':
                    {
                        float y = num();
                        var p = new PointF(cur.X, rel ? cur.Y + y : y);
                        path.AddLine(cur, p); cur = p; break;
                    }
                    case 'C':
                    {
                        var c1 = Pt(num(), num(), cur, rel); var c2 = Pt(num(), num(), cur, rel); var p = Pt(num(), num(), cur, rel);
                        path.AddBezier(cur, c1, c2, p); lastCtrl = c2; cur = p; break;
                    }
                    case 'S':
                    {
                        var c1 = (lastCmd == 'C' || lastCmd == 'S') ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                        var c2 = Pt(num(), num(), cur, rel); var p = Pt(num(), num(), cur, rel);
                        path.AddBezier(cur, c1, c2, p); lastCtrl = c2; cur = p; break;
                    }
                    case 'Q':
                    {
                        var q = Pt(num(), num(), cur, rel); var p = Pt(num(), num(), cur, rel);
                        var c1 = new PointF(cur.X + 2f / 3f * (q.X - cur.X), cur.Y + 2f / 3f * (q.Y - cur.Y));
                        var c2 = new PointF(p.X + 2f / 3f * (q.X - p.X), p.Y + 2f / 3f * (q.Y - p.Y));
                        path.AddBezier(cur, c1, c2, p); cur = p; break;
                    }
                    case 'A':
                    {
                        float rx = num(), ry = num(), rot = num(), large = num(), sweep = num();
                        var p = Pt(num(), num(), cur, rel);
                        AddArc(path, cur, p, rx, ry, rot, large != 0, sweep != 0);
                        cur = p; break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        cur = start;
                        lastCmd = 'Z';
                        continue;
                    default:
                        i++; continue;
                }
                lastCmd = C;
            }
            return path;
        }

        static PointF Pt(float x, float y, PointF cur, bool rel) { return rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y); }

        // SVG elliptical arc -> cubic Béziers
        static void AddArc(GraphicsPath path, PointF p0, PointF p1, float rx, float ry, float rotDeg, bool large, bool sweep)
        {
            if (rx == 0 || ry == 0 || (p0.X == p1.X && p0.Y == p1.Y)) { path.AddLine(p0, p1); return; }
            double phi = rotDeg * Math.PI / 180, cosP = Math.Cos(phi), sinP = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 = cosP * dx + sinP * dy, y1 = -sinP * dx + cosP * dy;
            double Rx = Math.Abs(rx), Ry = Math.Abs(ry);
            double lam = (x1 * x1) / (Rx * Rx) + (y1 * y1) / (Ry * Ry);
            if (lam > 1) { double s = Math.Sqrt(lam); Rx *= s; Ry *= s; }
            double num = Rx * Rx * Ry * Ry - Rx * Rx * y1 * y1 - Ry * Ry * x1 * x1;
            double den = Rx * Rx * y1 * y1 + Ry * Ry * x1 * x1;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp = coef * Rx * y1 / Ry, cyp = -coef * Ry * x1 / Rx;
            double cx = cosP * cxp - sinP * cyp + (p0.X + p1.X) / 2.0;
            double cy = sinP * cxp + cosP * cyp + (p0.Y + p1.Y) / 2.0;
            Func<double, double, double, double, double> ang = (ux, uy, vx, vy) =>
            {
                double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
                return a;
            };
            double t1 = ang(1, 0, (x1 - cxp) / Rx, (y1 - cyp) / Ry);
            double dt = ang((x1 - cxp) / Rx, (y1 - cyp) / Ry, (-x1 - cxp) / Rx, (-y1 - cyp) / Ry);
            if (!sweep && dt > 0) dt -= 2 * Math.PI;
            else if (sweep && dt < 0) dt += 2 * Math.PI;
            int segs = (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2));
            double step = dt / segs;
            double k = 4.0 / 3.0 * Math.Tan(step / 4);
            var prev = p0;
            for (int s = 0; s < segs; s++)
            {
                double a1 = t1 + s * step, a2 = a1 + step;
                double c1x = Math.Cos(a1) - k * Math.Sin(a1), c1y = Math.Sin(a1) + k * Math.Cos(a1);
                double c2x = Math.Cos(a2) + k * Math.Sin(a2), c2y = Math.Sin(a2) - k * Math.Cos(a2);
                Func<double, double, PointF> map = (ux, uy) => new PointF(
                    (float)(cx + Rx * ux * cosP - Ry * uy * sinP),
                    (float)(cy + Rx * ux * sinP + Ry * uy * cosP));
                var e = map(Math.Cos(a2), Math.Sin(a2));
                path.AddBezier(prev, map(c1x, c1y), map(c2x, c2y), s == segs - 1 ? p1 : e);
                prev = s == segs - 1 ? p1 : e;
            }
        }
    }
}

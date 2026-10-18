using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KnobRing
{
    public class Chord
    {
        public List<ushort> Mods = new List<ushort>();
        public ushort Key;
        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var m in Mods) parts.Add("0x" + m.ToString("X2"));
            parts.Add("0x" + Key.ToString("X2"));
            return string.Join("+", parts.ToArray());
        }
    }

    internal static class Actions
    {
        public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

        static readonly Dictionary<string, ushort> Named = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            {"space",0x20},{"enter",0x0D},{"return",0x0D},{"tab",0x09},{"esc",0x1B},{"escape",0x1B},
            {"backspace",0x08},{"delete",0x2E},{"del",0x2E},{"insert",0x2D},{"ins",0x2D},{"home",0x24},{"end",0x23},
            {"pgup",0x21},{"pageup",0x21},{"pgdn",0x22},{"pagedown",0x22},{"left",0x25},{"up",0x26},{"right",0x27},
            {"down",0x28},{"printscreen",0x2C},{"prtsc",0x2C},{"capslock",0x14},{"menu",0x5D},{"apps",0x5D},
            {"plus",0xBB},{"minus",0xBD},{"comma",0xBC},{"period",0xBE},{"dot",0xBE},
            {"num0",0x60},{"num1",0x61},{"num2",0x62},{"num3",0x63},{"num4",0x64},{"num5",0x65},{"num6",0x66},
            {"num7",0x67},{"num8",0x68},{"num9",0x69},{"nummultiply",0x6A},{"numplus",0x6B},{"numminus",0x6D},
            {"numdot",0x6E},{"numdivide",0x6F},
            {"mute",0xAD},{"voldown",0xAE},{"volup",0xAF},{"next",0xB0},{"prev",0xB1},{"stop",0xB2},{"playpause",0xB3},
        };

        static readonly HashSet<ushort> Extended = new HashSet<ushort>
        { 0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x2D,0x2E,0x5B,0x5C,0x5D,0x6F,0xAD,0xAE,0xAF,0xB0,0xB1,0xB2,0xB3 };

        // "Ctrl+Shift+E", "Space", "Alt+]", "Ctrl+K, Ctrl+C"
        public static List<Chord> Parse(string spec)
        {
            var result = new List<Chord>();
            if (string.IsNullOrEmpty(spec)) return result;
            foreach (var rawChord in spec.Split(','))
            {
                var s = rawChord.Trim();
                if (s.Length == 0) continue;
                var tokens = new List<string>();
                if (s.EndsWith("++")) { tokens.AddRange(s.Substring(0, s.Length - 2).Split('+')); tokens.Add("+"); }
                else if (s == "+") tokens.Add("+");
                else tokens.AddRange(s.Split('+'));

                var c = new Chord();
                for (int i = 0; i < tokens.Count; i++)
                {
                    var t = tokens[i].Trim();
                    if (t.Length == 0) continue;
                    bool last = i == tokens.Count - 1;
                    string tl = t.ToLowerInvariant();
                    if (!last && (tl == "ctrl" || tl == "control")) { AddMod(c, VK_CONTROL); continue; }
                    if (!last && tl == "shift") { AddMod(c, VK_SHIFT); continue; }
                    if (!last && (tl == "alt" || tl == "option")) { AddMod(c, VK_MENU); continue; }
                    if (!last && (tl == "win" || tl == "windows" || tl == "super")) { AddMod(c, VK_LWIN); continue; }
                    if (!last) throw new FormatException("Unknown modifier '" + t + "' in '" + s + "'");
                    c.Key = KeyFor(t, c);
                }
                if (c.Key == 0)
                {
                    // a lone modifier, e.g. "Win"
                    if (c.Mods.Count > 0) { c.Key = c.Mods[c.Mods.Count - 1]; c.Mods.RemoveAt(c.Mods.Count - 1); }
                    else throw new FormatException("No key in '" + s + "'");
                }
                result.Add(c);
            }
            return result;
        }

        static void AddMod(Chord c, ushort vk) { if (!c.Mods.Contains(vk)) c.Mods.Add(vk); }

        static ushort KeyFor(string t, Chord c)
        {
            ushort vk;
            if (Named.TryGetValue(t, out vk)) return vk;
            string tl = t.ToLowerInvariant();
            if (tl == "ctrl" || tl == "control") return VK_CONTROL;
            if (tl == "shift") return VK_SHIFT;
            if (tl == "alt") return VK_MENU;
            if (tl == "win" || tl == "windows") return VK_LWIN;
            if (tl.Length >= 2 && tl[0] == 'f')
            {
                int n;
                if (int.TryParse(tl.Substring(1), out n) && n >= 1 && n <= 24) return (ushort)(0x70 + n - 1);
            }
            if (t.Length == 1)
            {
                char ch = t[0];
                if (ch >= 'a' && ch <= 'z') return (ushort)char.ToUpperInvariant(ch);
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) return ch;
                short scan = -1;
                try { scan = Native.VkKeyScan(ch); } catch (Exception) { scan = Fallback(ch); }
                if (scan == -1) throw new FormatException("Can't type '" + ch + "' on this keyboard layout");
                int shiftState = (scan >> 8) & 0xFF;
                if ((shiftState & 1) != 0) AddMod(c, VK_SHIFT);
                if ((shiftState & 2) != 0) AddMod(c, VK_CONTROL);
                if ((shiftState & 4) != 0) AddMod(c, VK_MENU);
                return (ushort)(scan & 0xFF);
            }
            throw new FormatException("Unknown key '" + t + "'");
        }

        // US-layout fallback, only used when user32 isn't available (testing off Windows)
        static short Fallback(char ch)
        {
            switch (ch)
            {
                case '[': return 0xDB; case ']': return 0xDD; case '\\': return 0xDC; case ';': return 0xBA;
                case '\'': return 0xDE; case ',': return 0xBC; case '.': return 0xBE; case '/': return 0xBF;
                case '-': return 0xBD; case '=': return 0xBB; case '`': return 0xC0; case '+': return 0x1BB;
                default: return -1;
            }
        }

        // -------------------------------------------------------------- sending
        public static void SendChords(List<Chord> chords)
        {
            foreach (var c in chords)
            {
                var inputs = new List<Native.INPUT>();
                foreach (var m in c.Mods) inputs.Add(Key(m, false));
                inputs.Add(Key(c.Key, false));
                inputs.Add(Key(c.Key, true));
                for (int i = c.Mods.Count - 1; i >= 0; i--) inputs.Add(Key(c.Mods[i], true));
                Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
                if (chords.Count > 1) System.Threading.Thread.Sleep(25);
            }
        }

        static Native.INPUT Key(ushort vk, bool up)
        {
            var i = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            i.u.ki.wVk = vk;
            i.u.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            i.u.ki.dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (Extended.Contains(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0);
            return i;
        }

        public static void TypeText(string text)
        {
            var inputs = new List<Native.INPUT>();
            foreach (char ch in text.Replace("\r\n", "\n"))
            {
                if (ch == '\n') { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); continue; }
                foreach (bool up in new[] { false, true })
                {
                    var i = new Native.INPUT { type = Native.INPUT_KEYBOARD };
                    i.u.ki.wScan = ch;
                    i.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
                    inputs.Add(i);
                }
            }
            if (inputs.Count > 0)
                Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void Media(string what)
        {
            ushort vk;
            if (!Named.TryGetValue((what ?? "").Replace(" ", "").Replace("/", ""), out vk) || vk < 0xAD)
                throw new FormatException("Unknown media action '" + what + "'");
            SendChords(new List<Chord> { new Chord { Key = vk } });
        }

        public static void Run(string target, string args)
        {
            var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target),
                                           Environment.ExpandEnvironmentVariables(args ?? ""));
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        // Runs an item; returns the name of a sub-ring to open, or null.
        public static string Execute(RingItem it)
        {
            switch (it.Type)
            {
                case "keys": SendChords(Parse(it.Value)); return null;
                case "text": TypeText(it.Value); return null;
                case "media": Media(it.Value); return null;
                case "run": Run(it.Value, it.Args); return null;
                case "ring": return it.Value;
                default: throw new FormatException("Unknown action type '" + it.Type + "'");
            }
        }
    }
}

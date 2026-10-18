using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KnobRing
{
    // Draws the ring into a transparent bitmap. Kept separate from the window so it can be previewed anywhere.
    internal sealed class RingRenderer : IDisposable
    {
        // geometry at 96 DPI, size 1.0
        const float BaseRadius = 116f, SlotR = 31f, HubR = 50f, Pad = 18f, SelScale = 1.17f;

        public List<RingItem> Items = new List<RingItem>();
        public string Title = "";
        public Color Accent = Color.FromArgb(47, 107, 255);
        public float Scale = 1f;            // dpi * size
        public int Selected = 0;
        public bool HubHover = false;
        public bool ShowLabels = true;
        public float Liquid = 0.35f;        // 0 = solid glide, 1 = very gooey (shape)
        public float EaseAmount = 0.25f;          // 0 = stiff, lands exactly; 1 = soft with a little overshoot (motion)

        // animation state
        public float Open = 1f;             // 1 = fully shown; falls to 0 while the ring closes
        public float IntroT = 99f;          // seconds since the ring appeared (bounce-in); large = already in

        const float HubIn = 0.30f, SlotDelay0 = 0.10f, SlotStagger = 0.045f, SlotIn = 0.42f;

        // seconds until every part has bounced in
        public float IntroDuration
        {
            get { return SlotDelay0 + (Items.Count / 2) * SlotStagger + SlotIn; }
        }

        // ease with an overshoot: goes past 1 and settles back, like a soft bounce
        static float BackOut(float t, float k = 2.1f)
        {
            t = Math.Max(0, Math.Min(1, t));
            float u = t - 1;
            return 1 + (k + 1) * u * u * u + k * u * u;
        }

        float SlotProgress(int i)
        {
            int n = Math.Max(1, Items.Count);
            int d = Math.Min(i, n - i);                     // spreads down both sides from the top
            return Math.Max(0, Math.Min(1, (IntroT - SlotDelay0 - d * SlotStagger) / SlotIn));
        }
        float[] _slotScale = new float[0];

        // the liquid highlight: a head that springs to the selected slot and a tail that trails behind it
        float _head = float.NaN, _headV, _tail, _tailV, _target, _wob = 1f, _wobV, _blobA = 1f;
        int _lastSel = -1;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        double _lastT;
        const float TwoPi = (float)(Math.PI * 2);

        readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public float Radius
        {
            get
            {
                int n = Math.Max(Items.Count, 1);
                float needed = n > 1 ? (SlotR + 7f) / (float)Math.Sin(Math.PI / n) : 0f;
                return Math.Max(BaseRadius, needed);
            }
        }

        public int PixelSize { get { return (int)Math.Ceiling(2 * (Radius + SlotR * SelScale + Pad) * Scale); } }

        public void ResetAnimation()
        {
            _slotScale = new float[Items.Count];
            for (int i = 0; i < _slotScale.Length; i++) _slotScale[i] = i == Selected ? SelScale : 1f;
            _head = _tail = _target = SlotAngle(Selected);
            _headV = _tailV = _wobV = 0;
            _wob = 1f;
            _lastT = _clock.Elapsed.TotalSeconds;
            _blobA = HubHover ? 0f : 1f;
            _lastSel = Selected;
        }

        // Spring settings (angular frequency, damping ratio). Head: eased with a slight overshoot.
        // Tail: slower and fully damped, so the liquid stretches out and then flows back into the head.
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        float L { get { return Math.Max(0f, Math.Min(1f, Liquid)); } }

        // Advances the animations to "now". Returns true while anything is still moving.
        public bool Step()
        {
            if (_slotScale.Length != Items.Count || float.IsNaN(_head)) ResetAnimation();
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, Math.Max(0.0, now - _lastT));
            _lastT = now;

            bool moving = false;
            float slotLerp = 1f - (float)Math.Exp(-dt * 22f);
            for (int i = 0; i < _slotScale.Length; i++)
            {
                float target = i == Selected && !HubHover ? SelScale : 1f;
                float d = target - _slotScale[i];
                if (Math.Abs(d) > 0.002f) { _slotScale[i] += d * slotLerp; moving = true; }
                else _slotScale[i] = target;
            }

            if (Selected != _lastSel)
            {
                // go the short way round, keeping the angle continuous
                _target += (float)Math.IEEERemainder(SlotAngle(Selected) - _target, TwoPi);
                _lastSel = Selected;
                _wobV -= 1.6f * L;              // the blob squeezes as it sets off
            }

            // integrate in small fixed steps so the motion is the same at any frame rate
            float E = Math.Max(0f, Math.Min(1f, EaseAmount));
            // up to the middle of the slider the head never overshoots (damping >= 1)
            float HeadW = Lerp(32f, 10f, E), HeadZ = E <= 0.5f ? 1.0f : Lerp(1.0f, 0.72f, (E - 0.5f) * 2f);
            float TailW = Lerp(45f, 7.5f, L), TailZ = 1.0f;
            float WobW = 17f, WobZ = Lerp(0.6f, 0.3f, L);
            const float h = 1f / 480f;
            for (float t = 0; t < dt; t += h)
            {
                float step = Math.Min(h, dt - t);
                float gap = _head - _tail;
                _headV += (HeadW * HeadW * (_target - _head) - 2f * HeadZ * HeadW * _headV) * step;
                _head += _headV * step;
                float tailBefore = _tailV;
                _tailV += (TailW * TailW * (_head - _tail) - 2f * TailZ * TailW * _tailV) * step;
                _tail += _tailV * step;
                // when the trailing drop flows back in, the head swells and jiggles
                float merge = Math.Abs(gap) - Math.Abs(_head - _tail);
                if (merge > 0) _wobV += merge * 9f * L;
                _wobV += (WobW * WobW * (1f - _wob) - 2f * WobZ * WobW * _wobV) * step;
                _wob += _wobV * step;
            }
            float aTarget = HubHover ? 0f : 1f;
            _blobA += (aTarget - _blobA) * (1f - (float)Math.Exp(-dt * 18f));

            if (Math.Abs(_target - _head) > 0.001f || Math.Abs(_headV) > 0.01f || Math.Abs(_head - _tail) > 0.001f ||
                Math.Abs(_tailV) > 0.01f || Math.Abs(1f - _wob) > 0.002f || Math.Abs(_wobV) > 0.02f || Math.Abs(aTarget - _blobA) > 0.01f)
                moving = true;
            else
            {
                float norm = (float)Math.IEEERemainder(_target, TwoPi);   // keep the angle small after many turns
                _head = _tail = _target = norm;
                _headV = _tailV = _wobV = 0; _wob = 1f; _blobA = aTarget;
            }
            return moving;
        }

        public float SlotAngle(int i)
        {
            int n = Math.Max(Items.Count, 1);
            return (float)(-Math.PI / 2 + i * 2 * Math.PI / n);
        }

        // Which slot (or -1 = hub, -2 = nothing) is at a point in window pixels
        public int HitTest(float px, float py)
        {
            float c = PixelSize / 2f;
            float dx = (px - c) / Scale, dy = (py - c) / Scale;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist <= HubR) return -1;
            float R = Radius;
            for (int i = 0; i < Items.Count; i++)
            {
                float a = SlotAngle(i);
                float sx = (float)Math.Cos(a) * R, sy = (float)Math.Sin(a) * R;
                float ddx = dx - sx, ddy = dy - sy;
                if (ddx * ddx + ddy * ddy <= (SlotR * SelScale) * (SlotR * SelScale)) return i;
            }
            // anywhere in the band between hub and slots counts as the nearest slot
            if (dist > HubR && dist < R + SlotR && Items.Count > 0)
            {
                double ang = Math.Atan2(dy, dx) + Math.PI / 2;
                if (ang < 0) ang += Math.PI * 2;
                int n = Items.Count;
                return (int)Math.Round(ang / (2 * Math.PI / n)) % n;
            }
            return -2;
        }

        // Which slot lies in the direction (dx, dy) from the ring's centre, in pixels. -1 = inside the hub.
        public int SlotAtOffset(float dx, float dy)
        {
            float x = dx / Scale, y = dy / Scale;
            if (x * x + y * y <= HubR * HubR || Items.Count == 0) return -1;
            double ang = Math.Atan2(y, x) + Math.PI / 2;
            if (ang < 0) ang += Math.PI * 2;
            int n = Items.Count;
            return (int)Math.Round(ang / (2 * Math.PI / n)) % n;
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static float Ease(float t) { t = Math.Max(0, Math.Min(1, t)); return 1 - (float)Math.Pow(1 - t, 3); }

        public Bitmap Render()
        {
            int size = PixelSize;
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                float c = size / 2f;
                float open = Ease(Open);
                g.TranslateTransform(c, c);
                float s = Scale * (0.9f + 0.1f * open);
                g.ScaleTransform(s, s);

                int n = Items.Count;
                float R = Radius;
                if (_slotScale.Length != n) ResetAnimation();

                if (float.IsNaN(_head)) ResetAnimation();
                int selI = Math.Max(0, Math.Min(n - 1, Selected));
                float selP = n > 0 ? SlotProgress(selI) : 1f;
                float ringR = R * (0.72f + 0.28f * Ease(selP));
                var pos = new PointF[n];
                var rad = new float[n];
                var alphas = new int[n];

                // slots: white discs, staggered pop-out from the centre
                for (int i = 0; i < n; i++)
                {
                    float p = SlotProgress(i);
                    float local = Math.Min(1f, p * 3f) * open;          // fade in quickly, fade out on close
                    float a = SlotAngle(i);
                    float rr = R * (0.72f + 0.28f * Ease(p));
                    pos[i] = new PointF((float)Math.Cos(a) * rr, (float)Math.Sin(a) * rr);
                    rad[i] = SlotR * _slotScale[i] * BackOut(p);
                    alphas[i] = (int)(255 * local);
                    if (local <= 0.001f || rad[i] <= 0.5f) continue;
                    float x = pos[i].X, y = pos[i].Y, sr = rad[i];
                    DrawShadow(g, x, y, sr, alphas[i]);
                    using (var b = new SolidBrush(Color.FromArgb(alphas[i], 248, 248, 250)))
                        g.FillEllipse(b, x - sr, y - sr, sr * 2, sr * 2);
                    using (var edge = new Pen(Color.FromArgb((int)(28 * local), 0, 0, 0), 1f))
                        g.DrawEllipse(edge, x - sr, y - sr, sr * 2, sr * 2);
                }

                // the liquid highlight
                float headR = SlotR * SelScale * _wob * BackOut(selP) + 0.8f;
                int blobAlpha = (int)(255 * open * _blobA * Math.Min(1f, selP * 3f));
                if (n > 0 && blobAlpha > 2)
                {
                    float hx = (float)Math.Cos(_head) * ringR, hy = (float)Math.Sin(_head) * ringR;
                    // soft glow around the head
                    for (int k = 3; k >= 1; k--)
                        using (var gb = new SolidBrush(Color.FromArgb((int)(blobAlpha / 255f * (20 + (3 - k) * 16)), Accent)))
                            g.FillEllipse(gb, hx - headR - k * 3.5f, hy - headR - k * 3.5f, (headR + k * 3.5f) * 2, (headR + k * 3.5f) * 2);

                    // body: circles along the arc from the trailing drop to the head, filled as one shape.
                    // The longer it stretches, the thinner the neck in the middle gets.
                    using (var path = new GraphicsPath(FillMode.Winding))
                    {
                        float span = _head - _tail;
                        float len = Math.Abs(span) * ringR;
                        int steps = Math.Max(1, (int)Math.Ceiling(len / 1.5f));
                        float tailR = headR * Lerp(0.9f, 0.6f, L);
                        float pinch = Math.Min(0.58f * L, len / (headR * 4.2f));
                        float speedPx = Math.Abs(_headV) * ringR;
                        float stretch = 1f + Math.Min(0.4f * L, speedPx / 1400f * L);
                        for (int k = 0; k <= steps; k++)
                        {
                            float f = (float)k / steps;                 // 0 = trailing drop, 1 = head
                            float ang = _tail + span * f;
                            float sm = f * f * (3 - 2 * f);
                            float neck = 1f - pinch * (float)Math.Pow(Math.Sin(Math.PI * Math.Min(1f, f * 1.08f)), 1.6);
                            float r = (tailR + (headR - tailR) * sm) * neck;
                            if (k == 0) r = Math.Max(r, tailR * (1f - pinch * 0.35f));
                            float cx = (float)Math.Cos(ang) * ringR, cy = (float)Math.Sin(ang) * ringR;
                            if (k == steps && stretch > 1.01f)
                            {
                                // the head stretches along the direction of travel while it moves fast
                                using (var e = new GraphicsPath())
                                using (var m = new Matrix())
                                {
                                    float sq = 1f / (float)Math.Sqrt(stretch);
                                    e.AddEllipse(-r * stretch, -r * sq, r * 2 * stretch, r * 2 * sq);
                                    m.Translate(cx, cy);
                                    m.Rotate((float)(ang * 180 / Math.PI) + 90);
                                    e.Transform(m);
                                    path.AddPath(e, false);
                                }
                            }
                            else
                                path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        }
                        using (var b = new SolidBrush(Color.FromArgb(blobAlpha, Accent)))
                            g.FillPath(b, path);
                    }
                    using (var rp = new Pen(Color.FromArgb((int)(blobAlpha * Math.Max(0f, 1f - Math.Abs(_head - _tail) * 3f)), 255, 255, 255), 2.2f))
                        g.DrawEllipse(rp, hx - headR - 4f, hy - headR - 4f, (headR + 4f) * 2, (headR + 4f) * 2);
                }

                // icons on top: white where the liquid covers them
                for (int i = 0; i < n; i++)
                {
                    if (alphas[i] <= 2) continue;
                    float cover = 0f;
                    if (n > 0 && blobAlpha > 2)
                    {
                        float lo = Math.Min(_tail, _head), hi = Math.Max(_tail, _head);
                        float a = SlotAngle(i);
                        // nearest copy of this slot's angle to the blob
                        float a2 = lo + (float)Math.IEEERemainder(a - lo, TwoPi);
                        float dAng = a2 < lo ? lo - a2 : a2 > hi ? a2 - hi : 0f;
                        float dist = dAng * ringR;
                        float headDist = Math.Abs((float)Math.IEEERemainder(a - _head, TwoPi)) * ringR;
                        float covR = headR * 0.85f;
                        cover = Math.Max(0f, Math.Min(1f, 1f - Math.Min(dist * 1.6f, headDist) / covR));
                        cover *= blobAlpha / 255f;
                    }
                    var fg = Mix(Color.FromArgb(28, 28, 32), Color.White, cover);
                    DrawIcon(g, Items[i], pos[i].X, pos[i].Y, rad[i], Color.FromArgb(alphas[i], fg), alphas[i]);
                }

                // hub: bounces in first
                float hubP = Math.Max(0, Math.Min(1, IntroT / HubIn));
                float hubA = Math.Min(1f, hubP * 3f) * open;
                float hubS = BackOut(hubP, 2.4f);
                int ha = (int)(240 * hubA);
                var hubState = g.Save();
                g.ScaleTransform(Math.Max(0.001f, hubS), Math.Max(0.001f, hubS));
                DrawShadow(g, 0, 0, HubR, (int)(255 * hubA));
                using (var b = new SolidBrush(Color.FromArgb(ha, 24, 24, 28)))
                    g.FillEllipse(b, -HubR, -HubR, HubR * 2, HubR * 2);

                // pointer follows the liquid head
                if (n > 0 && _blobA > 0.05f)
                {
                    float pa = _head;
                    int pA = (int)(255 * hubA * _blobA * Math.Min(1f, selP * 3f));
                    var tip = new PointF((float)Math.Cos(pa) * (HubR + 7), (float)Math.Sin(pa) * (HubR + 7));
                    float side = 0.17f;
                    var l = new PointF((float)Math.Cos(pa - side) * (HubR - 1), (float)Math.Sin(pa - side) * (HubR - 1));
                    var r = new PointF((float)Math.Cos(pa + side) * (HubR - 1), (float)Math.Sin(pa + side) * (HubR - 1));
                    using (var b = new SolidBrush(Color.FromArgb(pA, Accent)))
                        g.FillPolygon(b, new[] { l, tip, r });
                    float sweep = 44 + Math.Abs(_head - _tail) * 180f / (float)Math.PI;
                    float mid = (_head + _tail) / 2f;
                    using (var p = new Pen(Color.FromArgb(pA, Accent), 2.2f))
                        g.DrawArc(p, -HubR + 3, -HubR + 3, (HubR - 3) * 2, (HubR - 3) * 2,
                            (float)(mid * 180 / Math.PI) - sweep / 2, sweep);
                }

                // hub text
                string label = HubHover ? "Close" : !ShowLabels ? "" : (n > 0 && Selected >= 0 && Selected < n ? Items[Selected].Label : "");
                using (var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var labelFont = new Font("Segoe UI Semibold", label.Length > 14 ? 11.5f : 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                using (var tb = new SolidBrush(Color.FromArgb((int)(150 * hubA), 255, 255, 255)))
                using (var lb = new SolidBrush(Color.FromArgb((int)(255 * hubA), 255, 255, 255)))
                {
                    string t = (Title ?? "").ToUpperInvariant();
                    if (t.Length > 16) t = t.Substring(0, 15) + "…";
                    g.DrawString(t, titleFont, tb, new RectangleF(-HubR + 8, -HubR * 0.62f, HubR * 2 - 16, 14), sf);
                    g.DrawString(label, labelFont, lb, new RectangleF(-HubR + 9, -10, HubR * 2 - 18, 34), sf);
                }
                g.Restore(hubState);
            }
            return bmp;
        }

        static void DrawShadow(Graphics g, float x, float y, float r, int alpha)
        {
            for (int k = 6; k >= 1; k--)
            {
                float rr = r + k * 1.6f;
                int a = (int)(alpha / 255f * (5 + (6 - k) * 3));
                using (var b = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                    g.FillEllipse(b, x - rr, y - rr + 2.5f, rr * 2, rr * 2);
            }
        }

        void DrawIcon(Graphics g, RingItem it, float x, float y, float r, Color fg, int alpha)
        {
            string icon = (it.Icon ?? "").Trim();
            if (icon.Length == 0) icon = it.Type == "ring" ? "more" : (it.Label.Length > 0 ? it.Label.Substring(0, 1).ToUpperInvariant() : "?");
            if (it.Type == "__back") icon = "back";

            if (VectorIcons.Draw(g, icon, x, y, r * 0.95f, fg, 1.7f)) return;
            char glyph;
            if (Icons.Map.TryGetValue(icon, out glyph))
            {
                using (var f = new Font(Icons.Family, r * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(fg))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph.ToString(), f, b, new RectangleF(x - r, y - r + 1, r * 2, r * 2), sf);
                return;
            }

            if (icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var img = LoadImage(icon);
                if (img != null)
                {
                    float w = r * 1.15f;
                    var ia = new System.Drawing.Imaging.ImageAttributes();
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha / 255f };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(img, new Rectangle((int)(x - w / 2), (int)(y - w / 2), (int)w, (int)w),
                        0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                    return;
                }
                icon = it.Label.Length > 0 ? it.Label.Substring(0, 1).ToUpperInvariant() : "?";
            }

            if (icon.Length > 3) icon = icon.Substring(0, 3);
            float fs = icon.Length == 1 ? r * 0.82f : icon.Length == 2 ? r * 0.64f : r * 0.5f;
            using (var f = new Font(Th.HeadFamilyName, fs, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(fg))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(icon, f, b, new RectangleF(x - r, y - r, r * 2, r * 2), sf);
        }

        Image LoadImage(string path)
        {
            Image img;
            if (_images.TryGetValue(path, out img)) return img;
            try
            {
                string full = Environment.ExpandEnvironmentVariables(path);
                if (!System.IO.Path.IsPathRooted(full)) full = System.IO.Path.Combine(AppConfig.Folder, full);
                using (var tmp = Image.FromFile(full)) img = new Bitmap(tmp);
            }
            catch { img = null; }
            _images[path] = img;
            return img;
        }

        public void Dispose()
        {
            foreach (var i in _images.Values) if (i != null) i.Dispose();
            _images.Clear();
        }
    }
}

// The V.T.T? main screen: a glowing orb with a live voice spectrum in one of several styles and colour
// palettes, the status line under it, party mode, and pop-up toasts (badge unlocks).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace VTT
{
    // Colours for the orb's states.
    class Palette
    {
        public string Name;
        public Color Off, Live, Paused, Record;

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static readonly Palette[] All = {
            new Palette { Name = "Classic", Off = Theme.Off, Live = Theme.Live, Paused = Theme.Paused, Record = Theme.Record },
            new Palette { Name = "Neon", Off = Hex(0xFF3D8B), Live = Hex(0x2CF5E0), Paused = Hex(0x5A4F8C), Record = Hex(0xFFE14D) },
            new Palette { Name = "Pastel", Off = Hex(0xF29BB0), Live = Hex(0x93DDBA), Paused = Hex(0x9AA3B5), Record = Hex(0xF6D293) },
            new Palette { Name = "Stealth", Off = Hex(0x4C505A), Live = Hex(0xDDE0E5), Paused = Hex(0x2F323A), Record = Hex(0x8A8F99) },
        };

        public static Palette Find(string name) { return All.FirstOrDefault(p => p.Name == name) ?? All[0]; }
    }

    class OrbView : SmoothControl
    {
        public static readonly string[] Styles = { "Bars", "Wave", "Particles", "Blob" };

        public Color TargetColor = Theme.Off;
        public string Status = "", Hint = "", Footer = "", Glyph = Theme.GlyphMicOff, Style = "Bars";
        public bool Paused, Frozen; // Frozen: fully covered by the settings page, so skip repainting
        public Bitmap Overlay;
        public float Slide;
        public event EventHandler OrbClick, SettingsClick, PauseClick;

        class Particle { public float X, Y, Vx, Vy, Life, MaxLife, Size, Spin, Angle; public Color Color; public bool Confetti; }

        const int Bars = SpectrumAnalyzer.Bands * 2;
        float cr, cg, cb;
        readonly float[] bands = new float[SpectrumAnalyzer.Bands];
        float level, time, hover, intro, ripple = 1, party, toastT = -1;
        bool partyOn;
        string toast = "";
        string shownStatus = "", prevStatus = "", shownHint = "", prevHint = "";
        float statusFade = 1, hintFade = 1;
        Point mouse = new Point(-1000, -1000);
        readonly List<Particle> particles = new List<Particle>();
        readonly Random rng = new Random();
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public OrbView()
        {
            cr = TargetColor.R; cg = TargetColor.G; cb = TargetColor.B;
        }

        public float Level { get { return level; } }
        public bool Party { get { return partyOn; } }
        float S { get { return Width / 400f; } }
        PointF Center { get { return new PointF(Width / 2f, Height * 0.40f); } }
        float BaseRadius { get { return 62 * S; } }
        RectangleF PauseRect { get { return new RectangleF(10 * S, Height - 58 * S, 44 * S, 44 * S); } }
        RectangleF GearRect { get { return new RectangleF(Width - 54 * S, Height - 58 * S, 44 * S, 44 * S); } }

        Font F(string family, float px)
        {
            string key = family + px.ToString("0.0");
            Font f;
            if (!fonts.TryGetValue(key, out f)) { f = new Font(family, px, GraphicsUnit.Pixel); fonts[key] = f; }
            return f;
        }

        bool OverOrb(Point p)
        {
            float dx = p.X - Center.X, dy = p.Y - Center.Y;
            return dx * dx + dy * dy < Math.Pow(BaseRadius + 14 * S, 2);
        }

        public void SetParty(bool on)
        {
            if (on == partyOn) return;
            partyOn = on;
            if (on) Confetti(160);
        }

        public void ShowToast(string text) { toast = text; toastT = 0; }

        static Color Hue(double h, double sat = 0.75, double val = 1)
        {
            h = ((h % 1) + 1) % 1 * 6;
            int i = (int)h;
            double f = h - i, p = val * (1 - sat), q = val * (1 - sat * f), t = val * (1 - sat * (1 - f));
            double r, g, b;
            switch (i) { case 0: r = val; g = t; b = p; break; case 1: r = q; g = val; b = p; break; case 2: r = p; g = val; b = t; break;
                         case 3: r = p; g = q; b = val; break; case 4: r = t; g = p; b = val; break; default: r = val; g = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        void Confetti(int count)
        {
            float s = S;
            for (int i = 0; i < count && particles.Count < 700; i++)
            {
                particles.Add(new Particle {
                    X = (float)rng.NextDouble() * Width, Y = -10 * s - (float)rng.NextDouble() * Height * 0.5f,
                    Vx = ((float)rng.NextDouble() - 0.5f) * 60 * s, Vy = (60 + (float)rng.NextDouble() * 90) * s,
                    Life = 0, MaxLife = 3.5f + (float)rng.NextDouble() * 2, Size = (4 + (float)rng.NextDouble() * 4) * s,
                    Spin = ((float)rng.NextDouble() - 0.5f) * 12, Angle = (float)rng.NextDouble() * 6,
                    Color = Hue(rng.NextDouble(), 0.7, 1), Confetti = true });
            }
        }

        public void Animate(float dt, float[] rawBands, float rawLevel)
        {
            time += dt;
            var target = TargetColor;
            cr = Theme.Approach(cr, target.R, 7, dt);
            cg = Theme.Approach(cg, target.G, 7, dt);
            cb = Theme.Approach(cb, target.B, 7, dt);
            float damp = Paused ? 0 : 1;
            for (int i = 0; i < bands.Length; i++)
            {
                float t = rawBands[i] * damp;
                bands[i] = Theme.Approach(bands[i], t, t > bands[i] ? 28 : 7, dt);
            }
            level = Theme.Approach(level, rawLevel * damp, rawLevel > level ? 20 : 5, dt);
            hover = Theme.Approach(hover, OverOrb(mouse) && Slide == 0 ? 1 : 0, 12, dt);
            party = Theme.Approach(party, partyOn ? 1 : 0, 4, dt);
            intro = Math.Min(1, intro + dt * 1.5f);
            if (ripple < 1) ripple = Math.Min(1, ripple + dt * 1.6f);
            if (toastT >= 0) { toastT += dt; if (toastT > 4) toastT = -1; }
            if (Status != shownStatus) { prevStatus = shownStatus; shownStatus = Status; statusFade = 0; }
            if (Hint != shownHint) { prevHint = shownHint; shownHint = Hint; hintFade = 0; }
            statusFade = Math.Min(1, statusFade + dt * 4);
            hintFade = Math.Min(1, hintFade + dt * 3);
            UpdateParticles(dt);
            if (!Frozen) Invalidate();
        }

        void UpdateParticles(float dt)
        {
            float s = S;
            var ctr = Center;
            // Sparks fly off the orb from wherever your voice is loudest.
            if (Style == "Particles" && !Paused)
            {
                for (int i = 0; i < Bars; i++)
                {
                    int band = i < Bars / 2 ? i : Bars - 1 - i;
                    float v = bands[band];
                    if (rng.NextDouble() > v * v * 1.6 * dt * 60 + 0.002) continue;
                    double a = -Math.PI / 2 + (i + rng.NextDouble()) * 2 * Math.PI / Bars;
                    float speed = (50 + v * 170 + (float)rng.NextDouble() * 30) * s, r0 = BaseRadius + 8 * s;
                    particles.Add(new Particle { X = ctr.X + (float)Math.Cos(a) * r0, Y = ctr.Y + (float)Math.Sin(a) * r0,
                        Vx = (float)Math.Cos(a) * speed, Vy = (float)Math.Sin(a) * speed, MaxLife = 0.7f + (float)rng.NextDouble() * 0.7f,
                        Size = (1.6f + v * 2.6f) * s, Color = Color.Empty });
                }
            }
            if (partyOn && rng.NextDouble() < dt * 6) Confetti(2);
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Life += dt;
                if (p.Life >= p.MaxLife || p.Y > Height + 20) { particles.RemoveAt(i); continue; }
                if (p.Confetti) { p.Vx += (float)Math.Sin(time * 3 + p.Angle) * 30 * s * dt; p.Angle += p.Spin * dt; }
                else { p.Vx *= 1 - dt * 1.2f; p.Vy *= 1 - dt * 1.2f; }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
            }
            while (particles.Count > 700) particles.RemoveAt(0);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            bool hot = Slide == 0 && (OverOrb(mouse) || PauseRect.Contains(mouse) || GearRect.Contains(mouse));
            Cursor = hot ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { mouse = new Point(-1000, -1000); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (Slide != 0 || e.Button != MouseButtons.Left) return;
            if (OverOrb(e.Location)) { ripple = 0; if (OrbClick != null) OrbClick(this, EventArgs.Empty); }
            else if (PauseRect.Contains(e.Location)) { if (PauseClick != null) PauseClick(this, EventArgs.Empty); }
            else if (GearRect.Contains(e.Location)) { if (SettingsClick != null) SettingsClick(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Theme.Bg);
            float eased = Theme.EaseInOut(Slide);
            if (eased < 1)
            {
                var state = g.Save();
                g.TranslateTransform(-eased * Width * 0.3f, 0);
                DrawMain(g, 1 - eased);
                g.Restore(state);
            }
            if (Overlay != null && eased > 0)
            {
                float x = (1 - eased) * Width;
                using (var shadow = new LinearGradientBrush(new RectangleF(x - 30, 0, 30, Height), Color.Transparent, Color.FromArgb(90, 0, 0, 0), 0f))
                    g.FillRectangle(shadow, x - 29, 0, 30, Height);
                g.DrawImageUnscaled(Overlay, (int)x, 0);
            }
        }

        // Colour of the orb right now; in party mode it cycles through the rainbow.
        Color OrbColor(double offset)
        {
            var c = Color.FromArgb((int)cr, (int)cg, (int)cb);
            return party < 0.01f ? c : Theme.Lerp(c, Hue(time * 0.22 + offset, 0.7, 1), party);
        }

        void DrawMain(Graphics g, float fade)
        {
            float s = S;
            var c = OrbColor(0);
            var ctr = Center;
            float introScale = 0.8f + 0.2f * Theme.EaseOut(intro), introAlpha = Theme.EaseOut(intro) * fade;
            float beat = party > 0.01f ? (float)Math.Pow(Math.Max(0, Math.Sin(time * Math.PI * 2 * 2)), 8) * 6 * s * party : 0; // 120 bpm bounce
            float r = (BaseRadius + (float)Math.Sin(time * 2.1) * 1.6f * s + level * 10 * s + hover * 4 * s + beat) * introScale;

            // Soft glow that swells with your voice.
            float glowR = r * (2.3f + level * 0.5f + party * 0.3f);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ctr.X - glowR, ctr.Y - glowR, glowR * 2, glowR * 2);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Theme.Alpha(c, (38 + 80 * level + 40 * party) * introAlpha);
                    glow.SurroundColors = new[] { Theme.Alpha(c, 0) };
                    g.FillPath(glow, path);
                }
            }

            if (ripple < 1)
            {
                float rr = r + Theme.EaseOut(ripple) * 90 * s;
                using (var pen = new Pen(Theme.Alpha(c, (1 - ripple) * 170 * fade), 2 * s)) g.DrawEllipse(pen, ctr.X - rr, ctr.Y - rr, rr * 2, rr * 2);
            }

            switch (Style)
            {
                case "Wave": DrawWave(g, ctr, r, introAlpha); break;
                case "Particles": DrawSparks(g, introAlpha); break;
                case "Blob": break; // the orb itself moves
                default: DrawBars(g, ctr, r, introAlpha); break;
            }

            // The orb: a circle, or for Blob a shape that bulges wherever your voice is loudest.
            using (var orbPath = new GraphicsPath())
            {
                if (Style == "Blob") orbPath.AddClosedCurve(BlobPoints(ctr, r), 0.5f);
                else orbPath.AddEllipse(ctr.X - r, ctr.Y - r, r * 2, r * 2);
                var bounds = orbPath.GetBounds();
                using (var fill = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2),
                    Theme.Alpha(Theme.Lerp(c, Color.White, 0.16f), 255 * introAlpha), Theme.Alpha(Theme.Lerp(OrbColor(0.3), Color.Black, 0.28f), 255 * introAlpha), 90f))
                    g.FillPath(fill, orbPath);
                using (var rim = new Pen(Theme.Alpha(Color.White, 34 * introAlpha), 1.2f * s)) g.DrawPath(rim, orbPath);
            }

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(Theme.Alpha(Color.White, 240 * introAlpha)))
                g.DrawString(Glyph, F(Theme.IconFontName, 30 * s), b, new RectangleF(ctr.X - r, ctr.Y - r + 1 * s, r * 2, r * 2), center);

            // Status crossfades and drifts upward when it changes.
            float sy = ctr.Y + 150 * s;
            float sf = Theme.EaseOut(statusFade);
            using (var b = new SolidBrush(Theme.Alpha(Theme.Text, 255 * (1 - sf) * fade)))
                g.DrawString(prevStatus, F("Segoe UI Semibold", 24 * s), b, new RectangleF(0, sy - 40 * s - 10 * s * sf, Width, 80 * s), center);
            using (var b = new SolidBrush(Theme.Alpha(Theme.Text, 255 * sf * fade)))
                g.DrawString(shownStatus, F("Segoe UI Semibold", 24 * s), b, new RectangleF(0, sy - 40 * s + 10 * s * (1 - sf), Width, 80 * s), center);

            var wrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
            var hintRect = new RectangleF(30 * s, sy + 26 * s, Width - 60 * s, 60 * s);
            float hf = Theme.EaseOut(hintFade);
            using (var b = new SolidBrush(Theme.Alpha(Theme.TextDim, 255 * (1 - hf) * fade))) g.DrawString(prevHint, F("Segoe UI", 13.5f * s), b, hintRect, wrap);
            using (var b = new SolidBrush(Theme.Alpha(Theme.TextDim, 255 * hf * fade))) g.DrawString(shownHint, F("Segoe UI", 13.5f * s), b, hintRect, wrap);

            DrawConfetti(g, fade);

            // Bottom bar.
            DrawIcon(g, Paused ? Theme.GlyphPlay : Theme.GlyphPause, PauseRect, fade);
            DrawIcon(g, Theme.GlyphSettings, GearRect, fade);
            using (var b = new SolidBrush(Theme.Alpha(Theme.TextFaint, 255 * fade)))
                g.DrawString(partyOn ? "♪  party mode  ♪" : Footer, F("Segoe UI", 12 * s), b, new RectangleF(60 * s, PauseRect.Y, Width - 120 * s, PauseRect.Height), center);

            DrawToast(g, fade);
        }

        float BandAt(int i)
        {
            int band = i < Bars / 2 ? i : Bars - 1 - i;
            return bands[band];
        }

        // Classic: a ring of bars, low frequencies at the top, mirrored left and right.
        void DrawBars(Graphics g, PointF ctr, float r, float alpha)
        {
            float s = S;
            using (var pen = new Pen(Color.White, 3.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < Bars; i++)
                {
                    float v = BandAt(i);
                    double angle = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / Bars;
                    float r0 = r + 12 * s, r1 = r0 + (2 + v * 62 + level * 6) * s;
                    pen.Color = Theme.Alpha(Theme.Lerp(OrbColor(i / (double)Bars), Color.White, v * 0.35f), (45 + 210 * v) * alpha);
                    float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                    g.DrawLine(pen, ctr.X + cos * r0, ctr.Y + sin * r0, ctr.X + cos * r1, ctr.Y + sin * r1);
                }
            }
        }

        // Wave: two smooth rings that ripple outward with your voice and slowly flow around the orb.
        void DrawWave(Graphics g, PointF ctr, float r, float alpha)
        {
            float s = S;
            for (int layer = 0; layer < 2; layer++)
            {
                var pts = new PointF[Bars];
                for (int i = 0; i < Bars; i++)
                {
                    double angle = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / Bars;
                    float flow = (float)Math.Sin(angle * 3 + time * (layer == 0 ? 1.4 : -1.0)) * (2 + level * 6) * s;
                    float rr = r + (14 + layer * 10) * s + BandAt(i) * (layer == 0 ? 52 : 34) * s + flow;
                    pts[i] = new PointF(ctr.X + (float)Math.Cos(angle) * rr, ctr.Y + (float)Math.Sin(angle) * rr);
                }
                var c = OrbColor(layer * 0.25);
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(pts, 0.5f);
                    using (var b = new SolidBrush(Theme.Alpha(c, (layer == 0 ? 40 : 22) * alpha))) g.FillPath(b, path);
                    using (var pen = new Pen(Theme.Alpha(Theme.Lerp(c, Color.White, 0.2f), (layer == 0 ? 210 : 110) * alpha), (layer == 0 ? 2.4f : 1.4f) * s)) g.DrawPath(pen, path);
                }
            }
        }

        void DrawSparks(Graphics g, float alpha)
        {
            foreach (var p in particles)
            {
                if (p.Confetti) continue;
                float life = 1 - p.Life / p.MaxLife;
                var c = OrbColor(p.X / Math.Max(1, Width));
                using (var b = new SolidBrush(Theme.Alpha(Theme.Lerp(c, Color.White, life * 0.4f), 230 * life * alpha)))
                    g.FillEllipse(b, p.X - p.Size, p.Y - p.Size, p.Size * 2, p.Size * 2);
            }
        }

        PointF[] BlobPoints(PointF ctr, float r)
        {
            var pts = new PointF[Bars];
            for (int i = 0; i < Bars; i++)
            {
                double angle = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / Bars;
                float wobble = (float)(Math.Sin(angle * 3 + time * 1.7) + Math.Sin(angle * 5 - time * 1.1)) * 1.6f * S;
                float rr = r * (1 + BandAt(i) * 0.32f) + wobble;
                pts[i] = new PointF(ctr.X + (float)Math.Cos(angle) * rr, ctr.Y + (float)Math.Sin(angle) * rr);
            }
            return pts;
        }

        void DrawConfetti(Graphics g, float fade)
        {
            foreach (var p in particles)
            {
                if (!p.Confetti) continue;
                float life = Math.Min(1, (p.MaxLife - p.Life) * 2);
                var state = g.Save();
                g.TranslateTransform(p.X, p.Y);
                g.RotateTransform(p.Angle * 57.3f);
                g.ScaleTransform(1, 0.45f + 0.55f * (float)Math.Abs(Math.Sin(p.Angle * 2))); // flutter
                using (var b = new SolidBrush(Theme.Alpha(p.Color, 235 * life * fade))) g.FillRectangle(b, -p.Size / 2, -p.Size / 4, p.Size, p.Size / 2);
                g.Restore(state);
            }
        }

        // A pill that drops in from the top, stays a few seconds and slides away.
        void DrawToast(Graphics g, float fade)
        {
            if (toastT < 0 || toast == "") return;
            float s = S;
            float t = toastT < 0.4f ? Theme.EaseOut(toastT / 0.4f) : toastT > 3.5f ? 1 - Theme.EaseInOut((toastT - 3.5f) / 0.5f) : 1;
            var font = F("Segoe UI Semibold", 13.5f * s);
            var size = g.MeasureString(toast, font);
            float w = size.Width + 52 * s, h = 38 * s, x = (Width - w) / 2, y = -h + t * (h + 18 * s);
            var rect = new RectangleF(x, y, w, h);
            using (var path = Theme.RoundRect(rect, h / 2))
            {
                using (var b = new SolidBrush(Theme.Alpha(Theme.Surface, 245 * fade))) g.FillPath(b, path);
                using (var pen = new Pen(Theme.Alpha(Theme.Record, 200 * fade), 1.4f * s)) g.DrawPath(pen, path);
            }
            using (var b = new SolidBrush(Theme.Alpha(Theme.Record, 255 * fade)))
                g.DrawString("★", F("Segoe UI Symbol", 15 * s), b, new RectangleF(x + 12 * s, y, 24 * s, h), new StringFormat { LineAlignment = StringAlignment.Center });
            using (var b = new SolidBrush(Theme.Alpha(Theme.Text, 255 * fade)))
                g.DrawString(toast, font, b, new RectangleF(x + 36 * s, y, w - 44 * s, h), new StringFormat { LineAlignment = StringAlignment.Center });
        }

        void DrawIcon(Graphics g, string glyph, RectangleF rect, float fade)
        {
            bool hot = rect.Contains(mouse) && Slide == 0;
            if (hot)
                using (var b = new SolidBrush(Theme.Alpha(Theme.Surface, 255 * fade))) g.FillEllipse(b, rect);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(Theme.Alpha(hot ? Theme.Text : Theme.TextDim, 255 * fade)))
                g.DrawString(glyph, F(Theme.IconFontName, 17 * S), b, new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height), fmt);
        }
    }
}

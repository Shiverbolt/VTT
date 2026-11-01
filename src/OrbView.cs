// The V.T.T? main screen: a glowing orb ringed by a live voice spectrum, with the status line under it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VTT
{
    // The main screen: a glowing orb ringed by a live voice spectrum, the status line under it,
    // and the pause / settings icons at the bottom. Also draws the settings page sliding in.
    class OrbView : SmoothControl
    {
        public Color TargetColor = Theme.Off;
        public string Status = "", Hint = "", Footer = "", Glyph = Theme.GlyphMicOff;
        public bool Paused, Frozen; // Frozen: fully covered by the settings page, so skip repainting
        public Bitmap Overlay;
        public float Slide;
        public event EventHandler OrbClick, SettingsClick, PauseClick;

        const int Bars = SpectrumAnalyzer.Bands * 2;
        float cr, cg, cb;
        readonly float[] bands = new float[SpectrumAnalyzer.Bands];
        float level, time, hover, intro, ripple = 1;
        string shownStatus = "", prevStatus = "", shownHint = "", prevHint = "";
        float statusFade = 1, hintFade = 1;
        Point mouse = new Point(-1000, -1000);
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public OrbView()
        {
            cr = TargetColor.R; cg = TargetColor.G; cb = TargetColor.B;
        }

        public float Level { get { return level; } }
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

        public void Animate(float dt, float[] rawBands, float rawLevel)
        {
            time += dt;
            cr = Theme.Approach(cr, TargetColor.R, 7, dt);
            cg = Theme.Approach(cg, TargetColor.G, 7, dt);
            cb = Theme.Approach(cb, TargetColor.B, 7, dt);
            float damp = Paused ? 0 : 1;
            for (int i = 0; i < bands.Length; i++)
            {
                float target = rawBands[i] * damp;
                bands[i] = Theme.Approach(bands[i], target, target > bands[i] ? 28 : 7, dt);
            }
            level = Theme.Approach(level, rawLevel * damp, rawLevel > level ? 20 : 5, dt);
            hover = Theme.Approach(hover, OverOrb(mouse) && Slide == 0 ? 1 : 0, 12, dt);
            intro = Math.Min(1, intro + dt * 1.5f);
            if (ripple < 1) ripple = Math.Min(1, ripple + dt * 1.6f);
            if (Status != shownStatus) { prevStatus = shownStatus; shownStatus = Status; statusFade = 0; }
            if (Hint != shownHint) { prevHint = shownHint; shownHint = Hint; hintFade = 0; }
            statusFade = Math.Min(1, statusFade + dt * 4);
            hintFade = Math.Min(1, hintFade + dt * 3);
            if (!Frozen) Invalidate();
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

        void DrawMain(Graphics g, float fade)
        {
            float s = S;
            var c = Color.FromArgb((int)cr, (int)cg, (int)cb);
            var ctr = Center;
            float introScale = 0.8f + 0.2f * Theme.EaseOut(intro), introAlpha = Theme.EaseOut(intro) * fade;
            float r = (BaseRadius + (float)Math.Sin(time * 2.1) * 1.6f * s + level * 10 * s + hover * 4 * s) * introScale;

            // Soft glow that swells with your voice.
            float glowR = r * (2.3f + level * 0.5f);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ctr.X - glowR, ctr.Y - glowR, glowR * 2, glowR * 2);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Theme.Alpha(c, (38 + 80 * level) * introAlpha);
                    glow.SurroundColors = new[] { Theme.Alpha(c, 0) };
                    g.FillPath(glow, path);
                }
            }

            if (ripple < 1)
            {
                float rr = r + Theme.EaseOut(ripple) * 90 * s;
                using (var pen = new Pen(Theme.Alpha(c, (1 - ripple) * 170 * fade), 2 * s)) g.DrawEllipse(pen, ctr.X - rr, ctr.Y - rr, rr * 2, rr * 2);
            }

            // Voice spectrum: low frequencies at the top, mirrored left and right.
            using (var pen = new Pen(c, 3.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < Bars; i++)
                {
                    int half = Bars / 2;
                    int band = i < half ? i : Bars - 1 - i;
                    float v = bands[band];
                    double angle = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / Bars;
                    float r0 = r + 12 * s, r1 = r0 + (2 + v * 62 + level * 6) * s;
                    pen.Color = Theme.Alpha(Theme.Lerp(c, Color.White, v * 0.35f), (45 + 210 * v) * introAlpha);
                    float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                    g.DrawLine(pen, ctr.X + cos * r0, ctr.Y + sin * r0, ctr.X + cos * r1, ctr.Y + sin * r1);
                }
            }

            var orb = new RectangleF(ctr.X - r, ctr.Y - r, r * 2, r * 2);
            using (var fill = new LinearGradientBrush(orb, Theme.Alpha(Theme.Lerp(c, Color.White, 0.16f), 255 * introAlpha),
                Theme.Alpha(Theme.Lerp(c, Color.Black, 0.28f), 255 * introAlpha), 90f))
                g.FillEllipse(fill, orb);
            using (var rim = new Pen(Theme.Alpha(Color.White, 34 * introAlpha), 1.2f * s)) g.DrawEllipse(rim, orb);

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(Theme.Alpha(Color.White, 240 * introAlpha)))
                g.DrawString(Glyph, F(Theme.IconFontName, 30 * s), b, new RectangleF(orb.X, orb.Y + 1 * s, orb.Width, orb.Height), center);

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

            // Bottom bar.
            DrawIcon(g, Paused ? Theme.GlyphPlay : Theme.GlyphPause, PauseRect, fade);
            DrawIcon(g, Theme.GlyphSettings, GearRect, fade);
            using (var b = new SolidBrush(Theme.Alpha(Theme.TextFaint, 255 * fade)))
                g.DrawString(Footer, F("Segoe UI", 12 * s), b, new RectangleF(60 * s, PauseRect.Y, Width - 120 * s, PauseRect.Height), center);
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

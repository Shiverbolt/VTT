// Look and feel of V.T.T?: colours, dark window chrome, and the custom-drawn controls
// (the animated orb with its voice spectrum, toggle switches, steppers and level meters).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VTT
{
    static class Theme
    {
        public static readonly Color Bg = Hex(0x0E0F13), Surface = Hex(0x1A1C23), SurfaceHover = Hex(0x242731), Border = Hex(0x2C2F3A);
        public static readonly Color Text = Hex(0xECEDEF), TextDim = Hex(0x8A8F99), TextFaint = Hex(0x5A5F6A);
        public static readonly Color Off = Hex(0xE5484D), Live = Hex(0x30A46C), Paused = Hex(0x5F6470), Record = Hex(0xF5A524), Accent = Hex(0x8B9CFF);

        public const string GlyphMic = "", GlyphMicOff = "", GlyphPause = "", GlyphPlay = "",
            GlyphSettings = "", GlyphBack = "", GlyphDelete = "";

        public static readonly string IconFontName = DetectIconFont();
        public static readonly Font ChevronFont = new Font(IconFontName, 7.5f);

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        static string DetectIconFont()
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                using (var f = new Font(name, 10)) if (f.Name == name) return name;
            return "Segoe UI Symbol";
        }

        public static Font Ui(float pt, bool semibold = false) { return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", pt); }
        public static Font Icons(float pt) { return new Font(IconFontName, pt); }

        public static Color Alpha(Color c, double a) { return Color.FromArgb((int)Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static float EaseInOut(float t) { return t < 0.5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2; }
        public static float EaseOut(float t) { return 1 - (float)Math.Pow(1 - t, 3); }

        // Moves a value toward a target at a frame-rate independent speed.
        public static float Approach(float value, float target, float speed, float dt) { return value + (target - value) * (1 - (float)Math.Exp(-speed * dt)); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void DarkTitleBar(Form form)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref on, 4);                   // immersive dark mode
                int caption = Bg.R | (Bg.G << 8) | (Bg.B << 16);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);              // caption colour (Windows 11)
            }
            catch { }
        }

        public static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static Button FlatButton(string text, bool icon = false)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Text, Cursor = Cursors.Hand,
                Font = icon ? Icons(10) : Ui(9.5f), UseVisualStyleBackColor = false, TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = SurfaceHover;
            b.FlatAppearance.MouseDownBackColor = Border;
            return b;
        }

        public static ComboBox Combo()
        {
            var c = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Text,
                DrawMode = DrawMode.OwnerDrawFixed, Font = Ui(9.5f), ItemHeight = 22 };
            c.DrawItem += (s, e) => {
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var bg = new SolidBrush(selected ? SurfaceHover : Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, c.Items[e.Index].ToString(), c.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                        Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            c.HandleCreated += delegate { try { SetWindowTheme(c.Handle, "DarkMode_CFD", null); } catch { } };
            return c;
        }

        // A borderless text box sitting in a padded surface-coloured panel.
        public static Panel Field(TextBox box, int width)
        {
            var p = new Panel { BackColor = Surface, Size = new Size(width, 32), Padding = new Padding(10, 7, 10, 4), Margin = new Padding(0, 0, 0, 10) };
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Surface;
            box.ForeColor = Text;
            box.Font = Ui(9.5f);
            box.Dock = DockStyle.Fill;
            p.Controls.Add(box);
            return p;
        }

        public static Label Caption(string text, Color? color = null, float pt = 9.5f)
        {
            return new Label { Text = text, ForeColor = color ?? Text, Font = Ui(pt), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
        }
    }

    // Drop-down whose frame and arrow are repainted to match the dark theme.
    class DarkCombo : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F) return; // WM_PAINT
            using (var g = Graphics.FromHwnd(Handle))
            {
                var r = ClientRectangle;
                int arrowW = SystemInformation.VerticalScrollBarWidth + 4;
                using (var pen = new Pen(Theme.Surface, 2)) g.DrawRectangle(pen, 1, 1, r.Width - 2, r.Height - 2);
                var arrow = new Rectangle(r.Width - arrowW - 1, 2, arrowW - 1, r.Height - 4);
                using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, arrow);
                TextRenderer.DrawText(g, "", Theme.ChevronFont, arrow, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    class SmoothControl : Control
    {
        public SmoothControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Bg;
        }
    }

    // Animated on/off switch.
    class Toggle : SmoothControl
    {
        bool isChecked;
        float knob, hover;
        public event EventHandler CheckedChanged;

        public Toggle() { Size = new Size(40, 22); Cursor = Cursors.Hand; }

        public bool Checked
        {
            get { return isChecked; }
            set { if (value == isChecked) return; isChecked = value; if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public void SnapToState() { knob = isChecked ? 1 : 0; Invalidate(); }

        public void Animate(float dt)
        {
            float k = Theme.Approach(knob, isChecked ? 1 : 0, 16, dt);
            float h = Theme.Approach(hover, ClientRectangle.Contains(PointToClient(MousePosition)) ? 1 : 0, 14, dt);
            if (Math.Abs(k - knob) > 0.001f || Math.Abs(h - hover) > 0.001f) { knob = k; hover = h; Invalidate(); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var track = new RectangleF(1, 1, Width - 2, Height - 2);
            var color = Theme.Lerp(Theme.Lerp(Theme.Border, Theme.SurfaceHover, hover * 0.6f), Theme.Live, knob);
            using (var path = Theme.RoundRect(track, track.Height / 2))
            using (var b = new SolidBrush(color)) g.FillPath(b, path);
            float d = track.Height - 6;
            float x = track.X + 3 + (track.Width - d - 6) * Theme.EaseInOut(knob);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, track.Y + 3, d, d);
        }
    }

    // Minimal "-  value  +" number picker.
    class Stepper : SmoothControl
    {
        public double Value, Min, Max, Step = 1;
        public string Format = "0", Suffix = "", ZeroText;
        public event EventHandler ValueChanged;
        int hoverSide;

        public Stepper() { Size = new Size(110, 32); Cursor = Cursors.Hand; Font = Theme.Ui(9.5f); }

        public void Set(double v) { Value = Math.Max(Min, Math.Min(Max, Math.Round(v / Step) * Step)); Invalidate(); }

        int Side(Point p) { return p.X < Height ? -1 : p.X > Width - Height ? 1 : 0; }

        protected override void OnMouseMove(MouseEventArgs e) { int s = Side(e.Location); if (s != hoverSide) { hoverSide = s; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hoverSide = 0; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int s = Side(e.Location);
            if (s == 0) return;
            double old = Value;
            Set(Value + s * Step);
            if (Value != old && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 6))
            using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
            for (int side = -1; side <= 1; side += 2)
            {
                var r = side < 0 ? new Rectangle(0, 0, Height, Height) : new Rectangle(Width - Height, 0, Height, Height);
                bool enabled = side < 0 ? Value > Min : Value < Max;
                var color = !enabled ? Theme.TextFaint : hoverSide == side ? Theme.Text : Theme.TextDim;
                TextRenderer.DrawText(g, side < 0 ? "−" : "+", Theme.Ui(11), r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            string text = Value == 0 && ZeroText != null ? ZeroText : Value.ToString(Format) + Suffix;
            TextRenderer.DrawText(g, text, Font, new Rectangle(Height, 0, Width - 2 * Height, Height), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Thin live microphone level bar.
    class LevelMeter : SmoothControl
    {
        float value;
        public LevelMeter() { Size = new Size(100, 6); }
        public float Value { set { if (Math.Abs(value - this.value) > 0.004f) { this.value = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, r.Height / 2))
            using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
            if (value <= 0.01f) return;
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Math.Max(r.Height, r.Width * value), r.Height), r.Height / 2))
            using (var b = new SolidBrush(Theme.Lerp(Theme.Live, Theme.Record, Math.Max(0, value - 0.75f) * 4))) g.FillPath(b, path);
        }
    }

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

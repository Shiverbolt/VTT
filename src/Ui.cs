// Look and feel of V.T.T? (shared by the app and its installer): colours, dark window chrome,
// themed dialogs and the custom-drawn controls (toggle switches, steppers, level meters).
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

    // A dark message box with any number of buttons.
    static class ThemedDialog
    {
        // Returns the index of the clicked button, or -1 if the dialog was closed. The default button is
        // highlighted and answers Enter; a danger button is drawn in red and is never the default.
        public static int Show(IWin32Window owner, string title, string message, string[] buttons, int defaultButton = 0, int dangerButton = -1)
        {
            const int width = 420, pad = 24;
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = form.MinimizeBox = false;
                form.ShowInTaskbar = owner == null;
                form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                form.BackColor = Theme.Bg;
                form.ForeColor = Theme.Text;
                form.Font = Theme.Ui(9.5f);
                form.AutoScaleDimensions = new SizeF(96F, 96F);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.HandleCreated += delegate { Theme.DarkTitleBar(form); };

                var heading = new Label { Text = title, Font = Theme.Ui(13, true), ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(pad, 20, width - 2 * pad, 30) };
                var body = new Label { Text = message, ForeColor = Theme.TextDim, AutoSize = false, Font = Theme.Ui(9.5f) };
                int bodyHeight = TextRenderer.MeasureText(message, body.Font, new Size(width - 2 * pad, 0), TextFormatFlags.WordBreak).Height + 4;
                body.Bounds = new Rectangle(pad, 56, width - 2 * pad, bodyHeight);
                form.Controls.Add(heading);
                form.Controls.Add(body);

                int result = -1, x = width - pad, y = 56 + bodyHeight + 20;
                for (int i = buttons.Length - 1; i >= 0; i--)
                {
                    int index = i;
                    var b = Theme.FlatButton(buttons[i]);
                    int w = Math.Max(84, TextRenderer.MeasureText(buttons[i], b.Font).Width + 28);
                    x -= w;
                    b.Bounds = new Rectangle(x, y, w, 34);
                    x -= 8;
                    if (i == dangerButton) b.ForeColor = Theme.Off;
                    else if (i == defaultButton)
                    {
                        b.BackColor = Theme.Accent;
                        b.ForeColor = Theme.Bg;
                        b.FlatAppearance.MouseOverBackColor = Theme.Lerp(Theme.Accent, Color.White, 0.15f);
                        b.FlatAppearance.MouseDownBackColor = Theme.Lerp(Theme.Accent, Color.Black, 0.15f);
                    }
                    b.Click += delegate { result = index; form.Close(); };
                    form.Controls.Add(b);
                    if (i == defaultButton && i != dangerButton) { form.AcceptButton = b; form.Shown += delegate { b.Focus(); }; }
                }
                form.ClientSize = new Size(width, y + 34 + pad);
                form.ShowDialog(owner);
                return result;
            }
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
}

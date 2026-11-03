// The floating mic indicator: a small always-on-top pill showing whether you're live, for use over games.
// It never takes focus, can be dragged anywhere, double-click opens V.T.T?, right-click shows options.
// Drawn as a per-pixel-alpha layered window so its rounded edges and glow blend with whatever is behind it.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VTT
{
    class MicIndicator : Form
    {
        public event Action OpenRequested, ToggleRequested, HideRequested;
        public event Action<Point> Moved;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
        const int WM_NCHITTEST = 0x84, HTCAPTION = 2, WM_NCLBUTTONDBLCLK = 0xA3, WM_NCRBUTTONUP = 0xA5, WM_EXITSIZEMOVE = 0x232;

        readonly float scale;
        readonly ContextMenu menu = new ContextMenu();
        string lastKey = "";
        float pulse, time;

        public MicIndicator()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Size = new Size((int)(146 * scale), (int)(44 * scale));
            menu.MenuItems.Add("Toggle mic", delegate { if (ToggleRequested != null) ToggleRequested(); });
            menu.MenuItems.Add("Open V.T.T?", delegate { if (OpenRequested != null) OpenRequested(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Hide indicator", delegate { if (HideRequested != null) HideRequested(); });
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST: m.Result = (IntPtr)HTCAPTION; return; // drag from anywhere
                case WM_NCLBUTTONDBLCLK: if (OpenRequested != null) OpenRequested(); return;
                case WM_NCRBUTTONUP: menu.Show(this, PointToClient(Cursor.Position)); return;
                case WM_EXITSIZEMOVE: if (Moved != null) Moved(Location); break;
            }
            base.WndProc(ref m);
        }

        // Puts the pill at a saved spot, or the top-right corner of the main screen if that spot is off-screen.
        public void PlaceAt(int x, int y)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            var r = new Rectangle(x, y, Width, Height);
            bool visible = false;
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) visible = true;
            Location = visible && x >= -10000 ? new Point(x, y) : new Point(area.Right - Width - (int)(16 * scale), area.Top + (int)(16 * scale));
        }

        public void Render(float dt, string text, Color color, float level, bool live)
        {
            time += dt;
            pulse = Theme.Approach(pulse, live ? level : 0, live && level > pulse ? 18 : 5, dt);
            string key = text + color.ToArgb() + (live ? ((int)(pulse * 40)).ToString() + ((int)(time * 4) % 8) : "");
            if (key == lastKey || !IsHandleCreated) return; // nothing visible changed
            lastKey = key;

            int w = Width, h = Height;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    var pill = new RectangleF(2 * scale, 2 * scale, w - 4 * scale, h - 4 * scale);
                    using (var path = Theme.RoundRect(pill, pill.Height / 2))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(225, Theme.Bg))) g.FillPath(b, path);
                        using (var pen = new Pen(Theme.Alpha(color, live ? 150 + 100 * pulse : 90), 1.4f * scale)) g.DrawPath(pen, path);
                    }
                    // Status dot with a glow that breathes when live and swells with your voice.
                    float cx = pill.X + pill.Height / 2 + 2 * scale, cy = h / 2f;
                    float glow = (9 + pulse * 7 + (live ? (float)Math.Sin(time * 3) * 1.2f : 0)) * scale;
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cx - glow, cy - glow, glow * 2, glow * 2);
                        using (var gb = new PathGradientBrush(gp) { CenterColor = Theme.Alpha(color, live ? 150 : 60), SurroundColors = new[] { Theme.Alpha(color, 0) } })
                            g.FillPath(gb, gp);
                    }
                    float dot = 5.5f * scale;
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - dot, cy - dot, dot * 2, dot * 2);
                    using (var font = new Font("Segoe UI Semibold", 11.5f * scale, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(Theme.Text))
                        g.DrawString(text.ToUpperInvariant(), font, b, new RectangleF(cx + 14 * scale, 0, w - cx - 18 * scale, h),
                            new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                Push(bmp);
            }
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                var size = new SIZE { W = bmp.Width, H = bmp.Height };
                var src = new POINT();
                var dst = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }
    }
}

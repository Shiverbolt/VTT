// V.T.T? Setup: installs VTT.exe (embedded in this file) into a folder you choose, adds shortcuts and an
// Apps & features entry, and doubles as the uninstaller (run with /uninstall).
//
// Command line:  /S  silent   /dir=PATH   /desktop=0|1   /startmenu=0|1   /startup=0|1   /run=0|1
//                /uninstall   /removedata=0|1   (/elevated is used internally after asking for admin rights)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("V.T.T? Setup")]
[assembly: AssemblyDescription("Installer for V.T.T?, voice-activated push-to-talk.")]
[assembly: AssemblyProduct("V.T.T?")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Shiverbolt. MIT License.")]
[assembly: AssemblyVersion("1.0.0.2")]
[assembly: AssemblyFileVersion("1.0.0.2")]
[assembly: AssemblyInformationalVersion("1.0.0-beta.2")]

namespace VTT.Setup
{
    static class Info
    {
        public const string Name = "V.T.T?", Version = "1.0.0-beta.2", DisplayVersion = "1.0 beta 2", Publisher = "Shiverbolt";
        public const string Url = "https://github.com/Shiverbolt/VTT";
        public const string ExeName = "VTT.exe", UninstallerName = "Uninstall VTT.exe", LicenseName = "LICENSE.txt", ShortcutName = "V.T.T.lnk";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VTT";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static readonly string DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VTT");
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VTT");
    }

    class Options
    {
        public string Dir = Info.DefaultDir;
        public bool Desktop = true, StartMenu = true, Startup = false, Launch = true, RemoveData = false;
        public bool Silent, Uninstall, Elevated;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            foreach (var raw in args)
            {
                string a = raw.TrimStart('/', '-'), key = a, val = "1";
                int eq = a.IndexOf('=');
                if (eq >= 0) { key = a.Substring(0, eq); val = a.Substring(eq + 1).Trim('"'); }
                bool on = val != "0" && !val.Equals("false", StringComparison.OrdinalIgnoreCase);
                switch (key.ToLowerInvariant())
                {
                    case "s": case "silent": o.Silent = true; break;
                    case "uninstall": o.Uninstall = true; break;
                    case "elevated": o.Elevated = true; break;
                    case "dir": o.Dir = val; break;
                    case "desktop": o.Desktop = on; break;
                    case "startmenu": o.StartMenu = on; break;
                    case "startup": o.Startup = on; break;
                    case "run": o.Launch = on; break;
                    case "removedata": o.RemoveData = on; break;
                }
            }
            return o;
        }

        public string ToArgs()
        {
            Func<bool, string> b = x => x ? "1" : "0";
            var list = new List<string> { "/dir=\"" + Dir + "\"", "/desktop=" + b(Desktop), "/startmenu=" + b(StartMenu),
                "/startup=" + b(Startup), "/run=" + b(Launch), "/removedata=" + b(RemoveData), "/elevated" };
            if (Uninstall) list.Add("/uninstall");
            return string.Join(" ", list);
        }
    }

    static class Installer
    {
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Info.ShortcutName); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Info.ShortcutName); } }

        // The folder an existing install lives in, if any (so upgrades and the uninstaller find it).
        public static string ExistingDir()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Info.UninstallKey))
                return k == null ? null : k.GetValue("InstallLocation") as string;
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".vtt-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        public static bool AppRunning() { return Process.GetProcessesByName("VTT").Length > 0; }

        public static void StopApp()
        {
            foreach (var p in Process.GetProcessesByName("VTT"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        public static void Install(Options o, Action<float, string> progress)
        {
            progress(0.05f, "Closing V.T.T? if it's running…");
            StopApp();

            progress(0.2f, "Copying files…");
            Directory.CreateDirectory(o.Dir);
            string exe = Path.Combine(o.Dir, Info.ExeName);
            ExtractResource(Info.ExeName, exe);
            ExtractResource(Info.LicenseName, Path.Combine(o.Dir, Info.LicenseName));
            string uninstaller = Path.Combine(o.Dir, Info.UninstallerName);
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninstaller, true);

            progress(0.55f, "Creating shortcuts…");
            if (o.Desktop) CreateShortcut(DesktopLink, exe, "", o.Dir); else TryDelete(DesktopLink);
            if (o.StartMenu) CreateShortcut(StartMenuLink, exe, "", o.Dir); else TryDelete(StartMenuLink);

            progress(0.75f, "Registering with Windows…");
            using (var run = Registry.CurrentUser.CreateSubKey(Info.RunKey))
            {
                if (o.Startup) run.SetValue("VTT", "\"" + exe + "\" --tray");
                else run.DeleteValue("VTT", false);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(Info.UninstallKey))
            {
                k.SetValue("DisplayName", Info.Name);
                k.SetValue("DisplayVersion", Info.Version);
                k.SetValue("Publisher", Info.Publisher);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", o.Dir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                k.SetValue("URLInfoAbout", Info.Url);
                k.SetValue("HelpLink", Info.Url + "/issues");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long bytes = new[] { exe, uninstaller }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
                k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
            progress(1f, "Done");
        }

        // Removes only the files this installer created; the folder itself goes only if it ends up empty.
        public static void Uninstall(Options o, Action<float, string> progress)
        {
            progress(0.1f, "Closing V.T.T?…");
            StopApp();
            progress(0.35f, "Removing files…");
            TryDelete(Path.Combine(o.Dir, Info.ExeName));
            TryDelete(Path.Combine(o.Dir, Info.LicenseName));
            TryDelete(DesktopLink);
            TryDelete(StartMenuLink);
            progress(0.6f, "Cleaning up Windows settings…");
            using (var run = Registry.CurrentUser.OpenSubKey(Info.RunKey, true))
                if (run != null) run.DeleteValue("VTT", false);
            Registry.CurrentUser.DeleteSubKeyTree(Info.UninstallKey, false);
            if (o.RemoveData)
            {
                progress(0.8f, "Deleting your settings and recordings…");
                try { if (Directory.Exists(Info.DataDir)) Directory.Delete(Info.DataDir, true); } catch { }
            }
            // A running program can't delete itself: a hidden command removes the uninstaller once we exit.
            string self = Path.Combine(o.Dir, Info.UninstallerName);
            if (File.Exists(self))
            {
                string cmd = string.Format("/c ping -n 3 127.0.0.1 > nul & del /f /q \"{0}\" & rmdir \"{1}\"", self, o.Dir);
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false });
            }
            else
            {
                try { Directory.Delete(o.Dir, false); } catch { }
            }
            progress(1f, "Done");
        }

        static void ExtractResource(string name, string path)
        {
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var dst = File.Create(path))
                src.CopyTo(dst);
        }

        static void CreateShortcut(string link, string target, string args, string workDir)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object sc = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                var t = sc.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { Info.Name + " - voice-activated push-to-talk" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        static void TryDelete(string file) { try { if (File.Exists(file)) File.Delete(file); } catch { } }
    }

    class SetupForm : Form
    {
        const int W = 400, Pad = 30;
        readonly Options options;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        readonly List<Toggle> toggles = new List<Toggle>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        Panel page;
        ProgressLine bar;
        Label status;
        volatile float target;
        volatile string statusText = "";
        bool working;

        public SetupForm(Options options)
        {
            this.options = options;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = (options.Uninstall ? "Uninstall " : "Install ") + Info.Name;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(W + 2 * Pad, 500);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            timer.Tick += delegate { Tick(); };
            timer.Start();
            if (options.Elevated) Shown += delegate { Run(); };
            else if (options.Uninstall) ShowUninstallPage();
            else ShowOptionsPage();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(this); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working) e.Cancel = true; // don't stop half-way
            base.OnFormClosing(e);
        }

        void Tick()
        {
            float dt = Math.Min(0.1f, (float)clock.Elapsed.TotalSeconds);
            clock.Restart();
            foreach (var t in toggles) t.Animate(dt);
            if (bar != null) bar.Animate(dt, target);
            if (status != null && status.Text != statusText) status.Text = statusText;
        }

        Panel NewPage(string title, string subtitle)
        {
            if (page != null) { Controls.Remove(page); page.Dispose(); }
            toggles.Clear();
            bar = null;
            status = null;
            page = new Panel { Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), BackColor = Theme.Bg };
            var icon = new PictureBox { Bounds = new Rectangle(Pad, 30, 48, 48), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Bg };
            try { icon.Image = new Icon(Icon, 48, 48).ToBitmap(); } catch { }
            var t = new Label { Text = title, Font = Theme.Ui(16, true), ForeColor = Theme.Text, Bounds = new Rectangle(Pad + 62, 26, W - 62, 34), AutoSize = false };
            var s = new Label { Text = subtitle, ForeColor = Theme.TextDim, Bounds = new Rectangle(Pad + 62, 60, W - 62, 22), AutoSize = false };
            page.Controls.AddRange(new Control[] { icon, t, s });
            Controls.Add(page);
            return page;
        }

        Button Primary(Panel p, string text, bool danger, EventHandler click)
        {
            var b = Theme.FlatButton(text);
            b.Bounds = new Rectangle(Pad + W - 160, ClientSize.Height - 30 - 38, 160, 38);
            b.Font = Theme.Ui(10, true);
            b.BackColor = danger ? Theme.Off : Theme.Accent;
            b.ForeColor = Theme.Bg;
            b.FlatAppearance.MouseOverBackColor = Theme.Lerp(b.BackColor, Color.White, 0.15f);
            b.Click += click;
            p.Controls.Add(b);
            AcceptButton = b;
            return b;
        }

        Button Secondary(Panel p, string text, EventHandler click)
        {
            var b = Theme.FlatButton(text);
            b.Bounds = new Rectangle(Pad, ClientSize.Height - 30 - 38, 120, 38);
            b.Click += click;
            p.Controls.Add(b);
            return b;
        }

        // A caption plus a folder path on its own line, shortened with "..." in the middle if it's too long.
        Control PathLabel(string caption, string path, ref int y)
        {
            var box = new Panel { Bounds = new Rectangle(Pad, y, W, 46), BackColor = Theme.Bg };
            var c = Theme.Caption(caption, Theme.TextDim, 9);
            c.Bounds = new Rectangle(0, 0, W, 20);
            var v = new PathText { Text = path, Bounds = new Rectangle(0, 20, W, 22) };
            box.Controls.Add(c);
            box.Controls.Add(v);
            y += 54;
            return box;
        }

        class PathText : Control
        {
            public PathText() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); Font = Theme.Ui(9.5f); BackColor = Theme.Bg; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Text, TextFormatFlags.PathEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }
        }

        Toggle ToggleRow(Panel p, string caption, bool on, ref int y)
        {
            var l = Theme.Caption(caption);
            l.Bounds = new Rectangle(Pad, y, W - 60, 34);
            var t = new Toggle { Checked = on, Location = new Point(Pad + W - 40, y + 6) };
            t.SnapToState();
            toggles.Add(t);
            p.Controls.Add(l);
            p.Controls.Add(t);
            y += 38;
            return t;
        }

        // ---------- pages ----------

        void ShowOptionsPage()
        {
            string existing = Installer.ExistingDir();
            if (!string.IsNullOrEmpty(existing)) options.Dir = existing;
            var p = NewPage("Install " + Info.Name, "Version " + Info.DisplayVersion + "  ·  voice-activated push-to-talk");

            int y = 108;
            var cap = Theme.Caption("Install to", Theme.TextDim, 9);
            cap.Bounds = new Rectangle(Pad, y, W, 20);
            p.Controls.Add(cap);
            y += 22;
            var dirBox = new TextBox { Text = options.Dir };
            var field = Theme.Field(dirBox, W - 100);
            field.Location = new Point(Pad, y);
            p.Controls.Add(field);
            var browse = Theme.FlatButton("Browse…");
            browse.Bounds = new Rectangle(Pad + W - 92, y, 92, 32);
            browse.Click += delegate {
                using (var dlg = new FolderBrowserDialog { Description = "Choose where to install " + Info.Name, SelectedPath = Directory.Exists(dirBox.Text) ? dirBox.Text : "" })
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        dirBox.Text = dlg.SelectedPath.EndsWith("VTT", StringComparison.OrdinalIgnoreCase) ? dlg.SelectedPath : Path.Combine(dlg.SelectedPath, "VTT");
            };
            p.Controls.Add(browse);
            y += 36;
            var note = Theme.Caption(string.IsNullOrEmpty(existing) ? "No admin rights needed for the default folder. Uses about 1 MB."
                : "An earlier version is installed here and will be updated. Your recordings are kept.", Theme.TextFaint, 8.5f);
            note.Bounds = new Rectangle(Pad, y, W, 22);
            p.Controls.Add(note);
            y += 34;

            var desktop = ToggleRow(p, "Desktop shortcut", options.Desktop, ref y);
            var startMenu = ToggleRow(p, "Start menu shortcut", options.StartMenu, ref y);
            var startup = ToggleRow(p, "Start with Windows (in the tray)", options.Startup, ref y);
            var launch = ToggleRow(p, "Open " + Info.Name + " when done", options.Launch, ref y);

            var license = new LinkLabel { Text = "Free and open source under the MIT License", Bounds = new Rectangle(Pad, y + 8, W, 22), LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.Ui(9) };
            license.LinkClicked += delegate { ShowLicense(); };
            p.Controls.Add(license);

            Secondary(p, "Cancel", delegate { Close(); });
            ActiveControl = Primary(p, "Install", false, delegate {
                options.Dir = dirBox.Text.Trim();
                options.Desktop = desktop.Checked;
                options.StartMenu = startMenu.Checked;
                options.Startup = startup.Checked;
                options.Launch = launch.Checked;
                if (options.Dir.Length == 0 || !Path.IsPathRooted(options.Dir)) { ThemedDialog.Show(this, "Choose a folder", "Please enter a full folder path, like " + Info.DefaultDir + ".", new[] { "OK" }); return; }
                Run();
            });
        }

        void ShowUninstallPage()
        {
            var p = NewPage("Uninstall " + Info.Name, "Version " + Info.DisplayVersion);
            int y = 112;
            p.Controls.Add(PathLabel("Removes the app from", options.Dir, ref y));
            var info = Theme.Caption("Shortcuts and the Apps & features entry are removed too. Other files in that folder are left alone.", Theme.TextDim, 9.5f);
            info.UseMnemonic = false;
            info.Bounds = new Rectangle(Pad, y, W, 44);
            info.TextAlign = ContentAlignment.TopLeft;
            p.Controls.Add(info);
            y += 60;
            var data = ToggleRow(p, "Also delete my settings and voice recordings", options.RemoveData, ref y);
            Secondary(p, "Cancel", delegate { Close(); });
            Primary(p, "Uninstall", true, delegate { options.RemoveData = data.Checked; Run(); });
        }

        void ShowLicense()
        {
            string text;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Info.LicenseName))
            using (var r = new StreamReader(s)) text = r.ReadToEnd();
            ThemedDialog.Show(this, "MIT License", string.Join(" ", text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.None).Select(x => x.Replace("\r\n", " ").Replace("\n", " ") + "\n\n")).Trim(), new[] { "Close" });
        }

        // ---------- work ----------

        void Run()
        {
            if (Installer.AppRunning() && !options.Elevated)
            {
                if (ThemedDialog.Show(this, Info.Name + " is running", "It will be closed to continue.", new[] { "Close it and continue", "Cancel" }) != 0) return;
            }
            if (!Installer.CanWrite(options.Dir))
            {
                if (options.Elevated)
                {
                    ThemedDialog.Show(this, "Can't write to that folder", "Even with administrator rights, " + options.Dir + " can't be written to. Choose another folder.", new[] { "OK" });
                    Close();
                    return;
                }
                if (ThemedDialog.Show(this, "Administrator rights needed", "Windows needs your permission to " + (options.Uninstall ? "remove files from " : "install into ") + options.Dir + ".",
                    new[] { "Continue", "Cancel" }) != 0) return;
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, options.ToArgs()) { Verb = "runas", UseShellExecute = true });
                    Close();
                }
                catch { } // the user said no to the Windows prompt; stay here
                return;
            }

            var p = NewPage(options.Uninstall ? "Uninstalling…" : "Installing…", "Version " + Info.DisplayVersion);
            int py = 112;
            p.Controls.Add(PathLabel(options.Uninstall ? "Removing from" : "Installing to", options.Dir, ref py));
            bar = new ProgressLine { Bounds = new Rectangle(Pad, 200, W, 6) };
            status = Theme.Caption("", Theme.TextDim, 9.5f);
            status.Bounds = new Rectangle(Pad, 216, W, 24);
            p.Controls.Add(bar);
            p.Controls.Add(status);
            working = true;
            ControlBox = false;

            var started = DateTime.Now;
            var worker = new Thread(() => {
                Exception error = null;
                try
                {
                    Action<float, string> progress = (f, s) => { target = f; statusText = s; Thread.Sleep(150); };
                    if (options.Uninstall) Installer.Uninstall(options, progress);
                    else Installer.Install(options, progress);
                }
                catch (Exception ex) { error = ex; }
                int wait = 900 - (int)(DateTime.Now - started).TotalMilliseconds; // let the bar finish its animation
                if (wait > 0) Thread.Sleep(wait);
                BeginInvoke(new Action(() => Finished(error)));
            }) { IsBackground = true };
            worker.Start();
        }

        void Finished(Exception error)
        {
            working = false;
            ControlBox = true;
            if (error != null)
            {
                var p = NewPage("Something went wrong", "");
                var msg = Theme.Caption(error.Message, Theme.Off, 9.5f);
                msg.Bounds = new Rectangle(Pad, 120, W, 120);
                msg.TextAlign = ContentAlignment.TopLeft;
                p.Controls.Add(msg);
                Primary(p, "Close", false, delegate { Close(); });
                return;
            }
            if (options.Uninstall)
            {
                var p = NewPage(Info.Name + " was removed", options.RemoveData ? "Your settings and recordings were deleted too." : "Your settings and recordings were kept.");
                Primary(p, "Close", false, delegate { Close(); });
            }
            else
            {
                var p = NewPage(Info.Name + " is installed", "Version " + Info.DisplayVersion);
                int fy = 112;
                p.Controls.Add(PathLabel("Installed to", options.Dir, ref fy));
                var tip = Theme.Caption("First time? Open Settings in the app and choose “Train my voice”.", Theme.TextFaint, 9);
                tip.Bounds = new Rectangle(Pad, 170, W, 40);
                tip.TextAlign = ContentAlignment.TopLeft;
                p.Controls.Add(tip);
                Primary(p, options.Launch ? "Open " + Info.Name : "Finish", false, delegate {
                    if (options.Launch) LaunchApp(options.Dir);
                    Close();
                });
            }
        }

        public static void LaunchApp(string dir)
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(dir, Info.ExeName)) { WorkingDirectory = dir, UseShellExecute = true }); } catch { }
        }
    }

    // Thin animated progress line.
    class ProgressLine : SmoothControl
    {
        float shown, time;
        public void Animate(float dt, float target)
        {
            time += dt;
            shown = Theme.Approach(shown, target, 6, dt);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, r.Height / 2)) using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
            float w = Math.Max(r.Height, r.Width * shown);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, w, r.Height), r.Height / 2))
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Accent, Theme.Live, 0f)) g.FillPath(b, path);
            // A soft highlight sweeping along the filled part, so it never looks frozen.
            float x = (time * 260) % (w + 80) - 40;
            using (var glow = new LinearGradientBrush(new RectangleF(x - 40, 0, 80, Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
            {
                var blend = new ColorBlend { Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }, Positions = new[] { 0f, 0.5f, 1f } };
                glow.InterpolationColors = blend;
                using (var clip = Theme.RoundRect(new RectangleF(0, 0, w, r.Height), r.Height / 2))
                {
                    g.SetClip(clip);
                    g.FillRectangle(glow, x - 40, 0, 80, Height);
                    g.ResetClip();
                }
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            var options = Options.Parse(args);
            if (options.Uninstall && !args.Any(a => a.StartsWith("/dir", StringComparison.OrdinalIgnoreCase) || a.StartsWith("-dir", StringComparison.OrdinalIgnoreCase)))
                options.Dir = Installer.ExistingDir() ?? Path.GetDirectoryName(Application.ExecutablePath);

            if (options.Silent)
            {
                try
                {
                    Action<float, string> quiet = (f, s) => { };
                    if (options.Uninstall) Installer.Uninstall(options, quiet);
                    else
                    {
                        Installer.Install(options, quiet);
                        // A silent install only opens the app when /run=1 is given explicitly.
                        if (args.Any(a => a.TrimStart('/', '-').StartsWith("run", StringComparison.OrdinalIgnoreCase)) && options.Launch)
                            SetupForm.LaunchApp(options.Dir);
                    }
                    return 0;
                }
                catch { return 1; }
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(options));
            return 0;
        }
    }
}

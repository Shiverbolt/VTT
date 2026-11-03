// Talk stats and badges, kept only on this PC in %APPDATA%\VTT\stats.txt (no audio, just counts).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VTT
{
    class Badge
    {
        public string Id, Name, Description;
        public Func<Stats, bool> Earned;
        public Badge(string id, string name, string description, Func<Stats, bool> earned) { Id = id; Name = name; Description = description; Earned = earned; }

        public static readonly Badge[] All = {
            new Badge("first-words", "First words", "Mic on by voice", s => s.VoiceOn >= 1),
            new Badge("trained", "Voice coach", "Finish the trainer", s => s.Trainings >= 1),
            new Badge("hands-free", "Hands-free", "50 voice ONs", s => s.VoiceOn >= 50),
            new Badge("over-and-out", "Over and out", "100 voice OFFs", s => s.VoiceOff >= 100),
            new Badge("ninja", "Ninja exit", "25 OFFs cut early", s => s.EarlyCuts >= 25),
            new Badge("chatterbox", "Chatterbox", "1 hour live", s => s.TotalLive >= 3600),
            new Badge("marathon", "Marathon", "10 hours live", s => s.TotalLive >= 36000),
            new Badge("regular", "Regular", "Use it on 7 days", s => s.UsedDays.Count >= 7),
            new Badge("streak", "On a roll", "3 days in a row", s => s.Streak >= 3),
            new Badge("party", "Party animal", "Find party mode", s => s.Parties >= 1),
        };
    }

    class Stats
    {
        public double TotalLive;
        public int VoiceOn, VoiceOff, EarlyCuts, Parties, Trainings;
        public readonly Dictionary<string, double> LiveByDay = new Dictionary<string, double>();
        public readonly HashSet<string> UsedDays = new HashSet<string>();
        public readonly Dictionary<string, string> Badges = new Dictionary<string, string>(); // id -> day unlocked
        bool dirty;

        static string FilePath { get { return Path.Combine(AppInfo.DataFolder, "stats.txt"); } }
        static string Day(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static string Today { get { return Day(DateTime.Now); } }

        public static Stats Load()
        {
            var s = new Stats();
            if (!File.Exists(FilePath)) return s;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line.Substring(0, i), v = line.Substring(i + 1);
                try
                {
                    if (k.StartsWith("day.")) s.LiveByDay[k.Substring(4)] = double.Parse(v, CultureInfo.InvariantCulture);
                    else if (k.StartsWith("used.")) s.UsedDays.Add(k.Substring(5));
                    else if (k.StartsWith("badge.")) s.Badges[k.Substring(6)] = v;
                    else switch (k)
                    {
                        case "live": s.TotalLive = double.Parse(v, CultureInfo.InvariantCulture); break;
                        case "voiceon": s.VoiceOn = int.Parse(v); break;
                        case "voiceoff": s.VoiceOff = int.Parse(v); break;
                        case "earlycuts": s.EarlyCuts = int.Parse(v); break;
                        case "parties": s.Parties = int.Parse(v); break;
                        case "trainings": s.Trainings = int.Parse(v); break;
                    }
                }
                catch { }
            }
            return s;
        }

        public void Save()
        {
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(AppInfo.DataFolder);
                var lines = new List<string> {
                    "live=" + TotalLive.ToString("0.0", CultureInfo.InvariantCulture), "voiceon=" + VoiceOn, "voiceoff=" + VoiceOff,
                    "earlycuts=" + EarlyCuts, "parties=" + Parties, "trainings=" + Trainings };
                lines.AddRange(LiveByDay.Select(kv => "day." + kv.Key + "=" + kv.Value.ToString("0.0", CultureInfo.InvariantCulture)));
                lines.AddRange(UsedDays.Select(d => "used." + d + "=1"));
                lines.AddRange(Badges.Select(kv => "badge." + kv.Key + "=" + kv.Value));
                File.WriteAllLines(FilePath, lines);
                dirty = false;
            }
            catch { }
        }

        public void Changed() { dirty = true; }

        public void AddLive(double seconds)
        {
            TotalLive += seconds;
            double t;
            LiveByDay.TryGetValue(Today, out t);
            LiveByDay[Today] = t + seconds;
            MarkUsed();
            dirty = true;
        }

        public void MarkUsed() { if (UsedDays.Add(Today)) dirty = true; }

        public double LiveToday { get { double t; return LiveByDay.TryGetValue(Today, out t) ? t : 0; } }

        public double LiveThisWeek
        {
            get
            {
                double sum = 0;
                for (int i = 0; i < 7; i++) { double t; if (LiveByDay.TryGetValue(Day(DateTime.Now.AddDays(-i)), out t)) sum += t; }
                return sum;
            }
        }

        // Consecutive days used, ending today (or yesterday, so a streak doesn't vanish first thing in the morning).
        public int Streak
        {
            get
            {
                var d = DateTime.Now.Date;
                if (!UsedDays.Contains(Day(d))) d = d.AddDays(-1);
                int n = 0;
                while (UsedDays.Contains(Day(d))) { n++; d = d.AddDays(-1); }
                return n;
            }
        }

        // Badges earned since the last check.
        public List<Badge> NewBadges()
        {
            var fresh = Badge.All.Where(b => !Badges.ContainsKey(b.Id) && b.Earned(this)).ToList();
            foreach (var b in fresh) Badges[b.Id] = Today;
            if (fresh.Count > 0) dirty = true;
            return fresh;
        }

        public static string Duration(double seconds)
        {
            if (seconds < 60) return (int)seconds + " s";
            if (seconds < 3600) return (int)(seconds / 60) + " min";
            return (seconds / 3600).ToString("0.0") + " h";
        }
    }

    // Three headline numbers: today, this week, streak.
    class StatTiles : SmoothControl
    {
        public Stats Stats;
        public StatTiles() { Size = new Size(340, 70); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (Stats == null) return;
            var tiles = new[] {
                Tuple.Create(Stats.Duration(Stats.LiveToday), "live today"),
                Tuple.Create(Stats.Duration(Stats.LiveThisWeek), "this week"),
                Tuple.Create(Stats.Streak + (Stats.Streak == 1 ? " day" : " days"), "streak") };
            float gap = 8, w = (Width - gap * 2) / 3f;
            using (var big = new Font("Segoe UI Semibold", 15f * DeviceDpi / 96f, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 11.5f * DeviceDpi / 96f, GraphicsUnit.Pixel))
                for (int i = 0; i < 3; i++)
                {
                    var r = new RectangleF(i * (w + gap), 0, w, Height - 1);
                    using (var path = Theme.RoundRect(r, 8)) using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using (var b = new SolidBrush(Theme.Text)) g.DrawString(tiles[i].Item1, big, b, new RectangleF(r.X, r.Y + 4, r.Width, r.Height * 0.55f), fmt);
                    using (var b = new SolidBrush(Theme.TextDim)) g.DrawString(tiles[i].Item2, small, b, new RectangleF(r.X, r.Y + r.Height * 0.5f, r.Width, r.Height * 0.4f), fmt);
                }
        }
    }

    // All badges, two per row; locked ones are dimmed.
    class BadgeGrid : SmoothControl
    {
        public Stats Stats;
        const int RowHeight = 58;
        public BadgeGrid() { Size = new Size(340, (Badge.All.Length + 1) / 2 * RowHeight); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (Stats == null) return;
            float k = DeviceDpi / 96f, gap = 8 * k, w = (Width - gap) / 2f, rh = RowHeight * k;
            using (var name = new Font("Segoe UI Semibold", 12.5f * k, GraphicsUnit.Pixel))
            using (var desc = new Font("Segoe UI", 11f * k, GraphicsUnit.Pixel))
            using (var star = new Font("Segoe UI Symbol", 16f * k, GraphicsUnit.Pixel))
                for (int i = 0; i < Badge.All.Length; i++)
                {
                    var badge = Badge.All[i];
                    bool got = Stats.Badges.ContainsKey(badge.Id);
                    var r = new RectangleF(i % 2 * (w + gap), i / 2 * rh, w, rh - gap);
                    using (var path = Theme.RoundRect(r, 8)) using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                    var circle = new RectangleF(r.X + 8 * k, r.Y + (r.Height - 30 * k) / 2, 30 * k, 30 * k);
                    using (var b = new SolidBrush(got ? Theme.Record : Theme.Border)) g.FillEllipse(b, circle);
                    using (var b = new SolidBrush(got ? Theme.Bg : Theme.TextFaint))
                        g.DrawString(got ? "★" : "?", star, b, circle, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    var text = new RectangleF(circle.Right + 8 * k, r.Y + 6 * k, r.Right - circle.Right - 12 * k, r.Height - 10 * k);
                    using (var b = new SolidBrush(got ? Theme.Text : Theme.TextDim)) g.DrawString(badge.Name, name, b, text);
                    using (var b = new SolidBrush(got ? Theme.TextDim : Theme.TextFaint))
                        g.DrawString(badge.Description, desc, b, new RectangleF(text.X, text.Y + 18 * k, text.Width, text.Height - 18 * k),
                            new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
        }
    }
}

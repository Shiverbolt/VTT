// The voice trainer: a guided page that has you say your ON and OFF commands several times, mixed with
// ordinary phrases (so V.T.T? learns what NOT to react to), then runs a short test of the result.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace VTT
{
    class TrainerPage : Panel
    {
        public class Recordings
        {
            public readonly List<short[]> On = new List<short[]>(), Off = new List<short[]>(), Other = new List<short[]>();
            public string OnLabel, OffLabel; // the words you said, as typed at the start
        }

        public event Action<Recordings> RecordingsReady; // save them and recalibrate before the test starts
        public event Action Closed;
        public Func<short[], string> Classify;            // "on", "off" or null, using the freshly trained matcher
        public Func<short[], double> EarlyCutAt;          // seconds into a recording the early cut would release the key, or -1
        public Func<bool> EarlyAvailable;                 // whether your OFF command can be cut early at all

        enum Stage { Intro, Quiet, Collect, Test, Results }

        class Step
        {
            public string Kind, Text; // Kind: "on", "off" or "other"
            public Step(string kind, string text) { Kind = kind; Text = text; }
        }

        static readonly string[] OtherPhrases = {
            "Can you hear me okay?", "Let's push up together", "I'm reloading, cover me", "Good game, everyone",
            "Wait for me", "One more round?", "What's the plan?", "Nice shot!", "Hold on a second", "That was close",
            "Okay, let's go", "Where are you?", "I need a minute", "Thanks for the help"
        };
        const int CommandRepeats = 6;

        Stage stage;
        List<Step> steps = new List<Step>();
        int index;
        Recordings data;
        readonly Dictionary<string, List<float[][]>> accepted = new Dictionary<string, List<float[][]>>();
        readonly List<string> testLog = new List<string>();
        int testPassed;
        float quietLeft, waited;
        string onLabel, offLabel;
        readonly Random rng = new Random();

        readonly TrainerCanvas canvas;
        readonly TextBox onBox, offBox;
        readonly Panel onField, offField;
        readonly Label onCaption, offCaption;
        readonly Button primary, secondary;

        public TrainerPage()
        {
            BackColor = Theme.Bg;
            canvas = new TrainerCanvas { Dock = DockStyle.Fill };
            Controls.Add(canvas);

            onCaption = Theme.Caption("What will you say to turn the mic ON?", Theme.TextDim, 9);
            onCaption.Bounds = new Rectangle(30, 300, 340, 22);
            onBox = new TextBox();
            onField = Theme.Field(onBox, 340);
            onField.Location = new Point(30, 324);
            offCaption = Theme.Caption("And to turn it OFF?", Theme.TextDim, 9);
            offCaption.Bounds = new Rectangle(30, 372, 340, 22);
            offBox = new TextBox();
            offField = Theme.Field(offBox, 340);
            offField.Location = new Point(30, 396);

            primary = Theme.FlatButton("");
            primary.Bounds = new Rectangle(200, 572, 170, 38);
            primary.BackColor = Theme.Accent;
            primary.ForeColor = Theme.Bg;
            primary.Font = Theme.Ui(10, true);
            primary.FlatAppearance.MouseOverBackColor = Theme.Lerp(Theme.Accent, Color.White, 0.15f);
            primary.Click += delegate { OnPrimary(); };
            secondary = Theme.FlatButton("");
            secondary.Bounds = new Rectangle(30, 572, 150, 38);
            secondary.Click += delegate { OnSecondary(); };

            canvas.Controls.AddRange(new Control[] { onCaption, onField, offCaption, offField, primary, secondary });
        }

        public bool Busy { get { return Visible && stage != Stage.Intro && stage != Stage.Results; } }

        public void Begin(string suggestedOn, string suggestedOff)
        {
            onBox.Text = suggestedOn;
            offBox.Text = suggestedOff;
            stage = Stage.Intro;
            Refresh_();
        }

        // ---------- flow ----------

        void OnPrimary()
        {
            switch (stage)
            {
                case Stage.Intro: StartTraining(); break;
                case Stage.Results: if (Closed != null) Closed(); break;
            }
        }

        void OnSecondary()
        {
            switch (stage)
            {
                case Stage.Collect:
                    if (index > 0) UndoLast();
                    else Cancel();
                    break;
                case Stage.Results:
                    Begin(onLabel, offLabel);
                    break;
                default:
                    Cancel();
                    break;
            }
        }

        // Leaving before the recording step finishes keeps your previous samples untouched.
        public void Cancel()
        {
            if (Closed != null) Closed();
        }

        void StartTraining()
        {
            onLabel = string.IsNullOrWhiteSpace(onBox.Text) ? "mic on" : onBox.Text.Trim();
            offLabel = string.IsNullOrWhiteSpace(offBox.Text) ? "over and out" : offBox.Text.Trim();
            data = new Recordings { OnLabel = onLabel, OffLabel = offLabel };
            accepted["on"] = new List<float[][]>();
            accepted["off"] = new List<float[][]>();

            // Commands and ordinary phrases are interleaved so you don't fall into a robotic rhythm.
            var others = OtherPhrases.OrderBy(x => rng.Next()).Take(6).ToList();
            foreach (var near in new[] { FirstWord(onLabel), FirstWord(offLabel) })
                if (near != null && !others.Contains(near)) others.Add(near); // near-misses like just "mic"
            others = others.OrderBy(x => rng.Next()).ToList();
            steps = new List<Step>();
            int o = 0;
            for (int round = 0; round < 2; round++)
            {
                for (int i = 0; i < CommandRepeats / 2; i++) steps.Add(new Step("on", onLabel));
                for (int i = 0; i < CommandRepeats / 2; i++) steps.Add(new Step("off", offLabel));
                int half = (others.Count + 1) / 2;
                for (int i = 0; i < half && o < others.Count; i++) steps.Add(new Step("other", others[o++]));
            }
            index = 0;
            stage = Stage.Quiet;
            quietLeft = 2.5f;
            Refresh_();
        }

        static string FirstWord(string phrase)
        {
            var words = phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 ? words[0] : null;
        }

        void StartTest()
        {
            steps = new List<Step> {
                new Step("on", onLabel), new Step("other", "I think we should wait here"), new Step("off", offLabel),
                new Step("on", "okay, " + onLabel), new Step("off", offLabel)
            };
            index = 0;
            testLog.Clear();
            testPassed = 0;
            stage = Stage.Test;
            Refresh_();
        }

        void UndoLast()
        {
            index--;
            var step = steps[index];
            var list = step.Kind == "on" ? data.On : step.Kind == "off" ? data.Off : data.Other;
            list.RemoveAt(list.Count - 1);
            if (step.Kind != "other") accepted[step.Kind].RemoveAt(accepted[step.Kind].Count - 1);
            canvas.Flash("Removed - say it again", Theme.TextDim);
            Refresh_();
        }

        public void OnUtterance(short[] pcm)
        {
            if (stage != Stage.Collect && stage != Stage.Test) return;
            var step = steps[index];
            var f = Features.Compute(pcm);
            if (f.Length < 15) { canvas.Flash("Too short - try again", Theme.Off); return; }
            waited = 0;

            if (stage == Stage.Test)
            {
                string got = Classify != null ? Classify(pcm) : null;
                string expected = step.Kind == "other" ? null : step.Kind;
                bool pass = got == expected;
                if (pass) testPassed++;
                string what = got == null ? "ignored" : "turned the mic " + got;
                if (step.Kind == "off" && got == "off" && EarlyCutAt != null)
                {
                    double at = EarlyCutAt(pcm);
                    if (at >= 0) what += string.Format(", cut after {0:0.0} s", at);
                }
                testLog.Add((pass ? "✓  " : "✗  ") + "“" + step.Text + "” → " + what);
                canvas.Flash(pass ? "✓  Correct - " + what : "✗  It " + what, pass ? Theme.Live : Theme.Off);
                Advance();
                return;
            }

            if (step.Kind != "other")
            {
                if (pcm.Length > MicCapture.Rate * 3.5) { canvas.Flash("Just the command on its own, please", Theme.Off); return; }
                if (!Consistent(f, accepted[step.Kind])) { canvas.Flash("That sounded different - say it like before", Theme.Off); return; }
                accepted[step.Kind].Add(f);
            }
            (step.Kind == "on" ? data.On : step.Kind == "off" ? data.Off : data.Other).Add(pcm);
            canvas.Flash("✓  Got it", Theme.Live);
            Advance();
        }

        // Rejects a repeat that is far further from the earlier ones than they are from each other
        // (a cough, a different word, someone else talking).
        static bool Consistent(float[][] f, List<float[][]> earlier)
        {
            if (earlier.Count < 2) return true;
            var spread = new List<double>();
            for (int i = 0; i < earlier.Count; i++)
            {
                double best = double.PositiveInfinity;
                for (int j = 0; j < earlier.Count; j++) if (i != j) best = Math.Min(best, Features.Distance(earlier[i], earlier[j]));
                if (!double.IsInfinity(best)) spread.Add(best);
            }
            if (spread.Count == 0) return true;
            spread.Sort();
            double typical = spread[spread.Count / 2];
            double d = earlier.Min(e => Features.Distance(f, e));
            return d <= typical * 2.5;
        }

        void Advance()
        {
            index++;
            if (index < steps.Count) { Refresh_(); return; }
            if (stage == Stage.Collect)
            {
                if (RecordingsReady != null) RecordingsReady(data);
                StartTest();
            }
            else
            {
                stage = Stage.Results;
                Refresh_();
            }
        }

        // ---------- presentation ----------

        void Refresh_()
        {
            bool intro = stage == Stage.Intro;
            foreach (Control c in new Control[] { onCaption, onField, offCaption, offField }) c.Visible = intro;
            primary.Visible = intro || stage == Stage.Results;
            secondary.Visible = true;
            canvas.ShowRing = !intro && stage != Stage.Results;
            canvas.Lines = null;

            switch (stage)
            {
                case Stage.Intro:
                    canvas.Set("VOICE TRAINER", "Teach V.T.T? your voice",
                        "You'll say your ON and OFF commands a few times, plus some everyday phrases so it learns what to ignore. " +
                        "About 2 minutes. Use the mic and room you normally play in.", "", 0);
                    primary.Text = "Start training";
                    secondary.Text = "Cancel";
                    break;
                case Stage.Quiet:
                    canvas.Set("STEP 1 OF 3  ·  QUIET", "Stay quiet for a moment", "Measuring the background noise in your room…", "", 0);
                    secondary.Text = "Cancel";
                    break;
                case Stage.Collect:
                {
                    var step = steps[index];
                    string kind = step.Kind == "on" ? "your ON command" : step.Kind == "off" ? "your OFF command" : "an everyday phrase";
                    canvas.Set("STEP 2 OF 3  ·  RECORDING", "“" + step.Text + "”",
                        "Say " + kind + " naturally, then pause.", (index + 1) + " / " + steps.Count, index / (float)steps.Count);
                    secondary.Text = index > 0 ? "Redo last" : "Cancel";
                    break;
                }
                case Stage.Test:
                {
                    var step = steps[index];
                    string hint = step.Kind == "other" ? "This one should be ignored." : "This should turn the mic " + step.Kind + ".";
                    canvas.Set("STEP 3 OF 3  ·  TEST", "“" + step.Text + "”", "Say it now. " + hint, (index + 1) + " / " + steps.Count, index / (float)steps.Count);
                    secondary.Text = "Skip test";
                    break;
                }
                case Stage.Results:
                {
                    bool good = testPassed >= steps.Count - 1;
                    canvas.Set("DONE", good ? "You're all set" : "Trained - but it could be better",
                        good ? string.Format("{0} of {1} test phrases handled correctly. You can fine-tune Sensitivity in settings.", testPassed, steps.Count)
                             : string.Format("{0} of {1} test phrases handled correctly. Try training again in a quieter spot, keep the mic at the same distance, " +
                               "or adjust Sensitivity in settings.", testPassed, steps.Count), "", 1);
                    canvas.Lines = testLog.ToList();
                    if (EarlyAvailable != null && !EarlyAvailable())
                        canvas.Lines.Add("✗  Early cut is off: your OFF command starts like everyday speech. A distinctive start, like “over and out”, lets V.T.T? cut the mic as you begin saying it.");
                    primary.Text = "Done";
                    secondary.Text = "Train again";
                    break;
                }
            }
        }

        public void Animate(float dt, float level)
        {
            if (stage == Stage.Quiet)
            {
                quietLeft -= dt;
                canvas.Progress = 1 - Math.Max(0, quietLeft) / 2.5f;
                if (quietLeft <= 0)
                {
                    stage = Stage.Collect;
                    canvas.Progress = 0;
                    Refresh_();
                }
            }
            if (stage == Stage.Collect || stage == Stage.Test)
            {
                waited += dt;
                if (waited > 10) { canvas.Flash("Didn't catch that - speak a little louder or closer", Theme.Record); waited = 0; }
            }
            canvas.Animate(dt, level);
        }
    }

    // Custom-drawn body of the trainer: heading, prompt, a ring that pulses with your voice, progress and feedback.
    class TrainerCanvas : SmoothControl
    {
        public bool ShowRing;
        public float Progress;
        public List<string> Lines;
        string eyebrow = "", title = "", body = "", counter = "", prevTitle = "";
        float titleFade = 1, shown, level, time, flashT = 1;
        string flash = "";
        Color flashColor = Theme.Live;
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        Font F(string family, float px)
        {
            string key = family + px;
            Font f;
            if (!fonts.TryGetValue(key, out f)) { f = new Font(family, px, GraphicsUnit.Pixel); fonts[key] = f; }
            return f;
        }

        public void Set(string eyebrow, string title, string body, string counter, float progress)
        {
            this.eyebrow = eyebrow;
            if (title != this.title) { prevTitle = this.title; this.title = title; titleFade = 0; }
            this.body = body;
            this.counter = counter;
            Progress = progress;
            Invalidate();
        }

        public void Flash(string text, Color color) { flash = text; flashColor = color; flashT = 0; }

        public void Animate(float dt, float rawLevel)
        {
            time += dt;
            level = Theme.Approach(level, rawLevel, rawLevel > level ? 20 : 6, dt);
            shown = Theme.Approach(shown, Progress, 8, dt);
            titleFade = Math.Min(1, titleFade + dt * 4);
            if (flashT < 1) flashT = Math.Min(1, flashT + dt * 0.6f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Theme.Bg);
            float s = Width / 400f;
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
            var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

            using (var b = new SolidBrush(Theme.TextFaint)) g.DrawString(eyebrow, F("Segoe UI Semibold", 11 * s), b, new RectangleF(30 * s, 28 * s, Width, 20 * s), left);

            float titleY = ShowRing ? 300 * s : 60 * s;
            var titleFmt = ShowRing ? center : left;
            var titleRect = ShowRing ? new RectangleF(20 * s, titleY, Width - 40 * s, 80 * s) : new RectangleF(30 * s, titleY, Width - 60 * s, 80 * s);
            float tf = Theme.EaseOut(titleFade);
            float titlePx = ShowRing ? 26 * s : 24 * s;
            using (var b = new SolidBrush(Theme.Alpha(Theme.Text, 255 * (1 - tf))))
                g.DrawString(prevTitle, F("Segoe UI Semibold", titlePx), b, new RectangleF(titleRect.X, titleRect.Y - 12 * s * tf, titleRect.Width, titleRect.Height), titleFmt);
            using (var b = new SolidBrush(Theme.Alpha(Theme.Text, 255 * tf)))
                g.DrawString(title, F("Segoe UI Semibold", titlePx), b, new RectangleF(titleRect.X, titleRect.Y + 12 * s * (1 - tf), titleRect.Width, titleRect.Height), titleFmt);

            var bodySize = g.MeasureString(title, F("Segoe UI Semibold", titlePx), (int)titleRect.Width);
            float bodyY = titleY + bodySize.Height + 10 * s;
            var bodyRect = ShowRing ? new RectangleF(30 * s, bodyY, Width - 60 * s, 60 * s) : new RectangleF(30 * s, bodyY, Width - 60 * s, 120 * s);
            using (var b = new SolidBrush(Theme.TextDim)) g.DrawString(body, F("Segoe UI", 14 * s), b, bodyRect, titleFmt);

            if (ShowRing)
            {
                // A ring that breathes while waiting and swells with your voice.
                var c = new PointF(Width / 2f, 170 * s);
                float r = (54 + (float)Math.Sin(time * 2.4) * 2 + level * 26) * s;
                using (var path = new GraphicsPath())
                {
                    float gr = r * 2.2f;
                    path.AddEllipse(c.X - gr, c.Y - gr, gr * 2, gr * 2);
                    using (var glow = new PathGradientBrush(path) { CenterColor = Theme.Alpha(Theme.Accent, 40 + 90 * level), SurroundColors = new[] { Theme.Alpha(Theme.Accent, 0) } })
                        g.FillPath(glow, path);
                }
                using (var pen = new Pen(Theme.Alpha(Theme.Accent, 120 + 135 * level), 3 * s)) g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                using (var b = new SolidBrush(Theme.Alpha(Color.White, 230)))
                    g.DrawString(Theme.GlyphMic, F(Theme.IconFontName, 30 * s), b, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2),
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }

            if (Lines != null)
            {
                float y = bodyY + 90 * s;
                var font = F("Segoe UI", 13.5f * s);
                foreach (var line in Lines)
                {
                    float h = g.MeasureString(line, font, (int)(Width - 60 * s)).Height;
                    using (var b = new SolidBrush(line.StartsWith("✓") ? Theme.Live : Theme.Off))
                        g.DrawString(line, font, b, new RectangleF(30 * s, y, Width - 60 * s, h + 2), left);
                    y += h + 6 * s;
                }
            }

            if (flashT < 1 && flash != "")
            {
                float a = flashT < 0.7f ? 1 : (1 - flashT) / 0.3f;
                using (var b = new SolidBrush(Theme.Alpha(flashColor, 255 * a)))
                    g.DrawString(flash, F("Segoe UI Semibold", 14 * s), b, new RectangleF(20 * s, 470 * s, Width - 40 * s, 30 * s), center);
            }

            if (ShowRing || Progress > 0)
            {
                var track = new RectangleF(30 * s, 530 * s, Width - 60 * s, 4 * s);
                using (var path = Theme.RoundRect(track, 2 * s)) using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                if (shown > 0.005f)
                    using (var path = Theme.RoundRect(new RectangleF(track.X, track.Y, Math.Max(4 * s, track.Width * shown), track.Height), 2 * s))
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                using (var b = new SolidBrush(Theme.TextFaint))
                    g.DrawString(counter, F("Segoe UI", 12 * s), b, new RectangleF(track.X, track.Y + 8 * s, track.Width, 20 * s),
                        new StringFormat { Alignment = StringAlignment.Far });
            }
        }
    }
}

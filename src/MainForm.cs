// The V.T.T? window: the animated main screen, the settings page that slides over it,
// and the logic that turns detected commands into holding / releasing the push-to-talk key.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Windows.Forms;

namespace VTT
{
    class MainForm : Form
    {
        static readonly Keys[] KeyChoices = {
            Keys.F13, Keys.F14, Keys.F15, Keys.F16, Keys.F17, Keys.F18, Keys.F19, Keys.F20, Keys.F21, Keys.F22, Keys.F23, Keys.F24,
            Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12,
            Keys.Scroll, Keys.Pause, Keys.Insert, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Oemtilde, Keys.CapsLock,
            Keys.LControlKey, Keys.LShiftKey, Keys.LMenu, Keys.RControlKey, Keys.RShiftKey, Keys.RMenu,
            Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9,
            Keys.A, Keys.B, Keys.C, Keys.D, Keys.E, Keys.F, Keys.G, Keys.H, Keys.I, Keys.J, Keys.K, Keys.L, Keys.M,
            Keys.N, Keys.O, Keys.P, Keys.Q, Keys.R, Keys.S, Keys.T, Keys.U, Keys.V, Keys.W, Keys.X, Keys.Y, Keys.Z
        };
        static readonly string[] Commands = { "on", "off" };
        const int W = 340; // settings content width

        Settings settings = Settings.Load();

        // audio
        MicCapture mic;
        readonly AudioPipe pipe = new AudioPipe();
        readonly SpectrumAnalyzer analyzer = new SpectrumAnalyzer();
        readonly Segmenter segmenter = new Segmenter();
        readonly VoiceMatcher matcher = new VoiceMatcher();
        readonly Dictionary<string, List<string>> sampleFiles = new Dictionary<string, List<string>> { { "on", new List<string>() }, { "off", new List<string>() } };
        List<string> inputs = new List<string>(), outputs = new List<string>();
        SpeechRecognitionEngine engine;
        Grammar commandGrammar;
        HashSet<string> onSet, offSet;
        bool engineRunning, engineStopping, reloadPending;
        readonly short[] onTone = Tone(660, 990), offTone = Tone(880, 520);

        // state
        string recordTarget;
        bool live, listening = true, loading;
        Keys heldKey;
        DateTime lastSpeech = DateTime.Now;
        float slide;
        int slideDir;

        // ui
        OrbView view;
        Panel settingsPanel;
        Label settingsHint;
        ComboBox modeBox, keyBox, inputBox, outputBox;
        TextBox onBox, offBox;
        Stepper confStep, sensStep, autoOffStep;
        Toggle filterToggle, soundsToggle, askExitToggle;
        TrainerPage trainer;
        readonly bool startInTray;
        Control voicePanel, phrasePanel;
        readonly Dictionary<string, Label> sampleLabels = new Dictionary<string, Label>();
        readonly Dictionary<string, Button> recordButtons = new Dictionary<string, Button>();
        readonly List<LevelMeter> meters = new List<LevelMeter>();
        readonly List<Toggle> toggles = new List<Toggle>();
        NotifyIcon tray;
        Timer frameTimer, recordTimer, applyTimer;
        readonly Stopwatch clock = new Stopwatch();
        readonly float[] rawBands = new float[SpectrumAnalyzer.Bands];

        public MainForm(bool startInTray)
        {
            this.startInTray = startInTray;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = AppInfo.Name;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            KeyPreview = true;
            ClientSize = new Size(400, 640);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }

            view = new OrbView { Dock = DockStyle.Fill };
            view.OrbClick += delegate { if (recordTarget == null) SetLive(!live, "clicked"); };
            view.PauseClick += delegate { SetListening(!listening); };
            view.SettingsClick += delegate { OpenSettings(); };

            loading = true;
            BuildSettings();
            loading = false;
            trainer = new TrainerPage { Visible = false, Size = ClientSize };
            trainer.RecordingsReady += SaveTraining;
            trainer.Closed += CloseTrainer;
            trainer.Classify = pcm => {
                double on, off;
                return matcher.Match(Features.Compute(pcm), settings.Sensitivity / 100.0, out on, out off);
            };
            Controls.Add(trainer);
            Controls.Add(settingsPanel);
            Controls.Add(view);

            tray = new NotifyIcon { Icon = Icon, Text = AppInfo.Name, Visible = true };
            tray.DoubleClick += delegate { ShowFromTray(); };
            var menu = new ContextMenu();
            menu.MenuItems.Add("Show", delegate { ShowFromTray(); });
            menu.MenuItems.Add("Toggle mic", delegate { SetLive(!live, "tray"); });
            menu.MenuItems.Add("Pause / resume listening", delegate { SetListening(!listening); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Exit", delegate { Close(); });
            tray.ContextMenu = menu;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };

            frameTimer = new Timer { Interval = 15 };
            frameTimer.Tick += delegate { Frame(); };
            recordTimer = new Timer { Interval = 8000 };
            recordTimer.Tick += delegate { StopRecording("Didn't hear anything. Try recording again."); };
            applyTimer = new Timer { Interval = 450 };
            applyTimer.Tick += delegate { applyTimer.Stop(); ApplySettings(); };

            segmenter.SpeechActivity += () => lastSpeech = DateTime.Now;
            segmenter.Utterance += u => { try { BeginInvoke(new Action(() => OnUtterance(u))); } catch { } };

            heldKey = settings.Key;
            LoadSamples();
            UpdateModePanels();
            UpdateStatus();
            ResumeLayout(false);
            PerformLayout();

            Shown += delegate {
                Theme.DarkScrollbars(settingsPanel);
                StartAudio();
                clock.Start();
                frameTimer.Start();
                if (this.startInTray) WindowState = FormWindowState.Minimized;
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (settingsPanel == null) return;
            if (trainer != null) trainer.Size = ClientSize;
            settingsPanel.Size = ClientSize;
            settingsPanel.Left = slide >= 1 && slideDir == 0 ? 0 : ClientSize.Width;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && trainer.Visible) { trainer.Cancel(); return true; }
            if (keyData == Keys.Escape && slide >= 1) { CloseSettings(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---------- settings page ----------

        void BuildSettings()
        {
            settingsPanel = new Panel { AutoScroll = true, BackColor = Theme.Bg, Size = ClientSize, Location = new Point(ClientSize.Width, 0) };

            var back = new Label { Text = Theme.GlyphBack, Font = Theme.Icons(11), ForeColor = Theme.TextDim, Bounds = new Rectangle(14, 18, 34, 34),
                TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            back.Click += delegate { CloseSettings(); };
            back.MouseEnter += delegate { back.ForeColor = Theme.Text; };
            back.MouseLeave += delegate { back.ForeColor = Theme.TextDim; };
            var title = new Label { Text = "Settings", Font = Theme.Ui(15, true), ForeColor = Theme.Text, Bounds = new Rectangle(50, 15, 250, 40), TextAlign = ContentAlignment.MiddleLeft };
            settingsHint = Theme.Caption("", Theme.TextDim, 9);
            settingsHint.Bounds = new Rectangle(24, 58, W, 40);
            settingsHint.TextAlign = ContentAlignment.TopLeft;

            var content = Flow();
            content.Location = new Point(24, 96);
            content.Padding = new Padding(0, 0, 0, 28);

            // Commands
            content.Controls.Add(Section("Commands"));
            modeBox = Theme.Combo();
            modeBox.Width = 190;
            modeBox.Items.AddRange(new object[] { "My voice samples", "Spoken phrases" });
            modeBox.SelectedIndex = settings.UseRecordings ? 0 : 1;
            modeBox.SelectedIndexChanged += delegate { UpdateModePanels(); ScheduleApply(); };
            content.Controls.Add(Row("Detect with", modeBox));

            var vp = Flow();
            var train = Theme.FlatButton("Train my voice  ·  2 min");
            train.Size = new Size(W, 38);
            train.Margin = new Padding(0, 4, 0, 8);
            train.BackColor = Theme.Accent;
            train.ForeColor = Theme.Bg;
            train.Font = Theme.Ui(10, true);
            train.FlatAppearance.MouseOverBackColor = Theme.Lerp(Theme.Accent, Color.White, 0.15f);
            train.Click += delegate { OpenTrainer(); };
            vp.Controls.Add(train);
            vp.Controls.Add(Note("Guided recording of your commands plus everyday phrases, so V.T.T? learns your voice and what to ignore. Or record single samples below."));
            foreach (var cmd in Commands)
            {
                string c = cmd;
                var row = new Panel { Size = new Size(W, 40), Margin = new Padding(0, 0, 0, 4), BackColor = Theme.Bg };
                var lbl = Theme.Caption("");
                lbl.Bounds = new Rectangle(0, 0, 160, 40);
                var rec = Theme.FlatButton("Record");
                rec.Bounds = new Rectangle(W - 160, 4, 88, 32);
                rec.Click += delegate { StartRecording(c); };
                var play = Theme.FlatButton(Theme.GlyphPlay, true);
                play.Bounds = new Rectangle(W - 68, 4, 32, 32);
                play.Click += delegate { PlayLatest(c); };
                var del = Theme.FlatButton(Theme.GlyphDelete, true);
                del.Bounds = new Rectangle(W - 32, 4, 32, 32);
                del.Click += delegate { ClearSamples(c); };
                row.Controls.AddRange(new Control[] { lbl, rec, play, del });
                sampleLabels[c] = lbl;
                recordButtons[c] = rec;
                vp.Controls.Add(row);
            }
            vp.Controls.Add(Meter());
            sensStep = new Stepper { Min = 50, Max = 200, Step = 5, Suffix = "%" };
            sensStep.Set(settings.Sensitivity);
            sensStep.ValueChanged += delegate { ScheduleApply(); };
            vp.Controls.Add(Row("Sensitivity", sensStep));
            vp.Controls.Add(Note("Higher catches commands more easily; lower ignores more. Missed commands? Raise it. Random triggers? Lower it."));
            voicePanel = vp;
            content.Controls.Add(vp);

            var pp = Flow();
            pp.Controls.Add(Note("Turn the mic on with (comma-separated)", 22));
            onBox = new TextBox { Text = settings.OnPhrases };
            onBox.TextChanged += delegate { ScheduleApply(); };
            pp.Controls.Add(Theme.Field(onBox, W));
            pp.Controls.Add(Note("Turn the mic off with", 22));
            offBox = new TextBox { Text = settings.OffPhrases };
            offBox.TextChanged += delegate { ScheduleApply(); };
            pp.Controls.Add(Theme.Field(offBox, W));
            confStep = new Stepper { Min = 30, Max = 99, Step = 1, Suffix = "%" };
            confStep.Set(settings.MinConfidence);
            confStep.ValueChanged += delegate { ScheduleApply(); };
            pp.Controls.Add(Row("Minimum confidence", confStep));
            filterToggle = NewToggle(settings.FilterSpeech);
            pp.Controls.Add(Row("Ignore other speech", filterToggle));
            phrasePanel = pp;
            content.Controls.Add(pp);

            // Push-to-talk
            content.Controls.Add(Section("Push-to-talk"));
            keyBox = Theme.Combo();
            keyBox.Width = 130;
            foreach (var k in KeyChoices) keyBox.Items.Add(k);
            if (!KeyChoices.Contains(settings.Key)) keyBox.Items.Insert(0, settings.Key);
            keyBox.SelectedItem = settings.Key;
            keyBox.SelectedIndexChanged += delegate { ScheduleApply(); };
            content.Controls.Add(Row("Key to hold", keyBox));
            var bind = Theme.FlatButton("Press the key in 3 seconds");
            bind.Size = new Size(W, 34);
            bind.Margin = new Padding(0, 4, 0, 2);
            bind.Click += delegate { TestKey(bind); };
            content.Controls.Add(bind);
            content.Controls.Add(Note("Use this while setting the push-to-talk keybind in Discord, OBS or your game."));
            autoOffStep = new Stepper { Min = 0, Max = 600, Step = 5, Suffix = " s", ZeroText = "Never" };
            autoOffStep.Set(settings.AutoOffSeconds);
            autoOffStep.ValueChanged += delegate { ScheduleApply(); };
            content.Controls.Add(Row("Auto-off after silence", autoOffStep));

            // Audio
            content.Controls.Add(Section("Audio"));
            inputBox = Theme.Combo();
            inputBox.Width = 210;
            inputBox.DropDown += delegate { RefreshDevices(); };
            inputBox.SelectedIndexChanged += delegate { ScheduleApply(); };
            content.Controls.Add(Row("Microphone", inputBox));
            content.Controls.Add(Meter());
            outputBox = Theme.Combo();
            outputBox.Width = 210;
            outputBox.DropDown += delegate { RefreshDevices(); };
            outputBox.SelectedIndexChanged += delegate { ScheduleApply(); };
            content.Controls.Add(Row("Sound output", outputBox));
            soundsToggle = NewToggle(settings.Sounds);
            content.Controls.Add(Row("On / off sounds", soundsToggle));
            RefreshDevices();

            // Privacy
            content.Controls.Add(Section("Privacy"));
            askExitToggle = NewToggle(settings.AskDeleteOnExit);
            content.Controls.Add(Row("Ask to delete recordings on quit", askExitToggle));
            var wipe = Theme.FlatButton("Delete all my data");
            wipe.Size = new Size(W, 34);
            wipe.Margin = new Padding(0, 4, 0, 2);
            wipe.ForeColor = Theme.Off;
            wipe.Click += delegate { DeleteAllData(); };
            content.Controls.Add(wipe);
            content.Controls.Add(Note("Removes your voice recordings and settings from this PC (" + AppInfo.DataFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%") + "). Everything stays on this PC; nothing is ever uploaded."));

            // About
            content.Controls.Add(Section(""));
            content.Controls.Add(Note(AppInfo.Name + "  " + AppInfo.Version + "  ·  MIT License", 22));
            var link = new LinkLabel { Text = AppInfo.RepoUrl.Replace("https://", ""), AutoSize = false, Size = new Size(W, 22), Font = Theme.Ui(9),
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0) };
            link.LinkClicked += delegate { try { Process.Start(AppInfo.RepoUrl); } catch { } };
            content.Controls.Add(link);

            settingsPanel.Controls.AddRange(new Control[] { back, title, settingsHint, content });
        }

        static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Bg, Margin = new Padding(0) };
        }

        static Label Section(string text)
        {
            var l = Theme.Caption(text.ToUpperInvariant(), Theme.TextFaint, 8);
            l.Font = Theme.Ui(8, true);
            l.Size = new Size(W, 26);
            l.Margin = new Padding(0, 14, 0, 4);
            l.TextAlign = ContentAlignment.BottomLeft;
            return l;
        }

        static Label Note(string text, int height = 0)
        {
            var l = Theme.Caption(text, Theme.TextDim, 8.5f);
            if (height == 0) height = TextRenderer.MeasureText(text, l.Font, new Size(W, 0), TextFormatFlags.WordBreak).Height + 6;
            l.Size = new Size(W, height);
            l.Margin = new Padding(0, 0, 0, 4);
            l.TextAlign = ContentAlignment.TopLeft;
            return l;
        }

        static Panel Row(string caption, Control control)
        {
            var row = new Panel { Size = new Size(W, 40), Margin = new Padding(0, 0, 0, 4), BackColor = Theme.Bg };
            var l = Theme.Caption(caption);
            l.Bounds = new Rectangle(0, 0, W - control.Width - 8, 40);
            control.Location = new Point(W - control.Width, (40 - control.Height) / 2);
            row.Controls.Add(l);
            row.Controls.Add(control);
            return row;
        }

        Control Meter()
        {
            var row = new Panel { Size = new Size(W, 16), Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Bg };
            var m = new LevelMeter { Bounds = new Rectangle(0, 5, W, 6) };
            row.Controls.Add(m);
            meters.Add(m);
            return row;
        }

        Toggle NewToggle(bool on)
        {
            var t = new Toggle { Checked = on };
            t.SnapToState();
            t.CheckedChanged += delegate { ScheduleApply(); };
            toggles.Add(t);
            return t;
        }

        void UpdateModePanels()
        {
            bool rec = modeBox.SelectedIndex == 0;
            voicePanel.Visible = rec;
            phrasePanel.Visible = !rec;
        }

        void RefreshDevices()
        {
            bool wasLoading = loading;
            loading = true;
            inputs = AudioDevices.Inputs();
            outputs = AudioDevices.Outputs();
            Fill(inputBox, inputs, settings.InputDevice);
            Fill(outputBox, outputs, settings.OutputDevice);
            loading = wasLoading;
        }

        static void Fill(ComboBox box, List<string> devices, string selected)
        {
            box.BeginUpdate();
            box.Items.Clear();
            box.Items.Add("System default");
            foreach (var d in devices) box.Items.Add(d);
            int i = devices.IndexOf(selected);
            box.SelectedIndex = i < 0 ? 0 : i + 1;
            box.EndUpdate();
        }

        void ScheduleApply()
        {
            if (loading) return;
            applyTimer.Stop();
            applyTimer.Start();
        }

        void ApplySettings()
        {
            string oldInput = settings.InputDevice;
            bool phrasesChanged = settings.OnPhrases != onBox.Text || settings.OffPhrases != offBox.Text || settings.FilterSpeech != filterToggle.Checked;
            settings.UseRecordings = modeBox.SelectedIndex == 0;
            settings.OnPhrases = onBox.Text;
            settings.OffPhrases = offBox.Text;
            settings.MinConfidence = (int)confStep.Value;
            settings.Sensitivity = (int)sensStep.Value;
            settings.AskDeleteOnExit = askExitToggle.Checked;
            settings.FilterSpeech = filterToggle.Checked;
            settings.Key = (Keys)keyBox.SelectedItem;
            settings.AutoOffSeconds = (int)autoOffStep.Value;
            settings.InputDevice = inputBox.SelectedIndex <= 0 ? "" : (string)inputBox.SelectedItem;
            settings.OutputDevice = outputBox.SelectedIndex <= 0 ? "" : (string)outputBox.SelectedItem;
            settings.Sounds = soundsToggle.Checked;
            settings.Save();

            if (settings.Key != heldKey)
            {
                if (live) { KeySender.Send(heldKey, false); KeySender.Send(settings.Key, true); }
                heldKey = settings.Key;
            }
            if (settings.InputDevice != oldInput) RestartMic();
            SyncEngine(phrasesChanged);
            UpdateStatus();
            SetHint(ListeningHint());
        }

        void OpenSettings()
        {
            if (slideDir != 0 || slide > 0) return;
            RefreshDevices();
            view.Overlay = Snapshot();
            slideDir = 1;
        }

        void CloseSettings()
        {
            if (slideDir != 0 || slide < 1) return;
            if (applyTimer.Enabled) { applyTimer.Stop(); ApplySettings(); }
            view.Overlay = Snapshot();
            settingsPanel.Left = ClientSize.Width;
            view.Frozen = false;
            slideDir = -1;
        }

        Bitmap Snapshot()
        {
            var bmp = new Bitmap(settingsPanel.Width, settingsPanel.Height);
            settingsPanel.DrawToBitmap(bmp, new Rectangle(Point.Empty, settingsPanel.Size));
            if (view.Overlay != null) view.Overlay.Dispose();
            return bmp;
        }

        // ---------- frame loop ----------

        void Frame()
        {
            float dt = Math.Min(0.1f, (float)clock.Elapsed.TotalSeconds);
            clock.Restart();
            float level = analyzer.Read(rawBands);

            if (Visible && WindowState != FormWindowState.Minimized)
            {
                view.Animate(dt, rawBands, level);
                foreach (var m in meters) m.Value = view.Level;
                foreach (var t in toggles) t.Animate(dt);
                if (trainer.Visible) trainer.Animate(dt, view.Level);
            }

            if (slideDir != 0)
            {
                slide = Math.Max(0, Math.Min(1, slide + slideDir * dt / 0.34f));
                view.Slide = slide;
                if (slide >= 1)
                {
                    slideDir = 0;
                    settingsPanel.Left = 0;
                    settingsPanel.BringToFront();
                    view.Frozen = true;
                }
                else if (slide <= 0)
                {
                    slideDir = 0;
                    if (view.Overlay != null) { view.Overlay.Dispose(); view.Overlay = null; }
                }
            }

            if (live && settings.AutoOffSeconds > 0 && (DateTime.Now - lastSpeech).TotalSeconds >= settings.AutoOffSeconds)
            {
                SetLive(false, "timeout");
                SetHint("Turned off after " + settings.AutoOffSeconds + " s of silence");
            }
        }

        // ---------- audio ----------

        void StartAudio()
        {
            RestartMic();
            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                if (recognizers.Count == 0) throw new Exception("No Windows speech recognizer is installed (Settings > Time & language > Speech).");
                var info = recognizers.FirstOrDefault(r => r.Culture.Equals(CultureInfo.CurrentUICulture))
                    ?? recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                    ?? recognizers[0];
                engine = new SpeechRecognitionEngine(info);
                engine.SetInputToAudioStream(pipe, new SpeechAudioFormatInfo(MicCapture.Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                engine.SpeechRecognized += (s, e) => { try { BeginInvoke(new Action(() => OnRecognized(e.Result))); } catch { } };
                engine.RecognizeCompleted += (s, e) => { try { BeginInvoke(new Action(() => { engineRunning = false; engineStopping = false; SyncEngine(false); })); } catch { } };
                SyncEngine(true);
            }
            catch (Exception ex)
            {
                engine = null;
                if (!settings.UseRecordings) SetHint(ex.Message + " Voice samples still work.");
            }
            if (mic != null) SetHint(ListeningHint());
            UpdateStatus();
        }

        void RestartMic()
        {
            if (mic != null) { mic.Dispose(); mic = null; }
            try
            {
                var m = new MicCapture(settings.InputDevice == "" ? -1 : inputs.IndexOf(settings.InputDevice));
                m.Samples += s => { segmenter.Add(s); analyzer.Process(s); pipe.Write(s); };
                m.Start();
                mic = m;
            }
            catch (Exception ex)
            {
                SetHint(ex.Message);
            }
        }

        int OutputIndex() { return settings.OutputDevice == "" ? -1 : outputs.IndexOf(settings.OutputDevice); }

        // Runs the speech recognizer only when listening in phrase mode; reloads grammars when asked.
        void SyncEngine(bool reload)
        {
            if (engine == null) return;
            reloadPending |= reload;
            if (engineStopping) return;
            bool want = listening && !settings.UseRecordings;
            if (engineRunning && (reloadPending || !want))
            {
                engineStopping = true;
                engine.RecognizeAsyncCancel(); // RecognizeCompleted calls back in here
                return;
            }
            if (reloadPending) { LoadGrammars(); reloadPending = false; }
            if (want && !engineRunning)
            {
                engine.RecognizeAsync(RecognizeMode.Multiple);
                engineRunning = true;
            }
        }

        void LoadGrammars()
        {
            onSet = new HashSet<string>(Settings.Split(settings.OnPhrases));
            offSet = new HashSet<string>(Settings.Split(settings.OffPhrases));
            var all = onSet.Concat(offSet).Distinct().ToArray();
            if (all.Length == 0) all = new[] { "mic on", "mic off" };

            engine.UnloadAllGrammars();
            var builder = new GrammarBuilder(new Choices(all)) { Culture = engine.RecognizerInfo.Culture };
            commandGrammar = new Grammar(builder) { Name = "commands", Priority = 1 };
            engine.LoadGrammar(commandGrammar);
            if (settings.FilterSpeech)
            {
                // Ordinary speech gets matched to dictation instead of being forced onto a command phrase.
                engine.LoadGrammar(new DictationGrammar { Name = "dictation", Priority = 0, Weight = 0.5f });
            }
        }

        // ---------- detection ----------

        void OnRecognized(RecognitionResult result)
        {
            if (result.Grammar != commandGrammar || recordTarget != null || settings.UseRecordings || !listening) return;
            string text = result.Text.ToLowerInvariant();
            int conf = (int)Math.Round(result.Confidence * 100);
            if (conf < settings.MinConfidence)
            {
                SetHint(string.Format("Heard “{0}” but only {1}% sure", text, conf));
                return;
            }
            SetHint(string.Format("Heard “{0}”  ·  {1}%", text, conf));
            if (onSet.Contains(text)) SetLive(true, text);
            else if (offSet.Contains(text)) SetLive(false, text);
        }

        void OnUtterance(short[] pcm)
        {
            if (trainer.Visible) { trainer.OnUtterance(pcm); return; }
            if (recordTarget != null) { SaveSample(recordTarget, pcm); return; }
            if (!listening || !settings.UseRecordings) return;
            // Matching needs both commands: their distance from each other calibrates what counts as a match.
            if (!matcher.Ready) return;
            double on, off, cutoff = settings.Sensitivity / 100.0;
            string cmd = matcher.Match(Features.Compute(pcm), cutoff, out on, out off);
            SetHint(string.Format("on {0}  ·  off {1}  ·  needs under {2:0.00}{3}",
                Fmt(on), Fmt(off), cutoff, cmd == null ? "" : "  →  mic " + cmd));
            if (cmd == "on") SetLive(true, "voice");
            else if (cmd == "off") SetLive(false, "voice");
        }

        static string Fmt(double score) { return double.IsInfinity(score) ? "–" : score.ToString("0.00"); }

        // ---------- recorded samples ----------

        void LoadSamples()
        {
            foreach (var cmd in Commands) { matcher.Templates[cmd].Clear(); sampleFiles[cmd].Clear(); }
            matcher.Other.Clear();
            if (Directory.Exists(AppInfo.SamplesFolder))
            {
                foreach (var cmd in Commands)
                    foreach (var file in Directory.GetFiles(AppInfo.SamplesFolder, cmd + "_*.wav").OrderBy(f => f))
                    {
                        try
                        {
                            matcher.Templates[cmd].Add(Features.Compute(Wav.Read(file)));
                            sampleFiles[cmd].Add(file);
                        }
                        catch { }
                    }
                foreach (var file in Directory.GetFiles(AppInfo.SamplesFolder, "other_*.wav"))
                {
                    try { matcher.Other.Add(Features.Compute(Wav.Read(file))); } catch { }
                }
            }
            matcher.Recalibrate();
            UpdateSampleLabels();
        }

        // ---------- voice trainer ----------

        void OpenTrainer()
        {
            if (mic == null) { SetHint("The microphone isn't available. Pick another one under Audio."); return; }
            if (recordTarget != null) StopRecording(null);
            var on = Settings.Split(settings.OnPhrases).FirstOrDefault() ?? "mic on";
            var off = Settings.Split(settings.OffPhrases).FirstOrDefault() ?? "over and out";
            trainer.Begin(on, off);
            trainer.Size = ClientSize;
            trainer.Visible = true;
            trainer.BringToFront();
            if (live) SetLive(false, "training");
        }

        void CloseTrainer()
        {
            trainer.Visible = false;
            SetHint(ListeningHint());
            UpdateStatus();
        }

        // Replaces all earlier samples with the trainer's recordings.
        void SaveTraining(TrainerPage.Recordings rec)
        {
            DeleteRecordings();
            Directory.CreateDirectory(AppInfo.SamplesFolder);
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var groups = new[] { Tuple.Create("on", rec.On), Tuple.Create("off", rec.Off), Tuple.Create("other", rec.Other) };
            foreach (var g in groups)
                for (int i = 0; i < g.Item2.Count; i++)
                    Wav.Write(Path.Combine(AppInfo.SamplesFolder, string.Format("{0}_{1}_{2:00}.wav", g.Item1, stamp, i)), g.Item2[i]);
            LoadSamples();
            if (modeBox.SelectedIndex != 0) modeBox.SelectedIndex = 0;
            settings.UseRecordings = true;
            settings.Save();
            SyncEngine(false);
            UpdateStatus();
        }

        static bool HasRecordings()
        {
            return Directory.Exists(AppInfo.SamplesFolder) && Directory.GetFiles(AppInfo.SamplesFolder, "*.wav").Length > 0;
        }

        // Deletes every recorded sample (commands and trainer phrases).
        void DeleteRecordings()
        {
            if (Directory.Exists(AppInfo.SamplesFolder))
                foreach (var f in Directory.GetFiles(AppInfo.SamplesFolder, "*.wav")) TryDelete(f);
            foreach (var cmd in Commands) { matcher.Templates[cmd].Clear(); sampleFiles[cmd].Clear(); }
            matcher.Other.Clear();
            matcher.Recalibrate();
            UpdateSampleLabels();
        }

        void DeleteAllData()
        {
            if (ThemedDialog.Show(this, "Delete all your data?",
                "This removes your voice recordings and resets every setting to its default. It can't be undone.",
                new[] { "Delete everything", "Cancel" }, 1, 0) != 0) return;
            if (live) SetLive(false, "reset");
            if (recordTarget != null) StopRecording(null);
            DeleteRecordings();
            try { if (Directory.Exists(AppInfo.DataFolder)) Directory.Delete(AppInfo.DataFolder, true); } catch { }
            string oldInput = settings.InputDevice;
            settings = new Settings();
            SyncControls();
            if (settings.InputDevice != oldInput) RestartMic();
            heldKey = settings.Key;
            SyncEngine(true);
            UpdateStatus();
            SetHint("All your data was deleted. Settings are back to defaults.");
        }

        // Puts the current settings back into the settings page controls.
        void SyncControls()
        {
            loading = true;
            modeBox.SelectedIndex = settings.UseRecordings ? 0 : 1;
            onBox.Text = settings.OnPhrases;
            offBox.Text = settings.OffPhrases;
            confStep.Set(settings.MinConfidence);
            sensStep.Set(settings.Sensitivity);
            filterToggle.Checked = settings.FilterSpeech;
            keyBox.SelectedItem = settings.Key;
            autoOffStep.Set(settings.AutoOffSeconds);
            soundsToggle.Checked = settings.Sounds;
            askExitToggle.Checked = settings.AskDeleteOnExit;
            RefreshDevices();
            UpdateModePanels();
            loading = false;
            applyTimer.Stop();
        }

        void UpdateSampleLabels()
        {
            foreach (var cmd in Commands)
            {
                int n = sampleFiles[cmd].Count;
                sampleLabels[cmd].Text = string.Format("{0}   {1} sample{2}", cmd == "on" ? "On" : "Off", n, n == 1 ? "" : "s");
                sampleLabels[cmd].ForeColor = n == 0 ? Theme.Off : n < 3 ? Theme.Record : Theme.Live;
            }
        }

        void StartRecording(string cmd)
        {
            if (mic == null) { SetHint("The microphone isn't available. Pick another one under Audio."); return; }
            if (recordTarget != null) StopRecording(null);
            recordTarget = cmd;
            recordButtons[cmd].Text = "Listening…";
            recordButtons[cmd].BackColor = Theme.Record;
            recordButtons[cmd].ForeColor = Theme.Bg;
            SetHint(string.Format("Say your {0} command now, then stay quiet for a moment.", cmd.ToUpperInvariant()));
            recordTimer.Stop();
            recordTimer.Start();
            UpdateStatus();
        }

        void StopRecording(string message)
        {
            recordTimer.Stop();
            if (recordTarget != null)
            {
                recordButtons[recordTarget].Text = "Record";
                recordButtons[recordTarget].BackColor = Theme.Surface;
                recordButtons[recordTarget].ForeColor = Theme.Text;
            }
            recordTarget = null;
            if (message != null) SetHint(message);
            UpdateStatus();
        }

        void SaveSample(string cmd, short[] pcm)
        {
            var features = Features.Compute(pcm);
            if (features.Length < 15) { SetHint("That was too short. Say the command again."); recordTimer.Stop(); recordTimer.Start(); return; }
            Directory.CreateDirectory(AppInfo.SamplesFolder);
            var file = Path.Combine(AppInfo.SamplesFolder, cmd + "_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".wav");
            Wav.Write(file, pcm);
            sampleFiles[cmd].Add(file);
            matcher.Templates[cmd].Add(features);
            while (sampleFiles[cmd].Count > VoiceMatcher.MaxSamples)
            {
                TryDelete(sampleFiles[cmd][0]);
                sampleFiles[cmd].RemoveAt(0);
                matcher.Templates[cmd].RemoveAt(0);
            }
            matcher.Recalibrate();
            UpdateSampleLabels();
            int n = sampleFiles[cmd].Count;
            string msg = string.Format("Saved {0} sample {1} ({2:0.0} s).", cmd.ToUpperInvariant(), n, pcm.Length / (double)MicCapture.Rate);
            if (n < 3) msg += " Record it a few more times.";
            if (modeBox.SelectedIndex != 0) { modeBox.SelectedIndex = 0; msg += " Now using voice samples."; }
            StopRecording(msg);
        }

        void PlayLatest(string cmd)
        {
            if (sampleFiles[cmd].Count == 0) { SetHint("No " + cmd.ToUpperInvariant() + " samples yet."); return; }
            try { WavePlayer.Play(Wav.Read(sampleFiles[cmd].Last()), MicCapture.Rate, OutputIndex()); } catch { }
        }

        void ClearSamples(string cmd)
        {
            if (sampleFiles[cmd].Count == 0) return;
            if (MessageBox.Show(this, "Delete all " + cmd.ToUpperInvariant() + " samples?", AppInfo.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var f in sampleFiles[cmd]) TryDelete(f);
            sampleFiles[cmd].Clear();
            matcher.Templates[cmd].Clear();
            matcher.Recalibrate();
            UpdateSampleLabels();
            SetHint(cmd.ToUpperInvariant() + " samples deleted.");
        }

        static void TryDelete(string file) { try { File.Delete(file); } catch { } }

        // ---------- state ----------

        string ListeningHint()
        {
            if (mic == null) return "No microphone. Choose one in settings.";
            if (!listening) return "Listening is paused";
            if (!settings.UseRecordings)
            {
                var on = Settings.Split(settings.OnPhrases).FirstOrDefault() ?? "mic on";
                var off = Settings.Split(settings.OffPhrases).FirstOrDefault() ?? "mic off";
                return string.Format("Say “{0}” or “{1}”", on, off);
            }
            if (!matcher.Ready) return "Open settings and choose “Train my voice” to get started";
            return "Listening for your voice commands";
        }

        void SetHint(string text)
        {
            view.Hint = text;
            settingsHint.Text = text;
        }

        void SetLive(bool on, string reason)
        {
            if (on == live) return;
            live = on;
            KeySender.Send(heldKey, on);
            lastSpeech = DateTime.Now;
            if (settings.Sounds) WavePlayer.Play(on ? onTone : offTone, 22050, OutputIndex());
            UpdateStatus();
        }

        void SetListening(bool on)
        {
            listening = on;
            if (!on) SetLive(false, "paused");
            SyncEngine(false);
            SetHint(ListeningHint());
            UpdateStatus();
        }

        void UpdateStatus()
        {
            string status;
            if (recordTarget != null) { view.TargetColor = Theme.Record; status = "Recording " + recordTarget.ToUpperInvariant(); view.Glyph = Theme.GlyphMic; }
            else if (!listening) { view.TargetColor = Theme.Paused; status = "Paused"; view.Glyph = Theme.GlyphPause; }
            else if (live) { view.TargetColor = Theme.Live; status = "Live"; view.Glyph = Theme.GlyphMic; }
            else { view.TargetColor = Theme.Off; status = "Mic off"; view.Glyph = Theme.GlyphMicOff; }
            view.Status = status;
            view.Paused = !listening;
            view.Footer = string.Format("Holds {0}  ·  {1}", heldKey, settings.UseRecordings ? "voice samples" : "phrases");
            tray.Text = AppInfo.Name + " - " + status;
        }

        void TestKey(Button button)
        {
            var key = (Keys)keyBox.SelectedItem;
            button.Enabled = false;
            int left = 3;
            var t = new Timer { Interval = 1000 };
            button.Text = "Pressing in 3…";
            t.Tick += delegate {
                left--;
                if (left > 0) { button.Text = "Pressing in " + left + "…"; return; }
                t.Stop();
                t.Dispose();
                KeySender.Send(key, true);
                System.Threading.Thread.Sleep(150);
                KeySender.Send(key, false);
                button.Text = "Press the key in 3 seconds";
                button.Enabled = true;
            };
            t.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (live) KeySender.Send(heldKey, false);
            live = false;
            if (applyTimer.Enabled) { applyTimer.Stop(); ApplySettings(); }
            if (e.CloseReason == CloseReason.UserClosing && settings.AskDeleteOnExit && HasRecordings())
            {
                int choice = ThemedDialog.Show(Visible ? this : null, "Delete your voice recordings?",
                    "Your recorded commands are stored on this PC. Delete them now? You'd need to train again next time.",
                    new[] { "Delete", "Keep", "Keep, don't ask again" }, 1, 0);
                if (choice == 0) DeleteRecordings();
                else if (choice == 2) { settings.AskDeleteOnExit = false; settings.Save(); }
            }
            frameTimer.Stop();
            tray.Visible = false;
            if (mic != null) mic.Dispose();
            if (engine != null) engine.RecognizeAsyncCancel();
            pipe.Close();
            if (engine != null) engine.Dispose();
            base.OnFormClosing(e);
        }

        // Short sine sweep used as the on / off sound.
        static short[] Tone(double fromHz, double toHz)
        {
            const int rate = 22050;
            int samples = rate * 12 / 100;
            var pcm = new short[samples];
            double phase = 0;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / samples;
                phase += 2 * Math.PI * (fromHz + (toHz - fromHz) * t) / rate;
                double envelope = Math.Min(1, Math.Min(t * 20, (1 - t) * 10));
                pcm[i] = (short)(Math.Sin(phase) * envelope * 9000);
            }
            return pcm;
        }
    }
}

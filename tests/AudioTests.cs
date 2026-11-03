// Offline checks for the audio pipeline, driven by synthesized speech:
//   - the spectrum stays flat in silence and rises with speech
//   - phrases fed through AudioPipe reach the Windows recognizer
//   - recorded-sample matching accepts repeats and rejects other phrases
// Build and run with tests\run-tests.ps1.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading;
using VTT;

static class AudioTests
{
    static Random rng = new Random(1);
    static int failures;

    static short[] Say(string text, string voice, int rate)
    {
        var ms = new MemoryStream();
        using (var tts = new SpeechSynthesizer())
        {
            tts.SelectVoice(voice);
            tts.Rate = rate;
            tts.SetOutputToAudioStream(ms, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            tts.Speak(text);
        }
        var raw = ms.ToArray();
        var speech = new short[raw.Length / 2];
        Buffer.BlockCopy(raw, 0, speech, 0, raw.Length);
        return WithNoise(Silence(16000).Concat(speech).Concat(Silence(16000)).ToArray());
    }

    static short[] Silence(int n) { return new short[n]; }

    static short[] WithNoise(short[] a)
    {
        return a.Select(v => (short)Math.Max(-32768, Math.Min(32767, v + (rng.NextDouble() - 0.5) * 120))).ToArray();
    }

    static IEnumerable<short[]> Chunks(short[] a, int size)
    {
        for (int i = 0; i < a.Length; i += size) yield return a.Skip(i).Take(size).ToArray();
    }

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) failures++;
    }

    static List<short[]> Segment(short[] audio)
    {
        var seg = new Segmenter();
        var outs = new List<short[]>();
        seg.Utterance += u => outs.Add(u);
        foreach (var c in Chunks(audio, 400)) seg.Add(c);
        seg.Add(new short[16000]);
        return outs;
    }

    static int Main()
    {
        var voices = new SpeechSynthesizer().GetInstalledVoices().Select(v => v.VoiceInfo.Name).ToList();
        string voice = voices[0];

        // Spectrum: silence flat, speech lights it up.
        var an = new SpectrumAnalyzer();
        var bands = new float[SpectrumAnalyzer.Bands];
        float silentMax = 0, speechMax = 0, silentLevel = 0, speechLevel = 0;
        int chunk = 0;
        foreach (var c in Chunks(WithNoise(Silence(16000 * 6)), 400))
        {
            an.Process(c);
            float l = an.Read(bands);
            if (++chunk <= 40) continue; // first second is calibration
            silentMax = Math.Max(silentMax, bands.Max());
            silentLevel = Math.Max(silentLevel, l);
        }
        foreach (var c in Chunks(Say("testing the voice spectrum", voice, 0), 400)) { an.Process(c); float l = an.Read(bands); speechMax = Math.Max(speechMax, bands.Max()); speechLevel = Math.Max(speechLevel, l); }
        Console.WriteLine("  spectrum peak: silence {0:0.00}, speech {1:0.00}; level: silence {2:0.00}, speech {3:0.00}", silentMax, speechMax, silentLevel, speechLevel);
        Check(silentMax < 0.25 && silentLevel < 0.25, "spectrum and level stay low in silence");
        Check(speechMax > 0.6 && speechLevel > 0.5, "spectrum and level rise with speech");

        // Phrases through AudioPipe into the recognizer, at real-time pace.
        var pipe = new AudioPipe();
        var engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US"));
        engine.SetInputToAudioStream(pipe, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        var gb = new GrammarBuilder(new Choices("mic on", "microphone on", "over and out", "mic off")) { Culture = engine.RecognizerInfo.Culture };
        var cmd = new Grammar(gb) { Name = "commands", Priority = 1 };
        engine.LoadGrammar(cmd);
        engine.LoadGrammar(new DictationGrammar { Priority = 0, Weight = 0.5f });
        var heard = new List<string>();
        engine.SpeechRecognized += (s, e) => { if (e.Result.Grammar == cmd) lock (heard) heard.Add(e.Result.Text); };
        engine.RecognizeAsync(RecognizeMode.Multiple);
        foreach (var phrase in new[] { "microphone on", "nice shot over there", "over and out" })
            foreach (var c in Chunks(Say(phrase, voice, 0), 400)) { pipe.Write(c); Thread.Sleep(25); }
        Thread.Sleep(1500);
        engine.RecognizeAsyncCancel();
        pipe.Close();
        Console.WriteLine("  recognizer heard: " + string.Join(", ", heard));
        Check(heard.Contains("microphone on") && heard.Contains("over and out"), "commands reach the recognizer through AudioPipe");
        Check(heard.Count == 2, "normal speech is not reported as a command");

        // Recorded-sample matching, trained like the voice trainer does: repeats of each command
        // at different speeds plus some ordinary speech.
        var m = new VoiceMatcher();
        foreach (int r in new[] { -2, -1, 0, 1, 2, 0 })
        {
            foreach (var u in Segment(Say("mic on", voice, r))) m.Add("on", u);
            foreach (var u in Segment(Say("over and out", voice, r))) m.Add("off", u);
        }
        foreach (var other in new[] { "Can you hear me okay?", "Let's push up together", "Good game everyone", "Wait for me", "Nice shot", "mic", "over" })
            foreach (var u in Segment(Say(other, voice, 0))) m.Add("other", u);
        m.Recalibrate();
        Check(m.Ready, "matcher calibrated from training data");
        Check(m.EarlyReady, "early cut available for \"over and out\"");
        var cases = new[] {
            Tuple.Create("mic on", 3, "on"), Tuple.Create("mic on", -3, "on"), Tuple.Create("over and out", 3, "off"), Tuple.Create("over and out", -3, "off"),
            Tuple.Create("okay, mic on", 0, "on"), Tuple.Create("alright, over and out", 0, "off"),
            Tuple.Create("mic off", 0, (string)null), Tuple.Create("mic check", 0, (string)null), Tuple.Create("my turn", 0, (string)null),
            Tuple.Create("on my way", 0, (string)null), Tuple.Create("over here", 0, (string)null), Tuple.Create("I'm going to reload now", 0, (string)null),
            Tuple.Create("pick up the ammo on the left side", 0, (string)null) };
        foreach (var t in cases)
        {
            string got = null;
            double on = 0, off = 0;
            foreach (var u in Segment(Say(t.Item1, voice, t.Item2))) got = m.Match(u, 1.0, out on, out off) ?? got;
            Check(got == t.Item3, string.Format("\"{0}\" rate {1} -> {2} (on {3:0.00}, off {4:0.00})", t.Item1, t.Item2, got ?? "nothing", on, off));
        }

        // A command at the end of a long stretch of talking with no pause (over 6 s) is still caught.
        {
            var talk = Say("so I went around the back and there were three of them waiting by the stairs and I think we should regroup now, over and out", voice, 0);
            string got = null;
            double on = 0, off = 0;
            foreach (var u in Segment(talk)) got = m.Match(u, 1.0, out on, out off) ?? got;
            Check(got == "off", string.Format("OFF at the end of {0:0.0} s of talking -> {1} (off {2:0.00})", talk.Length / 16000.0 - 2, got ?? "nothing", off));
        }

        // Early cut, run exactly as the app does: release the key as soon as the start sounds like OFF,
        // take it back if the rest doesn't, and let the full match confirm.
        var early = new[] {
            Tuple.Create("over and out", true), Tuple.Create("over and out", true), Tuple.Create("I'm going to reload now", false),
            Tuple.Create("okay, let's go", false), Tuple.Create("over here, follow me to the left", false), Tuple.Create("on my way", false),
            Tuple.Create("mic on", false) };
        int rate = 0;
        foreach (var t in early)
        {
            var r = SimulateEarlyCut(m, Say(t.Item1, voice, rate));
            rate = rate == 0 ? 2 : 0;
            bool ok = t.Item2 ? r.Item1 >= 0 && r.Item1 <= 0.5 && r.Item3 : !r.Item3 && (r.Item1 < 0 || r.Item2 - r.Item1 <= 0.6);
            string what = r.Item1 < 0 ? "never cut" : string.Format("cut at {0:0.00} s", r.Item1) + (r.Item3 ? ", confirmed OFF" : string.Format(", resumed at {0:0.00} s", r.Item2));
            Check(ok, string.Format("early cut: \"{0}\" -> {1}", t.Item1, what));
        }

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " test(s) failed");
        return failures == 0 ? 0 : 1;
    }

    // Returns (seconds after speech start when the key was released or -1, seconds when it was taken back, confirmed as OFF).
    static Tuple<double, double, bool> SimulateEarlyCut(VoiceMatcher m, short[] audio)
    {
        var seg = new Segmenter();
        bool cut = false, blocked = false, confirmed = false;
        double cutAt = -1, resumedAt = -1;
        int lead = Segmenter.PreRollFrames * Segmenter.Frame;
        seg.Progress += p => {
            if (blocked) return;
            double s = m.EarlyScore(p), at = (p.Length - lead) / 16000.0;
            if (!cut && s < 1) { cut = true; cutAt = at; }
            else if (cut && (s > 1.3 || p.Length > m.LongestOff * 1.3)) { cut = false; blocked = true; resumedAt = at; }
        };
        seg.Utterance += u => {
            double on, off;
            if (cut) { confirmed = m.Match(u, 1.0, out on, out off) == "off"; if (!confirmed) resumedAt = (u.Length - lead) / 16000.0; }
            cut = false; blocked = false;
        };
        foreach (var c in Chunks(audio, 400)) seg.Add(c);
        seg.Add(new short[16000]);
        return Tuple.Create(cutAt, resumedAt, confirmed);
    }
}

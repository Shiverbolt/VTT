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

        // Recorded-sample matching.
        var m = new VoiceMatcher();
        foreach (int r in new[] { -1, 0, 1 })
        {
            foreach (var u in Segment(Say("mic on", voice, r))) m.Templates["on"].Add(Features.Compute(u));
            foreach (var u in Segment(Say("over and out", voice, r))) m.Templates["off"].Add(Features.Compute(u));
        }
        m.Recalibrate();
        var cases = new[] { Tuple.Create("mic on", 2, "on"), Tuple.Create("over and out", -2, "off"),
            Tuple.Create("mic off", 0, (string)null), Tuple.Create("my turn", 0, (string)null), Tuple.Create("on my way", 0, (string)null) };
        foreach (var t in cases)
        {
            string got = null;
            double on = 0, off = 0;
            foreach (var u in Segment(Say(t.Item1, voice, t.Item2))) got = m.Match(Features.Compute(u), 2.0, out on, out off) ?? got;
            Check(got == t.Item3, string.Format("\"{0}\" -> {1} (on {2:0.00}, off {3:0.00})", t.Item1, got ?? "nothing", on, off));
        }

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " test(s) failed");
        return failures == 0 ? 0 : 1;
    }
}

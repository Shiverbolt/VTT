// The on / off sound packs. Every sound is synthesised here from simple waveforms (no audio files, nothing
// to license), except "My own sounds", which plays clips you record in settings.
using System;
using System.IO;
using System.Linq;

namespace VTT
{
    struct Sound
    {
        public short[] Pcm;
        public int Rate;
        public Sound(short[] pcm, int rate) { Pcm = pcm; Rate = rate; }
    }

    static class SoundPacks
    {
        public static readonly string[] Names = { "Classic", "Walkie-talkie", "Sci-fi", "8-bit arcade", "Soft chime", "My own sounds" };
        public const string Custom = "My own sounds";
        const int Rate = 22050;
        static readonly Random rng = new Random(7);

        public static string CustomFile(bool on) { return Path.Combine(AppInfo.DataFolder, "sounds", on ? "on.wav" : "off.wav"); }
        public static bool HasCustom(bool on) { return File.Exists(CustomFile(on)); }

        public static Sound Get(string pack, bool on)
        {
            switch (pack)
            {
                case "Walkie-talkie": return Make(on ? Squelch(0.07, 0.9) : Concat(Sine(1250, 0.075, 0.6), Silence(0.02), Squelch(0.18, 0.7)));
                case "Sci-fi": return Make(on ? SciFi(320, 1500, 0.2) : SciFi(1500, 260, 0.24));
                case "8-bit arcade": return Make(on ? Arpeggio(new[] { 1046.5, 1318.5, 1568.0 }, 0.045) : Arpeggio(new[] { 1568.0, 1318.5, 1046.5 }, 0.045));
                case "Soft chime": return Make(on ? Mix(Bell(659.3, 0.6), Delay(Bell(987.8, 0.7), 0.09)) : Mix(Bell(987.8, 0.6), Delay(Bell(659.3, 0.7), 0.09)));
                case Custom:
                    if (HasCustom(on))
                        try { return new Sound(Wav.Read(CustomFile(on)), MicCapture.Rate); } catch { }
                    return Get("Classic", on);
                default: return Make(Sweep(on ? 660 : 880, on ? 990 : 520, 0.12));
            }
        }

        public static Sound Fanfare()
        {
            var notes = Arpeggio(new[] { 784.0, 1046.5, 1318.5 }, 0.07);
            return Make(Concat(notes, Square(1568.0, 0.28, 0.5)));
        }

        // Tidies a recorded clip for use as a custom sound: at most 1.5 s, soft edges, sensible loudness.
        public static short[] PrepareCustom(short[] pcm)
        {
            var clip = pcm.Take(MicCapture.Rate * 3 / 2).Select(s => (double)s).ToArray();
            int fade = MicCapture.Rate / 100;
            for (int i = 0; i < fade && i < clip.Length; i++) { clip[i] *= i / (double)fade; clip[clip.Length - 1 - i] *= i / (double)fade; }
            double peak = clip.Select(Math.Abs).DefaultIfEmpty(1).Max();
            double gain = peak < 1 ? 1 : Math.Min(4, 14000 / peak);
            return clip.Select(s => (short)Math.Max(-32767, Math.Min(32767, s * gain))).ToArray();
        }

        // ---------- synthesis ----------

        static Sound Make(double[] x)
        {
            double peak = x.Select(Math.Abs).DefaultIfEmpty(1).Max();
            double gain = peak > 0 ? 9500 / peak : 0;
            return new Sound(x.Select(v => (short)(v * gain)).ToArray(), Rate);
        }

        static int N(double seconds) { return (int)(seconds * Rate); }

        // Short attack and release so nothing clicks.
        static double Env(int i, int n, double attack = 0.004, double release = 0.03)
        {
            double t = i / (double)Rate, left = (n - i) / (double)Rate;
            return Math.Min(1, Math.Min(t / attack, left / release));
        }

        static double[] Silence(double seconds) { return new double[N(seconds)]; }

        static double[] Sine(double hz, double seconds, double level)
        {
            int n = N(seconds);
            return Enumerable.Range(0, n).Select(i => Math.Sin(2 * Math.PI * hz * i / Rate) * Env(i, n) * level).ToArray();
        }

        static double[] Square(double hz, double seconds, double level)
        {
            int n = N(seconds);
            return Enumerable.Range(0, n).Select(i => (Math.Sin(2 * Math.PI * hz * i / Rate) >= 0 ? 1 : -1) * Env(i, n, 0.002, 0.06) * level).ToArray();
        }

        static double[] Sweep(double fromHz, double toHz, double seconds)
        {
            int n = N(seconds);
            var x = new double[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                phase += 2 * Math.PI * (fromHz + (toHz - fromHz) * i / n) / Rate;
                x[i] = Math.Sin(phase) * Env(i, n, 0.006, 0.012);
            }
            return x;
        }

        // Radio squelch: band-limited noise with a crackly, fast decay and a click at the start.
        static double[] Squelch(double seconds, double level)
        {
            int n = N(seconds);
            var x = new double[n];
            double lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                double white = rng.NextDouble() * 2 - 1;
                lp += (white - lp) * 0.45;           // low-pass ~ 4 kHz
                hp = 0.92 * (hp + lp - prev);        // high-pass ~ 300 Hz
                prev = lp;
                double decay = Math.Exp(-i / (double)n * 3.2);
                double crackle = 0.75 + 0.25 * Math.Sin(i * 0.013) * Math.Sin(i * 0.0021);
                x[i] = hp * decay * crackle * level * Env(i, n, 0.001, 0.02);
            }
            for (int i = 0; i < Math.Min(n, 40); i++) x[i] += (1 - i / 40.0) * 0.8 * level; // click
            return x;
        }

        static double[] SciFi(double fromHz, double toHz, double seconds)
        {
            int n = N(seconds);
            var x = new double[n];
            double phase = 0, phase2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                double f = fromHz * Math.Pow(toHz / fromHz, t) + Math.Sin(2 * Math.PI * 38 * i / Rate) * 70; // warble
                phase += 2 * Math.PI * f / Rate;
                phase2 += 2 * Math.PI * f * 2.01 / Rate;
                x[i] = (Math.Sin(phase) + 0.3 * Math.Sin(phase2)) * Env(i, n, 0.005, 0.05);
            }
            return x;
        }

        static double[] Arpeggio(double[] notes, double each)
        {
            return notes.Select(hz => Square(hz, each, 0.45)).Aggregate(new double[0], (a, b) => Concat(a, b));
        }

        static double[] Bell(double hz, double seconds)
        {
            int n = N(seconds);
            var partials = new[] { Tuple.Create(1.0, 1.0, 3.5), Tuple.Create(2.76, 0.45, 6.0), Tuple.Create(5.4, 0.2, 9.0) };
            return Enumerable.Range(0, n).Select(i => {
                double t = i / (double)Rate, v = 0;
                foreach (var p in partials) v += Math.Sin(2 * Math.PI * hz * p.Item1 * t) * p.Item2 * Math.Exp(-t * p.Item3);
                return v * Env(i, n, 0.003, 0.05);
            }).ToArray();
        }

        static double[] Concat(params double[][] parts) { return parts.SelectMany(p => p).ToArray(); }
        static double[] Delay(double[] x, double seconds) { return Concat(Silence(seconds), x); }

        static double[] Mix(double[] a, double[] b)
        {
            var x = new double[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < a.Length; i++) x[i] += a[i];
            for (int i = 0; i < b.Length; i++) x[i] += b[i];
            return x;
        }
    }
}

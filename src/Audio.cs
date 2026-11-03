// Audio side of V.T.T?: device listing, microphone capture, playback, the live spectrum,
// splitting the stream into utterances, MFCC features and DTW matching against recorded samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace VTT
{
    static class WinMM
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport; }

        [DllImport("winmm.dll")] public static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int waveInGetDevCaps(IntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr h, uint device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr h);

        [DllImport("winmm.dll")] public static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int waveOutGetDevCaps(IntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr h, uint device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr h);

        public const uint WAVE_MAPPER = 0xFFFFFFFF, CALLBACK_EVENT = 0x50000, WHDR_DONE = 1;
        public static readonly int HdrSize = Marshal.SizeOf(typeof(WAVEHDR));

        public static WAVEFORMATEX Pcm16(int rate)
        {
            return new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = (uint)rate, nAvgBytesPerSec = (uint)rate * 2, nBlockAlign = 2, wBitsPerSample = 16 };
        }

        // -1 means the Windows default device.
        public static uint DeviceId(int index) { return index < 0 ? WAVE_MAPPER : (uint)index; }
    }

    static class AudioDevices
    {
        // Names are limited to 31 characters by the waveIn/waveOut API.
        public static List<string> Inputs()
        {
            var list = new List<string>();
            int n = WinMM.waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WinMM.WAVEINCAPS();
                list.Add(WinMM.waveInGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "Input " + (i + 1));
            }
            return list;
        }

        public static List<string> Outputs()
        {
            var list = new List<string>();
            int n = WinMM.waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WinMM.WAVEOUTCAPS();
                list.Add(WinMM.waveOutGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "Output " + (i + 1));
            }
            return list;
        }
    }

    // Captures 16 kHz mono 16-bit audio from a microphone with the waveIn API.
    class MicCapture : IDisposable
    {
        public const int Rate = 16000;
        const int BufferSamples = 400, BufferCount = 10; // 25 ms buffers keep the spectrum smooth

        public event Action<short[]> Samples; // raised on the capture thread

        readonly int device;
        IntPtr handle;
        IntPtr[] headers = new IntPtr[BufferCount];
        AutoResetEvent signal = new AutoResetEvent(false);
        Thread thread;
        volatile bool running;

        public MicCapture(int device) { this.device = device; }

        public void Start()
        {
            var fmt = WinMM.Pcm16(Rate);
            int err = WinMM.waveInOpen(out handle, WinMM.DeviceId(device), ref fmt, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, WinMM.CALLBACK_EVENT);
            if (err != 0)
            {
                handle = IntPtr.Zero;
                throw new Exception("Could not open the microphone (waveIn error " + err + "). Check that it is connected and allowed in Windows privacy settings.");
            }
            for (int i = 0; i < BufferCount; i++)
            {
                var hdr = new WinMM.WAVEHDR { lpData = Marshal.AllocHGlobal(BufferSamples * 2), dwBufferLength = BufferSamples * 2 };
                headers[i] = Marshal.AllocHGlobal(WinMM.HdrSize);
                Marshal.StructureToPtr(hdr, headers[i], false);
                WinMM.waveInPrepareHeader(handle, headers[i], WinMM.HdrSize);
                WinMM.waveInAddBuffer(handle, headers[i], WinMM.HdrSize);
            }
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "MicCapture" };
            thread.Start();
            WinMM.waveInStart(handle);
        }

        void Loop()
        {
            int next = 0;
            while (running)
            {
                signal.WaitOne(200);
                // Buffers complete in the order they were queued.
                while (running)
                {
                    var hdr = (WinMM.WAVEHDR)Marshal.PtrToStructure(headers[next], typeof(WinMM.WAVEHDR));
                    if ((hdr.dwFlags & WinMM.WHDR_DONE) == 0) break;
                    var data = new short[hdr.dwBytesRecorded / 2];
                    Marshal.Copy(hdr.lpData, data, 0, data.Length);
                    WinMM.waveInAddBuffer(handle, headers[next], WinMM.HdrSize);
                    next = (next + 1) % BufferCount;
                    var handler = Samples;
                    if (handler != null && data.Length > 0)
                    {
                        try { handler(data); } catch { }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            running = false;
            WinMM.waveInReset(handle);
            if (thread != null) thread.Join(1000);
            foreach (var h in headers)
            {
                if (h == IntPtr.Zero) continue;
                var hdr = (WinMM.WAVEHDR)Marshal.PtrToStructure(h, typeof(WinMM.WAVEHDR));
                WinMM.waveInUnprepareHeader(handle, h, WinMM.HdrSize);
                Marshal.FreeHGlobal(hdr.lpData);
                Marshal.FreeHGlobal(h);
            }
            WinMM.waveInClose(handle);
            handle = IntPtr.Zero;
        }
    }

    // Plays 16-bit mono PCM on a chosen output device, in the background.
    static class WavePlayer
    {
        public static void Play(short[] pcm, int rate, int device)
        {
            ThreadPool.QueueUserWorkItem(delegate {
                IntPtr h, hdrPtr = IntPtr.Zero, data = IntPtr.Zero;
                var fmt = WinMM.Pcm16(rate);
                if (WinMM.waveOutOpen(out h, WinMM.DeviceId(device), ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
                try
                {
                    data = Marshal.AllocHGlobal(pcm.Length * 2);
                    Marshal.Copy(pcm, 0, data, pcm.Length);
                    hdrPtr = Marshal.AllocHGlobal(WinMM.HdrSize);
                    Marshal.StructureToPtr(new WinMM.WAVEHDR { lpData = data, dwBufferLength = (uint)pcm.Length * 2 }, hdrPtr, false);
                    WinMM.waveOutPrepareHeader(h, hdrPtr, WinMM.HdrSize);
                    WinMM.waveOutWrite(h, hdrPtr, WinMM.HdrSize);
                    var deadline = DateTime.Now.AddSeconds(pcm.Length / (double)rate + 2);
                    while (DateTime.Now < deadline)
                    {
                        var hdr = (WinMM.WAVEHDR)Marshal.PtrToStructure(hdrPtr, typeof(WinMM.WAVEHDR));
                        if ((hdr.dwFlags & WinMM.WHDR_DONE) != 0) break;
                        Thread.Sleep(15);
                    }
                    WinMM.waveOutUnprepareHeader(h, hdrPtr, WinMM.HdrSize);
                }
                finally
                {
                    WinMM.waveOutClose(h);
                    if (hdrPtr != IntPtr.Zero) Marshal.FreeHGlobal(hdrPtr);
                    if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                }
            });
        }
    }

    // A blocking stream that hands captured audio to the Windows speech recognizer,
    // so both detection modes listen to the same chosen microphone.
    class AudioPipe : Stream
    {
        const int Capacity = MicCapture.Rate * 2 * 2; // 2 s; older audio is dropped
        readonly Queue<byte> buffer = new Queue<byte>();
        bool closed;

        public void Write(short[] samples)
        {
            lock (buffer)
            {
                foreach (var s in samples) { buffer.Enqueue((byte)s); buffer.Enqueue((byte)(s >> 8)); }
                while (buffer.Count > Capacity) { buffer.Dequeue(); buffer.Dequeue(); }
                Monitor.PulseAll(buffer);
            }
        }

        // The recognizer treats a short read as the end of the audio, so this always fills the whole
        // request: it waits for the microphone, and pads with silence if the microphone goes quiet.
        public override int Read(byte[] dest, int offset, int count)
        {
            lock (buffer)
            {
                while (buffer.Count < count && !closed)
                    if (!Monitor.Wait(buffer, 300)) break;
                if (closed) return 0;
                int take = Math.Min(count, buffer.Count);
                for (int i = 0; i < take; i++) dest[offset + i] = buffer.Dequeue();
                Array.Clear(dest, offset + take, count - take);
                return count;
            }
        }

        public override void Close()
        {
            lock (buffer) { closed = true; Monitor.PulseAll(buffer); }
            base.Close();
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return -1; } }
        public override long Position { get { return 0; } set { } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { return 0; } // the recognizer seeks once at start; must not throw
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
    }

    // Turns microphone audio into band levels (0..1) for the on-screen voice spectrum.
    class SpectrumAnalyzer
    {
        public const int Bands = 32;
        const int N = 512;
        readonly short[] ring = new short[N];
        int ringPos, filled;
        readonly double[] floor = Enumerable.Repeat(999.0, Bands).ToArray(); // first frame sets the real floor
        readonly double[] power = new double[Bands];
        readonly int[] edges;
        readonly double[] hann = Enumerable.Range(0, N).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1))).ToArray();
        readonly float[] latest = new float[Bands];
        double levelFloor = 999, level;

        public SpectrumAnalyzer()
        {
            // Log-spaced bands from 90 Hz to 6 kHz.
            edges = Enumerable.Range(0, Bands + 1)
                .Select(i => (int)Math.Round(90 * Math.Pow(6000.0 / 90, i / (double)Bands) * N / MicCapture.Rate))
                .ToArray();
            for (int i = 1; i <= Bands; i++) edges[i] = Math.Max(edges[i], edges[i - 1] + 1);
        }

        public void Process(short[] samples)
        {
            foreach (var s in samples) { ring[ringPos] = s; ringPos = (ringPos + 1) % N; }
            filled += samples.Length;
            if (filled < N) return; // wait for a full window so the noise floor starts from real audio
            var re = new double[N];
            var im = new double[N];
            double sum = 0;
            for (int i = 0; i < N; i++)
            {
                double v = ring[(ringPos + i) % N];
                sum += v * v;
                re[i] = v * hann[i];
            }
            Features.Fft(re, im);

            // Overall loudness against a floor that falls quickly and rises slowly (~1 dB/s).
            double ldb = 10 * Math.Log10(sum / N + 1);
            levelFloor = levelFloor > 900 ? ldb : ldb < levelFloor ? levelFloor + (ldb - levelFloor) * 0.2 : levelFloor + 0.03;
            double newLevel = Math.Max(0, Math.Min(1, (ldb - levelFloor - 6) / 30));

            var bands = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                double p = 0;
                for (int k = edges[b]; k < edges[b + 1]; k++) p += re[k] * re[k] + im[k] * im[k];
                p /= edges[b + 1] - edges[b];
                // Smoothing over time takes the random flicker out of background noise.
                power[b] = power[b] == 0 ? p : power[b] * 0.5 + p * 0.5;
                double db = 10 * Math.Log10(power[b] + 1);
                // Each band learns its average background level, but only while you are quiet,
                // so silence reads as flat and speech stands out.
                if (floor[b] > 900) floor[b] = db;
                else if (newLevel < 0.1) floor[b] += (db - floor[b]) * (filled < MicCapture.Rate ? 0.3 : 0.05); // settle fast in the first second
                bands[b] = (float)Math.Max(0, Math.Min(1, (db - floor[b] - 8) / 24));
            }
            lock (latest)
            {
                Array.Copy(bands, latest, Bands);
                level = newLevel;
            }
        }

        public float Read(float[] dest)
        {
            lock (latest)
            {
                Array.Copy(latest, dest, Bands);
                return (float)level;
            }
        }
    }

    // Splits a continuous audio stream into utterances: bursts of sound with a pause before and after.
    // While an utterance is in progress it also reports how it starts (Progress), so the OFF command can be
    // recognised from its first syllable instead of only after it has been said in full.
    class Segmenter
    {
        public const int Frame = 160;              // 10 ms
        public const int PreRollFrames = 25;       // every utterance starts with 0.25 s of lead-in
        public const int EarlyFrames = 30;         // first Progress 0.3 s after speech starts...
        const int EarlyEvery = 10, EarlyUntil = 300; // ...then every 0.1 s, for up to 3 s
        const int StartFrames = 4, EndSilenceFrames = 35, KeepTailFrames = 10;
        const int MaxFrames = 600, MinFrames = 25; // utterances keep at most their last 6 s
        const int AdaptAfterFrames = 800;          // after 8 s without a pause, assume the background got louder

        public event Action<short[]> Utterance, Progress;
        public event Action SpeechActivity;

        List<short> pending = new List<short>();
        Queue<short[]> preRoll = new Queue<short[]>();
        List<short[]> current = new List<short[]>();
        double noise = 200;
        bool inSpeech;
        int loudRun, silenceRun, sinceOnset;

        public bool InSpeech { get { return inSpeech; } }

        public void Add(short[] samples)
        {
            pending.AddRange(samples);
            int offset = 0;
            while (pending.Count - offset >= Frame)
            {
                var f = new short[Frame];
                pending.CopyTo(offset, f, 0, Frame);
                offset += Frame;
                ProcessFrame(f);
            }
            pending.RemoveRange(0, offset);
        }

        void ProcessFrame(short[] f)
        {
            double sum = 0;
            foreach (var s in f) sum += (double)s * s;
            double rms = Math.Sqrt(sum / f.Length);
            bool loud = rms > Math.Max(noise * 3.5, 150);

            if (!inSpeech || sinceOnset > AdaptAfterFrames)
            {
                // Noise floor follows quiet levels quickly and loud levels slowly.
                noise = rms < noise ? noise * 0.9 + rms * 0.1 : noise * 0.995 + rms * 0.005;
            }

            if (!inSpeech)
            {
                preRoll.Enqueue(f);
                if (preRoll.Count > PreRollFrames) preRoll.Dequeue();
                loudRun = loud ? loudRun + 1 : 0;
                if (loudRun >= StartFrames)
                {
                    inSpeech = true;
                    silenceRun = 0;
                    sinceOnset = 0;
                    current = new List<short[]>(preRoll);
                    preRoll.Clear();
                }
                return;
            }

            sinceOnset++;
            if (loud)
            {
                silenceRun = 0;
                if (SpeechActivity != null) SpeechActivity();
            }
            else silenceRun++;

            current.Add(f);
            // Long stretches of talking keep only their most recent part, where an OFF command would be.
            if (current.Count > MaxFrames + PreRollFrames + EndSilenceFrames) current.RemoveAt(0);

            if (Progress != null && sinceOnset >= EarlyFrames && sinceOnset <= EarlyUntil &&
                (sinceOnset - EarlyFrames) % EarlyEvery == 0 && silenceRun < EndSilenceFrames)
                Progress(current.SelectMany(x => x).ToArray());

            if (silenceRun >= EndSilenceFrames)
            {
                inSpeech = false;
                loudRun = 0;
                int keep = current.Count - EndSilenceFrames + KeepTailFrames;
                if (keep - PreRollFrames >= MinFrames && Utterance != null)
                    Utterance(current.Take(keep).SelectMany(x => x).ToArray());
                current = new List<short[]>();
            }
        }
    }

    // MFCC features per 10 ms frame: 12 mean-normalised cepstra plus their deltas (how they change),
    // with leading / trailing silence trimmed.
    static class Features
    {
        const int Win = 400, Hop = 160, NFft = 512, NMel = 26, NCep = 12;
        public const int Dims = NCep * 2;
        static readonly double[] Hamming = Enumerable.Range(0, Win).Select(i => 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (Win - 1))).ToArray();
        static readonly double[][] MelFilters = BuildMel();
        static readonly double[,] Dct = BuildDct();

        static double Mel(double hz) { return 2595 * Math.Log10(1 + hz / 700); }
        static double Hz(double mel) { return 700 * (Math.Pow(10, mel / 2595) - 1); }

        static double[][] BuildMel()
        {
            double lo = Mel(80), hi = Mel(7600);
            var bins = Enumerable.Range(0, NMel + 2).Select(i => Hz(lo + (hi - lo) * i / (NMel + 1)) * NFft / MicCapture.Rate).ToArray();
            var filters = new double[NMel][];
            for (int m = 0; m < NMel; m++)
            {
                filters[m] = new double[NFft / 2 + 1];
                for (int k = 0; k <= NFft / 2; k++)
                {
                    if (k > bins[m] && k <= bins[m + 1]) filters[m][k] = (k - bins[m]) / (bins[m + 1] - bins[m]);
                    else if (k > bins[m + 1] && k < bins[m + 2]) filters[m][k] = (bins[m + 2] - k) / (bins[m + 2] - bins[m + 1]);
                }
            }
            return filters;
        }

        static double[,] BuildDct()
        {
            var d = new double[NCep, NMel];
            for (int c = 0; c < NCep; c++)
                for (int m = 0; m < NMel; m++)
                    d[c, m] = Math.Cos(Math.PI * (c + 1) * (m + 0.5) / NMel);
            return d;
        }

        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        public static float[][] Compute(short[] pcm)
        {
            int frames = (pcm.Length - Win) / Hop + 1;
            if (frames < 1) return new float[0][];
            var x = new double[pcm.Length];
            x[0] = pcm[0];
            for (int i = 1; i < pcm.Length; i++) x[i] = pcm[i] - 0.97 * pcm[i - 1];

            var energy = new double[frames];
            var logMel = new double[frames][];
            var re = new double[NFft];
            var im = new double[NFft];
            var power = new double[NFft / 2 + 1];
            for (int f = 0; f < frames; f++)
            {
                Array.Clear(re, 0, NFft);
                Array.Clear(im, 0, NFft);
                for (int i = 0; i < Win; i++) re[i] = x[f * Hop + i] * Hamming[i];
                Fft(re, im);
                double total = 0;
                for (int k = 0; k <= NFft / 2; k++) { power[k] = re[k] * re[k] + im[k] * im[k]; total += power[k]; }
                energy[f] = Math.Log(total + 1e-9);
                logMel[f] = new double[NMel];
                for (int m = 0; m < NMel; m++)
                {
                    double s = 0;
                    var filt = MelFilters[m];
                    for (int k = 0; k <= NFft / 2; k++) s += filt[k] * power[k];
                    logMel[f][m] = Math.Log(s + 1e-9);
                }
            }

            // Trim frames more than ~25 dB below the loudest one (leading/trailing silence).
            double max = energy.Max();
            int first = Array.FindIndex(energy, e => e > max - 5.75), last = Array.FindLastIndex(energy, e => e > max - 5.75);
            int count = last - first + 1;
            var result = new float[count][];
            var mean = new double[NCep];
            for (int f = 0; f < count; f++)
            {
                result[f] = new float[Dims];
                for (int c = 0; c < NCep; c++)
                {
                    double s = 0;
                    for (int m = 0; m < NMel; m++) s += Dct[c, m] * logMel[first + f][m];
                    result[f][c] = (float)s;
                    mean[c] += s / count;
                }
            }
            foreach (var r in result)
                for (int c = 0; c < NCep; c++) r[c] -= (float)mean[c];
            for (int f = 0; f < count; f++)
            {
                var before = result[Math.Max(0, f - 1)];
                var after = result[Math.Min(count - 1, f + 1)];
                for (int c = 0; c < NCep; c++) result[f][NCep + c] = (after[c] - before[c]) * 0.5f;
            }
            return result;
        }

        static double FrameDistance(float[] a, float[] b)
        {
            double d = 0;
            for (int c = 0; c < a.Length; c++) { double t = a[c] - b[c]; d += t * t; }
            return Math.Sqrt(d);
        }

        // Best match of the whole template anywhere inside a longer utterance (subsequence DTW),
        // so "uh, mic on" or a command at the end of a sentence still counts.
        public static double SubsequenceDistance(float[][] utterance, float[][] template)
        {
            int n = utterance.Length, m = template.Length;
            if (m < 5 || n < 5) return double.PositiveInfinity;
            var prev = new double[n + 1]; // row 0 is free: the match may start anywhere
            var cur = new double[n + 1];
            for (int i = 1; i <= m; i++)
            {
                cur[0] = double.PositiveInfinity;
                var ti = template[i - 1];
                for (int j = 1; j <= n; j++)
                    cur[j] = FrameDistance(ti, utterance[j - 1]) + Math.Min(prev[j - 1], Math.Min(prev[j], cur[j - 1]));
                var swap = prev; prev = cur; cur = swap;
            }
            double best = double.PositiveInfinity;
            for (int j = 1; j <= n; j++) best = Math.Min(best, prev[j]);
            return best / (2 * m);
        }

        // Dynamic time warping distance, normalised by path length. Very different lengths never match.
        public static double Distance(float[][] a, float[][] b)
        {
            int n = a.Length, m = b.Length;
            if (n < 5 || m < 5 || n > m * 2.2 || m > n * 2.2) return double.PositiveInfinity;
            var prev = new double[m + 1];
            var cur = new double[m + 1];
            for (int j = 1; j <= m; j++) prev[j] = double.PositiveInfinity;
            prev[0] = 0;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = double.PositiveInfinity;
                var ai = a[i - 1];
                for (int j = 1; j <= m; j++)
                    cur[j] = FrameDistance(ai, b[j - 1]) + Math.Min(prev[j - 1], Math.Min(prev[j], cur[j - 1]));
                var swap = prev; prev = cur; cur = swap;
            }
            return prev[m] / (n + m);
        }
    }

    // Holds the recorded samples for each command, plus "other speech" samples from the voice trainer,
    // and decides which command (if any) an utterance matches.
    class VoiceMatcher
    {
        public const int MaxSamples = 10, MaxOther = 20;
        // How much audio the early OFF check first looks at: the lead-in plus 0.3 s of speech.
        public const int EarlySamples = (Segmenter.PreRollFrames + Segmenter.EarlyFrames) * Segmenter.Frame;

        readonly Dictionary<string, List<short[]>> pcm = new Dictionary<string, List<short[]>> {
            { "on", new List<short[]>() }, { "off", new List<short[]>() }, { "other", new List<short[]>() }
        };
        readonly Dictionary<string, List<float[][]>> feats = new Dictionary<string, List<float[][]>> {
            { "on", new List<float[][]>() }, { "off", new List<float[][]>() }, { "other", new List<float[][]>() }
        };
        readonly Dictionary<string, double> threshold = new Dictionary<string, double>();
        readonly Dictionary<string, float[][]> prefixCache = new Dictionary<string, float[][]>();
        double earlyThreshold = double.NaN;

        public bool Ready { get { return threshold.ContainsKey("on") && threshold.ContainsKey("off"); } }
        public bool EarlyReady { get { return IsFinite(earlyThreshold); } }
        public int Count(string kind) { return pcm[kind].Count; }
        public int LongestOff { get { return pcm["off"].Count == 0 ? 0 : pcm["off"].Max(p => p.Length); } }
        int LongestCommand { get { return pcm["on"].Concat(pcm["off"]).Select(p => p.Length).DefaultIfEmpty(0).Max(); } }

        public void Add(string kind, short[] samples) { pcm[kind].Add(samples); feats[kind].Add(Features.Compute(samples)); }
        public void RemoveOldest(string kind) { pcm[kind].RemoveAt(0); feats[kind].RemoveAt(0); }
        public void Clear(string kind) { pcm[kind].Clear(); feats[kind].Clear(); }
        public void ClearAll() { foreach (var k in pcm.Keys) Clear(k); }

        // The ways a sound is compared with the commands: as a whole, and for long stretches of talking also
        // just its start and just its end (each re-normalised on its own), where a command usually sits.
        List<float[][]> Views(short[] samples)
        {
            var views = new List<float[][]> { Features.Compute(samples) };
            int window = (int)(LongestCommand * 1.5) + MicCapture.Rate / 5;
            if (LongestCommand > 0 && samples.Length > window + MicCapture.Rate / 10)
            {
                views.Add(Features.Compute(samples.Skip(samples.Length - window).ToArray()));
                views.Add(Features.Compute(samples.Take(window).ToArray()));
            }
            return views;
        }

        static double BestOver(List<float[][]> views, List<float[][]> templates)
        {
            double best = double.PositiveInfinity;
            foreach (var v in views) best = Math.Min(best, Best(v, templates, -1));
            return best;
        }

        // Distance from an utterance to a template; a much longer utterance is searched for the template inside it.
        public static double Dist(float[][] utterance, float[][] template)
        {
            return utterance.Length > template.Length * 1.4 ? Features.SubsequenceDistance(utterance, template) : Features.Distance(utterance, template);
        }

        static double Best(float[][] f, List<float[][]> templates, int skip)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < templates.Count; i++)
                if (i != skip) best = Math.Min(best, Dist(f, templates[i]));
            return best;
        }

        // Learns, per command, the distance below which a sound counts as that command: between how close
        // your repeats are to each other and how close everything else (other speech, the other command) gets.
        public void Recalibrate()
        {
            threshold.Clear();
            prefixCache.Clear();
            var views = new Dictionary<short[], List<float[][]>>();
            Func<short[], List<float[][]>> viewsOf = s => { List<float[][]> v; if (!views.TryGetValue(s, out v)) views[s] = v = Views(s); return v; };
            foreach (var cmd in new[] { "on", "off" })
            {
                var own = feats[cmd];
                if (own.Count == 0) continue;
                var pos = new List<double>();
                if (own.Count >= 2)
                    for (int i = 0; i < own.Count; i++) pos.Add(Best(own[i], own, i)); // leave-one-out
                var neg = new List<double>();
                foreach (var n in pcm["other"].Concat(pcm[cmd == "on" ? "off" : "on"]))
                    neg.Add(BestOver(viewsOf(n), own));
                double t = PickThreshold(pos.Where(IsFinite).ToList(), neg.Where(IsFinite).ToList(), 0.6);
                if (IsFinite(t)) threshold[cmd] = t;
            }
            CalibrateEarly();
        }

        // The early cut only switches on when the start of your OFF command is clearly unlike the start of
        // everything else you said in training; a false cut would clip your speech, so it stays strict.
        void CalibrateEarly()
        {
            earlyThreshold = double.NaN;
            var offs = pcm["off"];
            var others = pcm["other"].Concat(pcm["on"]).ToList();
            if (offs.Count < 3 || others.Count < 3) return;
            var offPrefixes = Enumerable.Range(0, offs.Count).Select(i => PrefixFeatures(i, EarlySamples)).ToList();
            var pos = new List<double>();
            for (int i = 0; i < offPrefixes.Count; i++) pos.Add(Best(offPrefixes[i], offPrefixes, i));
            var neg = others.Select(o => Best(Features.Compute(o.Take(EarlySamples).ToArray()), offPrefixes, -1)).ToList();
            pos = pos.Where(IsFinite).ToList();
            neg = neg.Where(IsFinite).ToList();
            if (pos.Count < 2 || neg.Count == 0 || pos.Max() >= neg.Min()) return;
            earlyThreshold = pos.Max() + (neg.Min() - pos.Max()) * 0.4;
        }

        float[][] PrefixFeatures(int offIndex, int length)
        {
            var src = pcm["off"][offIndex];
            int n = Math.Min(length, src.Length);
            string key = offIndex + ":" + n;
            float[][] f;
            if (!prefixCache.TryGetValue(key, out f)) prefixCache[key] = f = Features.Compute(src.Take(n).ToArray());
            return f;
        }

        // How much the start of a sound in progress resembles the start of your OFF command (under 1 = it does).
        public double EarlyScore(short[] soFar)
        {
            if (!EarlyReady) return double.PositiveInfinity;
            var f = Features.Compute(soFar);
            double best = double.PositiveInfinity;
            for (int i = 0; i < pcm["off"].Count; i++) best = Math.Min(best, Features.Distance(f, PrefixFeatures(i, soFar.Length)));
            return best / earlyThreshold;
        }

        static bool IsFinite(double d) { return !double.IsInfinity(d) && !double.IsNaN(d); }

        // lean: where to cut between your repeats (0) and everything else (1) when they don't overlap.
        static double PickThreshold(List<double> pos, List<double> neg, double lean)
        {
            if (pos.Count == 0) return neg.Count > 0 ? neg.Min() * 0.6 : double.NaN; // one sample: stay strict
            double maxPos = pos.Max();
            if (neg.Count == 0) return maxPos * 1.4;
            double minNeg = neg.Min();
            // Repeats are judged against one fewer sample than real use, so lean a little toward accepting.
            if (maxPos < minNeg) return maxPos + (minNeg - maxPos) * lean;
            // Overlap: pick the cut with the fewest mistakes, counting a false trigger as worse than a miss.
            var cuts = pos.Concat(neg).OrderBy(x => x).ToList();
            double best = cuts[0] * 0.99, bestCost = pos.Count;
            for (int i = 0; i < cuts.Count; i++)
            {
                double t = i + 1 < cuts.Count ? (cuts[i] + cuts[i + 1]) / 2 : cuts[i] * 1.01;
                double cost = pos.Count(p => p > t) + 2.0 * neg.Count(n => n <= t);
                if (cost < bestCost) { bestCost = cost; best = t; }
            }
            return best;
        }

        // A score is the distance to the closest sample divided by the learned threshold, so under 1 means
        // "sounds like this command". Sensitivity scales that cut-off. Returns "on", "off" or null.
        public string Match(short[] samples, double sensitivity, out double onScore, out double offScore)
        {
            var views = Views(samples);
            onScore = Score("on", views);
            offScore = Score("off", views);
            string best = onScore <= offScore ? "on" : "off";
            double bestScore = Math.Min(onScore, offScore), otherScore = Math.Max(onScore, offScore);
            if (bestScore > sensitivity) return null;
            if (otherScore - bestScore < 0.1) return null; // too close to call
            return best;
        }

        double Score(string cmd, List<float[][]> views)
        {
            double t;
            if (!threshold.TryGetValue(cmd, out t)) return double.PositiveInfinity;
            return BestOver(views, feats[cmd]) / t;
        }
    }

    static class Wav
    {
        public static void Write(string path, short[] pcm)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVEfmt ".ToCharArray());
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(MicCapture.Rate); w.Write(MicCapture.Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(pcm.Length * 2);
                foreach (var s in pcm) w.Write(s);
            }
        }

        // Reads a 16-bit mono PCM WAV (as written above).
        public static short[] Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "data")
                {
                    size = Math.Min(size, bytes.Length - pos - 8);
                    var pcm = new short[size / 2];
                    Buffer.BlockCopy(bytes, pos + 8, pcm, 0, pcm.Length * 2);
                    return pcm;
                }
                pos += 8 + size + (size & 1);
            }
            return new short[0];
        }
    }
}

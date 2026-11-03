// V.T.T? - voice-activated push-to-talk.
// Say an "on" command and the app holds your push-to-talk key down; say an "off" command and it releases it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("V.T.T? - voice push-to-talk")]
[assembly: AssemblyDescription("Voice-activated push-to-talk: say a command to hold your push-to-talk key, say another to release it.")]
[assembly: AssemblyProduct("V.T.T?")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Shiverbolt. MIT License.")]
[assembly: AssemblyVersion("1.0.0.4")]
[assembly: AssemblyFileVersion("1.0.0.4")]
[assembly: AssemblyInformationalVersion("1.0.0-beta.4")]

namespace VTT
{
    static class AppInfo
    {
        public const string Name = "V.T.T?";
        public const string Version = "1.0 beta 4";
        public const string RepoUrl = "https://github.com/Shiverbolt/VTT";
        public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VTT");
        public static readonly string SamplesFolder = Path.Combine(DataFolder, "samples");
    }

    static class KeySender
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;

        static readonly HashSet<Keys> Extended = new HashSet<Keys> {
            Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
            Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.RControlKey, Keys.RMenu, Keys.Divide, Keys.NumLock
        };

        public static void Send(Keys key, bool down)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.ki.wVk = (ushort)key;
            input.ki.wScan = (ushort)MapVirtualKey((uint)key, 0);
            input.ki.dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (Extended.Contains(key) ? KEYEVENTF_EXTENDEDKEY : 0);
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    class Settings
    {
        public bool UseRecordings = false;
        public string OnPhrases = "mic on, microphone on";
        public string OffPhrases = "over and out, mic off";
        public Keys Key = Keys.F13;
        public int MinConfidence = 60;
        public int Sensitivity = 100; // percent of the trained cut-off
        public int AutoOffSeconds = 0;
        public bool Sounds = true;
        public bool FilterSpeech = true;
        public bool AskDeleteOnExit = true;
        public bool EarlyCut = true;     // release the key as soon as you start saying the OFF command
        public bool SpeechAssist = true; // Windows speech recognition confirms borderline voice-sample matches
        public string SoundPack = "Classic", OrbStyle = "Bars", Palette = "Classic";
        public bool ShowIndicator = false;
        public int IndicatorX = -100000, IndicatorY = -100000; // unset: top-right corner
        public string InputDevice = "", OutputDevice = ""; // empty = Windows default

        public static string FilePath { get { return Path.Combine(AppInfo.DataFolder, "settings.txt"); } }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) return s;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "mode": s.UseRecordings = v == "recordings"; break;
                        case "on": s.OnPhrases = v; break;
                        case "off": s.OffPhrases = v; break;
                        case "key": s.Key = (Keys)Enum.Parse(typeof(Keys), v); break;
                        case "confidence": s.MinConfidence = int.Parse(v); break;
                        case "sensitivity": s.Sensitivity = int.Parse(v); break;
                        case "askonexit": s.AskDeleteOnExit = bool.Parse(v); break;
                        case "earlycut": s.EarlyCut = bool.Parse(v); break;
                        case "speechassist": s.SpeechAssist = bool.Parse(v); break;
                        case "soundpack": s.SoundPack = v; break;
                        case "orbstyle": s.OrbStyle = v; break;
                        case "palette": s.Palette = v; break;
                        case "indicator": s.ShowIndicator = bool.Parse(v); break;
                        case "indicatorx": s.IndicatorX = int.Parse(v); break;
                        case "indicatory": s.IndicatorY = int.Parse(v); break;
                        case "autooff": s.AutoOffSeconds = int.Parse(v); break;
                        case "sounds": s.Sounds = bool.Parse(v); break;
                        case "filter": s.FilterSpeech = bool.Parse(v); break;
                        case "input": s.InputDevice = v; break;
                        case "output": s.OutputDevice = v; break;
                    }
                }
                catch { }
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataFolder);
                File.WriteAllLines(FilePath, new[] {
                    "mode=" + (UseRecordings ? "recordings" : "phrases"), "on=" + OnPhrases, "off=" + OffPhrases,
                    "key=" + Key, "confidence=" + MinConfidence, "sensitivity=" + Sensitivity,
                    "autooff=" + AutoOffSeconds, "sounds=" + Sounds, "filter=" + FilterSpeech, "askonexit=" + AskDeleteOnExit,
                    "earlycut=" + EarlyCut, "speechassist=" + SpeechAssist,
                    "soundpack=" + SoundPack, "orbstyle=" + OrbStyle, "palette=" + Palette,
                    "indicator=" + ShowIndicator, "indicatorx=" + IndicatorX, "indicatory=" + IndicatorY,
                    "input=" + InputDevice, "output=" + OutputDevice });
            }
            catch { }
        }

        public static List<string> Split(string phrases)
        {
            return phrases.Split(',').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).Distinct().ToList();
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (var single = new Mutex(true, "VTT-voice-push-to-talk", out first))
            {
                if (!first)
                {
                    MessageBox.Show(AppInfo.Name + " is already running - look for it in the system tray.", AppInfo.Name);
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // --tray starts hidden in the system tray (used by "Start with Windows").
                Application.Run(new MainForm(args.Contains("--tray")));
            }
        }
    }
}

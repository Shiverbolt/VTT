# V.T.T?

**Voice-activated push-to-talk for Windows.** Say a command and V.T.T? holds your push-to-talk key down for you; say another and it lets go. No hands, no hotkeys, no always-open mic.

<p align="center">
  <img src="docs/screenshot-off.png" width="260" alt="Mic off">
  <img src="docs/screenshot-live.png" width="260" alt="Live">
  <img src="docs/screenshot-settings.png" width="260" alt="Settings">
</p>

> **1.0 beta 3** - works, but expect rough edges. Please [open an issue](https://github.com/Shiverbolt/VTT/issues) if something misbehaves.

## Features

- **Your own voice commands** - say anything (any language, any word) as the ON and OFF commands. A 2-minute **voice trainer** learns your voice and what to ignore, so other people and game audio rarely trigger it.
- **Works mid-sentence** - "okay, mic on" counts, and so does "…let's regroup, over and out" at the end of a long stretch of talking.
- **Your OFF command stays (mostly) private** - V.T.T? recognises it from its first syllable and lets go of the key right away, so the other side hears about 0.3 s of it instead of the whole phrase. If it was a false alarm, the mic comes back within a fraction of a second.
- **Double-checked by speech recognition** - the words of your commands are also listened for, so borderline matches still count.
- **Or spoken phrases** - type phrases like `mic on` / `over and out` and let the built-in Windows speech recognizer listen for them.
- **Works with anything that has push-to-talk** - Discord, TeamSpeak, Mumble, OBS, games. V.T.T? simply holds the key you choose (default **F13**, a key no keyboard has, so it never clashes with typing).
- **Live voice spectrum** around an animated orb, so you can see it hearing you.
- **Pick your microphone and sound output**, optional on/off sounds, auto-off after silence, tray icon, start with Windows.
- **Private and offline** - audio never leaves your PC. Delete your recordings any time from settings, and V.T.T? offers to delete them when you quit.
- **Proper installer** - choose the install folder, shortcuts and startup; uninstall from Windows *Apps & features*.

## Get started

1. Download `VTT-Setup-1.0.0-beta.3.exe` from the [latest release](https://github.com/Shiverbolt/VTT/releases) and run it. The default folder (`%LOCALAPPDATA%\Programs\VTT`) needs no admin rights; pick another with **Browse**.
   Prefer no installer? Download the portable `VTT.exe` instead and just run it.
2. In Discord (or your app), switch to **Push to Talk** and click the keybind box.
3. In V.T.T?, open **Settings** (gear icon) and click **Press the key in 3 seconds** - your app records the key.
4. Click **Train my voice**. Say your ON and OFF commands and the everyday phrases it shows you; a short test at the end shows how well it works.
5. Close settings. Say your ON command - the orb turns green and your mic is live.

Click the orb to toggle manually, and the pause button to stop listening altogether.

## Tuning

After each sound, the line under the status shows how close it was to each command (e.g. `on 0.62 · off 3.10 · needs under 1.00`). Under 1 means "sounds like that command".

- Real commands missed? Raise **Sensitivity**, or train again in your usual room with your usual mic.
- Random sounds triggering it? Lower **Sensitivity**.
- **Early cut** needs an OFF command whose *start* sounds unlike your everyday speech (e.g. "over and out" rather than "okay stop"); the trainer tells you if yours qualifies. Rather not say an OFF command at all? Set **Auto-off after silence** and just stop talking.

## Privacy

Recordings and settings live only in `%APPDATA%\VTT` (`samples\*.wav`, `settings.txt`). Nothing is uploaded.

- **Settings → Privacy → Delete all my data** removes them any time.
- When you quit, V.T.T? asks whether to delete your recordings (*Delete*, *Keep*, or *Keep, don't ask again*; switch the question back on in settings).
- The uninstaller can delete them too.

## About the download (Windows SmartScreen)

The installer and `VTT.exe` are signed with a **self-signed** certificate (published as `VTT-codesign.cer` with each release), so Windows still shows *"Unknown publisher"* and SmartScreen may warn on first run (*More info → Run anyway*). The signature lets you confirm the files weren't altered; compare their SHA-256 with `SHA256SUMS.txt` in the release:

```powershell
Get-FileHash .\VTT-Setup-1.0.0-beta.3.exe -Algorithm SHA256
```

Some games with anti-cheat ignore simulated key presses; in those, use the game's open-mic mode and V.T.T? with your voice chat app instead.

## Installer options

`VTT-Setup-1.0.0-beta.3.exe` also runs unattended:

```powershell
.\VTT-Setup-1.0.0-beta.3.exe /S /dir="D:\Apps\VTT" /desktop=0 /startmenu=1 /startup=0 /run=1
& "$env:LOCALAPPDATA\Programs\VTT\Uninstall VTT.exe" /uninstall /S /removedata=1
```

## Build from source

No SDK required - Windows ships the C# compiler.

```powershell
.\build.ps1             # -> dist\VTT.exe
.\tests\run-tests.ps1   # offline audio tests using synthesized speech
.\tools\package.ps1     # signed VTT.exe + installer + SHA256SUMS.txt in dist\
```

Code layout: `src/Audio.cs` (capture, spectrum, voice matching), `src/Trainer.cs` (voice trainer), `src/MainForm.cs` (app logic and settings), `src/OrbView.cs` (main screen), `src/Ui.cs` (theme, dialogs and controls, shared with the installer), `src/Program.cs` (entry point, settings, key sending), `installer/Setup.cs` (installer and uninstaller).

## License

[MIT](LICENSE) © 2026 Shiverbolt

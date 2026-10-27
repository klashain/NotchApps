# NotchPilot

Power modes, **HP Omen fan control** and a game FPS counter in one app.

A notch at the top edge of the screen shows battery %, time remaining and CPU temperature. Hover over it to see the power mode name; click it to open the power mode buttons. With game mode on, the notch also shows the fan mode and GPU temperature.

[⬇ Download NotchPilot-Setup.exe](https://github.com/klashain/NotchApps/releases/latest) · [← Back to NotchApps](../README.md)

## Installation

1. Download `NotchPilot-Setup.exe` from the Releases page and run it.
2. Choose the install folder (default: `%LOCALAPPDATA%\Programs\NotchPilot`).
3. With **"Install admin mode"** checked, Windows asks for approval once. From then on NotchPilot always starts as administrator without a UAC prompt.
4. The installer creates Start menu and Windows startup shortcuts, plus an optional desktop shortcut. It also installs the PawnIO driver (used for CPU temperature) if it is missing.

Do not run NotchPilot from a USB stick: the admin task and startup shortcut store the install path.

## Features

| Feature | Details |
|---|---|
| **Power modes** | Battery Saver, Maximum Saver, Normal, Performance. In Maximum Saver the screen turns off after 3 minutes on battery, the display can drop to 60 Hz, and the discrete NVIDIA GPU can be disabled while the display runs on the integrated GPU. |
| **Game mode** | Turned on with a switch in Settings. Adds Power / Fan tabs to the main window with 4 presets (Quiet 0 RPM, Medium 1400, Game 2800, Max 5200) and an Auto (BIOS) button. |
| **Game FPS** | With game mode and "Show game FPS in the notch" on, the notch shows the live FPS of the foreground game. Counts DXGI "Present" events through Windows ETW; requires admin mode. Supports DirectX 11/12 games. |
| **Instant apply** | Settings apply as you change them; there is no save button. |
| **Start with Windows** | On by default; can be turned off in Settings. |
| **Languages** | Türkçe, English, Español, Deutsch, Français, 中文. |

## Fan safety

- When NotchPilot starts with game mode on, it does not touch the fans; it only reads them.
- When you pick a speed, it is written to the BIOS and re-applied periodically.
- On exit, and when game mode is turned off, the fans are handed back to BIOS auto mode.
- If the safety temperature (default 85 °C) is exceeded, the fans return to auto mode.
- Quiet (0 RPM) can cause heating under load.
- Fan control has **not been fully verified on hardware**. Try a low speed first.
- Do not run it at the same time as OmenFanControl or another fan tool.

## Command line

```
NotchPilot.exe                 opens the window; closing it minimizes to the tray
NotchPilot.exe pil             applies Battery Saver
NotchPilot.exe maks            applies Maximum Saver
NotchPilot.exe normal          applies Normal
NotchPilot.exe performans      applies Performance
```

## Building

The Windows .NET Framework compiler (`csc.exe`) is enough; no extra packages are needed. First put `PawnIO_setup.exe` in the repo root `lib/` folder (see the main README).

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Outputs:
- `build\out\NotchPilot.exe` — the app itself.
- `dist\NotchPilot-Setup.exe` — a single-file installer that embeds NotchPilot.exe. This is the file to publish.

## Folder layout

```
src/       source code (NotchPilot.cs, Setup.cs, Loc.cs, app.manifest)
assets/    icon (NotchPilot.ico)
build/     build script (build.ps1)
dist/      installer output (not in Git; published as a Release)
../lib/    shared libraries (used by both apps)
```

## Uninstalling

*Settings → Apps → NotchPilot → Uninstall*. This deletes the program folder, shortcuts, admin task and settings. Windows may ask for admin approval once while the task is removed.

If the uninstaller does not work, remove it manually:
1. Delete the `%LOCALAPPDATA%\Programs\NotchPilot` folder.
2. Delete the `NotchPilot.lnk` shortcuts in the Start menu, Startup folder and desktop.
3. Delete the "NotchPilot" task in Task Scheduler (may require admin rights).
4. Delete the `%APPDATA%\NotchPilot` folder (settings).

## Notes

- Settings file: `%APPDATA%\NotchPilot\settings.txt`
- GPU temperature is read from the NVIDIA driver (`nvml.dll`).
- The installer UI is currently Turkish only; the app itself supports all six languages.

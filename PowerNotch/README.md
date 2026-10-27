# PowerNotch

A power mode manager for **any Windows 10/11 laptop**. A notch at the top edge of the screen shows battery %, time remaining and CPU temperature; one click changes the power mode.

No fan control. Optionally shows game FPS in the notch.

[⬇ Download PowerNotch-Setup.exe](https://github.com/klashain/NotchApps/releases/latest) · [← Back to NotchApps](../README.md)

## Features

| Feature | Details |
|---|---|
| **Power modes** | Maximum Saver, Battery Saver, Normal, Performance. Each mode's CPU cap, screen brightness and screen-off time can be changed in Settings (in Maximum Saver the screen turns off after 3 minutes on battery). |
| **Notch** | Battery %, time remaining and CPU temperature. Hover to reveal the power mode name; click for the power mode buttons and a Settings button. |
| **Animations** | Windows open out of the notch and return to it with a genie effect; the notch bounces. Animations target 60 fps. |
| **FPS indicator (optional)** | Turn on "Show FPS" in Settings to see the live FPS of the foreground game in the notch. Counts DirectX (DXGI) frame events through Windows ETW; requires admin mode. |
| **Admin mode** | Installed once; afterwards the app starts as administrator without UAC prompts. |
| **Start with Windows** | On by default; can be turned off in Settings. |
| **Languages** | Türkçe, English, Español, Deutsch, Français, 中文. |

## Installation

1. Download `PowerNotch-Setup.exe` from the Releases page and run it.
2. Choose the install folder (default: `%LOCALAPPDATA%\Programs\PowerNotch`).
3. With **"Install admin mode"** checked, Windows asks for approval once. This step is needed for the FPS indicator; if skipped, the app runs in normal mode.
4. The installer creates Start menu and Windows startup shortcuts (plus an optional desktop shortcut).

Do not run the app from a USB stick: the admin task and startup shortcut store the install path.

## On other computers

- Windows 10 or 11 and .NET Framework 4 (ships with Windows) are enough.
- Without a battery, the notch shows `--` for battery info.
- CPU temperature depends on the sensor/ACPI data the hardware provides; otherwise `--` is shown.
- Power plans are applied with Windows' standard settings (`powercfg`).

## Command line

```
PowerNotch.exe pil | maks | normal | performans
```

Applies a power mode directly without opening the window.

## Building

The Windows .NET Framework compiler (`csc.exe`) is enough; no extra packages are needed. First put `PawnIO_setup.exe` in the repo root `lib/` folder (see the main README).

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Outputs:
- `build\out\PowerNotch.exe` — the app itself.
- `dist\PowerNotch-Setup.exe` — the installer that embeds PowerNotch.exe. This is the file to publish.

## Folder layout

```
src/       source code (PowerNotch.cs, Setup.cs, Loc.cs, app.manifest)
assets/    icon (PowerNotch.ico)
build/     build script (build.ps1)
dist/      installer output (not in Git; published as a Release)
../lib/    shared libraries (used by both apps)
```

## Uninstalling

*Settings → Apps → PowerNotch → Uninstall*. This deletes the program folder, shortcuts, admin task and settings.

If the uninstaller does not work, remove it manually:
1. Delete the `%LOCALAPPDATA%\Programs\PowerNotch` folder.
2. Delete the `PowerNotch.lnk` shortcuts in the Start menu, Startup folder and desktop.
3. Delete the "PowerNotch" task in Task Scheduler (may require admin rights).
4. Delete the `%APPDATA%\PowerNotch` folder (settings).

## Notes

- Settings file: `%APPDATA%\PowerNotch\settings.txt`
- The installer and uninstaller follow the Windows display language (six languages, English fallback).

<div align="center">

# NotchApps

**A Dynamic Island–style notch for Windows laptops — battery, temperatures, power modes and game FPS at the top of your screen.**

[![Latest release](https://img.shields.io/github/v/release/klashain/NotchApps?style=flat-square&label=release)](https://github.com/klashain/NotchApps/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/klashain/NotchApps/total?style=flat-square)](https://github.com/klashain/NotchApps/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4?style=flat-square)
![Languages](https://img.shields.io/badge/UI-TR%20%C2%B7%20EN%20%C2%B7%20ES%20%C2%B7%20DE%20%C2%B7%20FR%20%C2%B7%20ZH-555?style=flat-square)

[**Download**](https://github.com/klashain/NotchApps/releases/latest) · [Features](#-features) · [Which app?](#-which-app-should-i-use) · [FAQ](#-faq) · [Build](#-building-from-source)

</div>

---

NotchApps is two small, single-file Windows apps that share the same notch UI:

- **[NotchPilot](NotchPilot/)** — for **HP Omen** laptops: power modes **plus BIOS fan control** and a game mode.
- **[PowerNotch](PowerNotch/)** — for **any Windows 10/11 laptop**: power modes with per-mode tuning, no fan control.

Both are written in plain C# on .NET Framework 4 (ships with Windows), have **no installer dependencies**, and need no background services besides the app itself.

## ✨ Features

### The notch
- 🔋 **Always-visible status** — battery %, time remaining and CPU temperature in a pill at the top center of the screen.
- 🖱️ **Hover to peek, click to act** — hover to see the active power mode, click to drop down the power mode buttons.
- 🎮 **Live game FPS** — counts DirectX 11/12 frames of the foreground game via Windows ETW (the same technique PresentMon uses), no overlay injection.
- 🌡️ **GPU temperature** — read directly from the NVIDIA driver (`nvml.dll`), no `nvidia-smi` process spawning.
- 🐱 **Optional pet** — a tiny hand-drawn cat, dog, chick, rabbit or bird that lives in the notch and jumps when you click it.
- ↔️ **Draggable** — slide the notch horizontally; one button re-centers it.
- 🪄 **macOS-style animations** — windows fly out of the notch and return with a genie effect, spring bounces, 60 fps frame pacing.

### Power management
| Mode | What it does |
|---|---|
| 🍃 **Maximum Saver** | Most aggressive battery settings, caps CPU and brightness on battery, turns the screen off after 3 min on battery, can drop the display to **60 Hz** and disable the discrete NVIDIA GPU when the display runs on the iGPU. |
| 🔋 **Battery Saver** | Configurable CPU cap and brightness on battery. |
| ⚖️ **Normal** | Balanced plan, CPU limits reset, never sleeps. |
| ⚡ **Performance** | High performance plan. |

- Uses copies of Windows' built-in plans via `powercfg` — your own plans are never modified.
- Command line: `NotchPilot.exe pil | maks | normal | performans` (same for `PowerNotch.exe`) applies a mode without opening the UI.

### Fan control (NotchPilot only)
- 🌀 Four presets — **Quiet (0 RPM), Medium (1400), Game (2800), Max (5200)** — plus **Auto (BIOS)**.
- 🛡️ **Safety cut-off** — if the temperature exceeds the safety limit (default 85 °C), fans go back to BIOS auto.
- ♻️ Fans are always handed back to BIOS auto mode on exit or when game mode is turned off.
- Talks to the HP BIOS through its WMI interface (`hpqBIntM`), the same protocol OmenFanControl uses.

### Quality of life
- 🌍 **6 UI languages** — Türkçe, English, Español, Deutsch, Français, 中文 (follows Windows by default).
- 🔐 **Admin mode without UAC nags** — a one-time scheduled task lets the app start elevated (needed for FPS and fan control). The install folder is locked to administrators so the task cannot be abused.
- 🚀 **Start with Windows**, dark theme everywhere, tray icon, single-instance guard.
- 📦 **One-file installer** with a proper uninstaller entry in *Settings → Apps*.

## 🤔 Which app should I use?

| | NotchPilot | PowerNotch |
|---|:---:|:---:|
| Target hardware | HP Omen | Any Windows 10/11 laptop |
| Notch, battery, CPU temp | ✅ | ✅ |
| Power modes | ✅ | ✅ |
| Per-mode CPU / brightness / screen-off tuning | Battery & Max Saver | ✅ All modes |
| Game FPS in notch | ✅ | ✅ |
| GPU temperature | ✅ | ✅ |
| BIOS fan control & game mode | ✅ | ❌ |

> [!IMPORTANT]
> Install **only one** of them. Both manage power plans and draw a notch in the same place.

## 📥 Installation

1. Download `NotchPilot-Setup.exe` or `PowerNotch-Setup.exe` from the [latest release](https://github.com/klashain/NotchApps/releases/latest).
2. Run it and choose an install folder (default: `%LOCALAPPDATA%\Programs\<App>`).
3. Keep **"Install admin mode"** checked — Windows asks for approval once; afterwards the app starts elevated without UAC prompts.
4. Shortcuts are created in the Start menu and Startup folder (desktop shortcut optional).

> [!NOTE]
> The installers are not code-signed, so Windows SmartScreen may warn you. Click **More info → Run anyway**. You can verify the download with the SHA-256 hashes in the release notes, or build it yourself.

The installer window is currently in Turkish only; the apps themselves follow your Windows language (or the one you pick in Settings).

**Requirements:** Windows 10 or 11 (x64), .NET Framework 4 (preinstalled on Windows). Do not run from a USB stick — the admin task and startup shortcut store the install path.

## ❓ FAQ

<details>
<summary><b>Why does it need administrator rights?</b></summary>

Only for the features that require it: real-time ETW sessions (game FPS), reading CPU package temperature through the PawnIO driver, and HP BIOS fan commands. Without admin mode the app still runs; those features fall back or show `--`.
</details>

<details>
<summary><b>Is fan control safe?</b></summary>

NotchPilot only writes to the BIOS when you pick a speed, re-applies it periodically, and returns control to the BIOS on exit, when game mode is turned off, or when the safety temperature is exceeded. It is still **beta**: it has not been verified on every Omen model. Start with a low speed, and don't run it alongside OmenFanControl or another fan tool. Quiet (0 RPM) can cause heating under load.
</details>

<details>
<summary><b>The FPS counter shows nothing.</b></summary>

FPS needs admin mode and a DirectX 11/12 game in the foreground. OpenGL and Vulkan titles that don't present through DXGI are not counted.
</details>

<details>
<summary><b>CPU temperature shows <code>--</code>.</b></summary>

The app first reads the CPU sensor through LibreHardwareMonitor + PawnIO (installed by the setup), then falls back to the Windows ACPI thermal zones. Some laptops expose neither without admin rights.
</details>

<details>
<summary><b>What is PawnIO?</b></summary>

A signed, open-source kernel driver by namazso used by LibreHardwareMonitor to read CPU sensors. The installer sets it up silently if it is not already present.
</details>

<details>
<summary><b>How do I uninstall?</b></summary>

*Settings → Apps → NotchPilot/PowerNotch → Uninstall*. This removes the program folder, shortcuts, admin task and settings. Manual steps are in each app's README.
</details>

## 🛠️ Building from source

No Visual Studio or NuGet needed — the C# compiler that ships with Windows (`csc.exe`) is enough.

1. Download `PawnIO_setup.exe` from the [PawnIO.Setup releases](https://github.com/namazso/PawnIO.Setup/releases) and place it in `lib/`.
2. Build:

```powershell
powershell -ExecutionPolicy Bypass -File NotchPilot\build\build.ps1
powershell -ExecutionPolicy Bypass -File PowerNotch\build\build.ps1
```

Installers land in `NotchPilot\dist\` and `PowerNotch\dist\`.

## 📁 Repository layout

```
lib/                 shared third-party libraries (embedded into the exe)
NotchPilot/
  src/               NotchPilot.cs (app), Setup.cs (installer), Loc.cs (translations)
  assets/            icon
  build/build.ps1    build script
PowerNotch/          same layout
```

## 🤝 Contributing

Bug reports, translations and hardware test results (especially from other HP Omen models) are very welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## 🙏 Acknowledgements

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — CPU sensor access
- [PawnIO](https://github.com/namazso/PawnIO.Setup) by namazso — kernel driver for sensor reads
- [PresentMon](https://github.com/GameTechDev/PresentMon) — the ETW frame-counting approach
- OmenFanControl — reference for the HP BIOS WMI fan protocol

## 📄 License

[MIT](LICENSE) © klashain. Third-party components in `lib/` keep their own licenses — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

> **Disclaimer:** This project is not affiliated with HP Inc. "HP" and "OMEN" are trademarks of HP Inc. Fan control writes to your laptop's BIOS — use it at your own risk.

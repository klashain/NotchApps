# Contributing

Thanks for your interest in NotchApps! Contributions of all sizes are welcome.

## Ways to help

- **Report bugs** — open an issue with the bug report template. Include your laptop model, Windows version and which app you use.
- **Test fan control** — NotchPilot's fan control has only been verified on a few HP Omen models. A report with your model and whether each preset works is very valuable.
- **Translations** — all UI strings live in `src/Loc.cs` (one row per key: tr, en, es, de, fr, zh). Fix a translation or add a missing one.
- **Code** — bug fixes and small, focused features.

## Development setup

1. Windows 10/11 with .NET Framework 4 (already installed). No Visual Studio or NuGet needed.
2. Download `PawnIO_setup.exe` from the [PawnIO.Setup releases](https://github.com/namazso/PawnIO.Setup/releases) into `lib/`.
3. Build with `powershell -ExecutionPolicy Bypass -File NotchPilot\build\build.ps1` (or `PowerNotch\...`).
4. Run `NotchPilot\build\out\NotchPilot.exe` directly to test without installing.

## Guidelines

- Keep the apps single-file and dependency-free: no new NuGet packages or runtimes.
- Match the surrounding code style; write comments in English.
- If you change shared behavior (notch, power plans, installer), apply it to both `NotchPilot` and `PowerNotch` where it makes sense.
- New user-facing text goes through `Loc.T("key")` with at least English and Turkish.
- Anything that writes to the BIOS must fail safe: on error or exit, fans go back to BIOS auto mode.

## Pull requests

1. Fork the repo and create a branch from `main`.
2. Make sure both build scripts still succeed.
3. Describe what you changed and how you tested it (hardware, Windows version).

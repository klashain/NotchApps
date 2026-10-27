# Third-party notices

The libraries in `lib/` are **not** covered by this repository's MIT license; each is distributed under its own license. At build time these DLLs are embedded into the application exe.

| Component | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | 2.6.4 | Apache-2.0 | http://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| Microsoft.Win32.Registry | 4.6 | MIT | https://github.com/dotnet/runtime |
| System.IO.FileSystem.AccessControl | 4.6 | MIT | https://github.com/dotnet/runtime |
| System.Memory | 4.6 | MIT | https://github.com/dotnet/runtime |
| System.Runtime.CompilerServices.Unsafe | 6.0 | MIT | https://github.com/dotnet/runtime |
| System.Threading.AccessControl | 10.0 | MIT | https://github.com/dotnet/runtime |

The MPL-2.0 components are used unmodified; their source code is available at the links above.

## PawnIO

The installers embed the setup of the [PawnIO](https://pawnio.eu) driver (`PawnIO_setup.exe`, by namazso) to read CPU temperature. This file is not committed to the repository; before building, download it from the [PawnIO.Setup releases](https://github.com/namazso/PawnIO.Setup/releases) and place it in `lib/`. PawnIO is distributed under its own license.

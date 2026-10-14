# Third-party notices

Barline itself is released under the [MIT License](LICENSE). It uses the following third-party components without modifying them. The full license texts are in the [`licenses`](licenses) folder, which is also installed next to `Barline.exe`.

## Libraries included in the application

| Component | Version | License | License text | Source |
|---|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | [LibreHardwareMonitor.txt](licenses/LibreHardwareMonitor.txt) | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | [BlackSharp.txt](licenses/BlackSharp.txt) | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | [DiskInfoToolkit.txt](licenses/DiskInfoToolkit.txt) | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | [RAMSPDToolkit.txt](licenses/RAMSPDToolkit.txt) | https://github.com/Blacktempel/RAMSPDToolkit |
| PawnIO.Modules (compiled, signed modules embedded in LibreHardwareMonitorLib) | 0.1.6 | LGPL-2.1 | [PawnIO-Modules-LGPL-2.1.txt](licenses/PawnIO-Modules-LGPL-2.1.txt) | https://github.com/namazso/PawnIO.Modules/releases/tag/0.1.6 |
| HidSharp | 2.6.4 | Apache-2.0 | [HidSharp.txt](licenses/HidSharp.txt) | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | [Mono.Posix.txt](licenses/Mono.Posix.txt) | https://github.com/mono/mono |
| .NET runtime and libraries (System.CodeDom, System.IO.Ports, System.Management, System.Threading.AccessControl) | 8.0 / 10.0 | MIT | [dotnet-runtime.txt](licenses/dotnet-runtime.txt) | https://github.com/dotnet/runtime |
| WPF | 8.0 | MIT | [dotnet-wpf.txt](licenses/dotnet-wpf.txt) | https://github.com/dotnet/wpf |

The MPL-2.0 and LGPL-2.1 components are used unmodified; their source code is available at the links above. Barline communicates with them only through their public interfaces.

Some of these components contain code from further third parties. Their own notices are included as published by the projects:

- .NET runtime — [dotnet-runtime-THIRD-PARTY-NOTICES.txt](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt)
- WPF — [dotnet-wpf-THIRD-PARTY-NOTICES.txt](licenses/dotnet-wpf-THIRD-PARTY-NOTICES.txt)
- LibreHardwareMonitor — [LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt](licenses/LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt)

## Intel PresentMon (installed next to `Barline.exe`)

The unmodified, officially signed Intel binary is used to measure FPS.

- File: `PresentMon-2.5.1-x64.exe`
- License: MIT — [PresentMon.txt](licenses/PresentMon.txt); its own third-party notices — [PresentMon-THIRD-PARTY.txt](licenses/PresentMon-THIRD-PARTY.txt)
- Release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.5.1
- SHA-256: `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191`

## PawnIO driver (bundled in the installer)

The installer contains the unmodified official PawnIO 2.2.0 installer and runs it only after explicit consent. PawnIO is a separate program; Barline (through LibreHardwareMonitor) communicates with it only through its device I/O control interface, as permitted by the special exception in the PawnIO license. PawnIO is not removed together with Barline, because other software may use it.

- License: GPL-2.0 with a special exception — [PawnIO-GPL-2.0.txt](licenses/PawnIO-GPL-2.0.txt), https://github.com/namazso/PawnIO#license
- Corresponding source code: https://github.com/namazso/PawnIO/tree/2.2.0 (a source archive is also attached to every Barline release)
- Official installer release: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- SHA-256 of `PawnIO_setup.exe`: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

PresentMon and the PawnIO installer are downloaded at build time by `scripts/Get-Dependencies.ps1` and verified against these checksums and their Authenticode signatures; they are not stored in this repository.

## Build tools

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), which permits use for creating installers of any software, free of charge.

## Fonts

Barline uses the Windows system fonts Segoe UI Variable and Segoe UI. They are referenced from Windows and not redistributed.

# Changelog

## 3.4.0 — first public release

### Security
- The app is installed as a regular folder instead of a single-file bundle. The single-file WPF build extracted native libraries into the user's `%TEMP%` and loaded them into the elevated process.
- When elevated, logs and diagnostics are written to `Program Files\Barline\logs`, not into the user profile.
- The diagnostics report opens through the user's shell instead of an elevated editor.
- P/Invoke libraries are loaded from `System32` only.

### Changed
- “Show above all windows” is on by default for new installations: the strip stays visible and readable over fullscreen games and movies. Existing settings are kept.
- The tray menu and notifications are available in English and Russian, following the Windows display language. The diagnostics report is always in English.
- PresentMon and the PawnIO installer are downloaded at build time and verified instead of being stored in the repository.
- Smaller installer (57 MB instead of 74 MB).

## 3.3.0

### Changed
- The program is renamed from TaskbarMetrics to **Barline**. Installing over TaskbarMetrics moves the settings and removes the old shortcuts, folder and autostart entries.
- New logo: three bars on a graphite tile. The tray icon is monochrome, white on a dark taskbar and black on a light one.
- The installer follows the Windows 11 look and the system light/dark theme, with two steps instead of five.

## 3.2.0 – 3.2.1

### Changed
- Text labels are replaced with minimal filled icons; the strip uses the Windows 11 system font Segoe UI Variable.
- Fixed positions and widths for every icon and value; icons are centred exactly on the digits.
- Network traffic is shown in Mbit/s without a unit label, download first, from the physical adapter.

### Fixed
- Download and upload were identical when a VPN was active: the virtual tunnel adapter reported mirrored counters.

## 3.1.5

### Fixed
- CPU usage of the monitor itself dropped from 5.5% to about 2% of one core (NVIDIA through NVML, cheaper contrast sampling and window lookup).
- Turning autostart on from a non-elevated instance silently did nothing.
- With “show above all windows” off, the strip still stayed over fullscreen windows.
- FPS discarded long frames and mixed overlay swap chains with the game; Store apps showed no FPS.
- Network rates used binary prefixes and were about 5% too low.
- The error log could grow without limit.

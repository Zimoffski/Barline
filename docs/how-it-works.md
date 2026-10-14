# How Barline works

## Window and layering

The strip is a transparent, click-through WPF window (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`). It is owned by the taskbar window (`Shell_TrayWnd`) and placed just left of the notification area, so opening the hidden-icons flyout cannot push it below the Explorer layer. Explorer show/reorder events (`SetWinEventHook`) restore the order after the shell redraws or restarts.

- **Show above all windows: off.** The strip behaves like the taskbar itself: when a fullscreen window covers the primary monitor, it hides, and it returns after fullscreen ends.
- **Show above all windows: on (default).** The strip keeps its last stable position above fullscreen games and video.

## Layout

The strip uses the Windows 11 system font **Segoe UI Variable Small**, 12 px. It is referenced from Windows, never redistributed; Windows 10 has no Segoe UI Variable and falls back to Segoe UI. WPF exposes only the Regular and SemiBold instances of the variable font: values are SemiBold, temperatures Regular.

- Icons replace text labels: minimal filled glyphs on a 16×16 grid (processor, graphics card, memory module, speedometer) and filled network arrows. Every icon has an even pixel height and is centred exactly on the digits.
- Values use the primary colour, icons and temperatures the secondary colour, the `/` separator the tertiary colour. Missing values are a secondary-coloured `—`.
- Every icon has a fixed position. Every value cell has a fixed width measured from its longest value (`100%`, `100°`, `9999`, `999`) and clips its content, so changing numbers never move or overlap anything. Percentages are right-aligned before a fixed `/`; temperature, network and FPS grow from their left edge.
- Groups are separated by 16 px of space, without dividers.

## Adaptive contrast

With *show above all windows* on, the text colour follows whatever is behind the strip. Fifteen times per second the screen area under the strip is copied with a single `BitBlt` and 18 points are sampled (9 across, in the empty bands above and below the glyphs, so the strip never reacts to its own text). White or black wins by the better WCAG contrast at the worst point. The colour changes only when the other one is at least 1.25 times better, so a mid-grey background does not make the text blink. Temperature warnings keep their colours, picked from the palette that suits a dark or a light background. With the mode off, the screen is not sampled at all.

## FPS

FPS comes from the signed Intel PresentMon 2.5.1, run as a helper process with its own ETW session; nothing is injected into the measured app.

- The app of the foreground window is tracked; multi-process apps are merged by process name. For Microsoft Store apps the hosted app is used, not `ApplicationFrameHost`.
- If an app presents through several swap chains (for example, a game and an in-game overlay), the busiest one is used.
- The value is the number of displayed frames in the last two seconds divided by the time they were on screen, updated four times per second. Long frames are not discarded, so stutters lower the value honestly.
- The desktop and File Explorer are excluded; a window that draws nothing shows `—`.
- ETW requires administrator rights. Without them PresentMon is not started and FPS shows `—`.

## Sensors

- CPU load comes from `GetSystemTimes`, memory load from `GlobalMemoryStatusEx`.
- Temperatures and non-NVIDIA GPUs are read by LibreHardwareMonitor. The CPU temperature needs the PawnIO driver.
- NVIDIA load and temperature are read directly from the driver through NVML. A full LibreHardwareMonitor update of an NVIDIA card costs about 80 ms per second (it also samples PCIe throughput); NVML returns these two values in well under 1 ms.

## Network

The current traffic of the busiest **physical** adapter, from the adapter counters, in megabits per second with decimal prefixes. Virtual adapters (VPN tunnels, WSL, Hyper-V switches) are ignored while a physical adapter is up: a TUN adapter, for example, can report mirrored counters with equal download and upload. The adapter list is cached and rebuilt when Windows reports a network change.

## Autostart and rights

- Without sensor access, autostart is a `HKCU\...\Run` entry.
- With sensor access, autostart is a Task Scheduler task `Barline sensor access` that runs at logon with the highest privileges, so there is no UAC prompt at each logon. The installer creates it.
- Changing autostart from a non-elevated instance asks for administrator rights once; if declined, a normal `Run` entry is used.

## Security notes

An elevated monitor must not load or write anything an unelevated process can change:

- Barline is installed as a regular folder in `Program Files`, not as a single-file bundle. A WPF single-file app extracts native libraries into the user's `%TEMP%` and loads them from there, which would let any unelevated process plant a DLL.
- P/Invoke libraries are loaded from `System32` only.
- When elevated, logs and diagnostics are written to `Program Files\Barline\logs`, not into the user profile.
- The diagnostics report is opened through the user's own shell, not in an elevated editor.
- Third-party binaries are downloaded at build time from their official releases and verified against pinned SHA-256 checksums and Authenticode signatures.

## Files

| Path | Contents |
|---|---|
| `HKCU\Software\Barline` | Settings: autostart, sensor access, always on top. |
| `%LOCALAPPDATA%\Barline` | `error.log`, `diagnostics.txt` when running without administrator rights. |
| `Program Files\Barline\logs` | The same files when running with administrator rights. |

`error.log` writes the same error at most once every 10 minutes and is rotated to `error.old.log` above 1 MB.

## Command line

| Argument | Purpose |
|---|---|
| `--startup` | Used by autostart. |
| `--diagnostics` | Writes the sensor report and exits. |
| `--stop` | Stops running instances. |
| `--configure-sensor-access`, `--apply-startup`, `--uninstall` | Used by the installer and the tray menu. |

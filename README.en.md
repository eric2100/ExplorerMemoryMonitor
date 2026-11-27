# Explorer Memory Monitor 0.2.1

**Windows memory monitoring and Explorer diagnostics** · [繁體中文](README.md)

Shows available RAM in the notification area, records Explorer memory usage and captures diagnostic information when it becomes unusually high. Manual and optional automatic working-set trimming are included. The application interface is currently in Traditional Chinese.

![Feature overview](docs/overview.svg)

This diagram illustrates the workflow; it is not a screenshot.

## Features

| Feature | Default behavior |
| --- | --- |
| Tray icon | Updates about every 15 seconds; the number and % sign show the percentage of physical memory available, rounded down. Hover for the percentage and capacity. Orange below 2 GB, red below 1 GB. |
| Manual trim | Right-click **整理目前符合條件的閒置程式** to trim at most one process meeting the idle and once-per-process conditions below. |
| Automatic idle trim | On by default. A visible-window process in the current user session must have a working set of at least 1 GB, have stayed out of the foreground for at least five minutes and averaged less than 1% of one CPU core. Below 2 GB available system memory, the threshold is 100 MB and only one attempt is made per low-memory episode; it rearms after 2.5 GB remains available for five minutes. Browsers, Explorer and selected Windows components are excluded. Attempts at most once per process, at least 30 minutes apart. Toggle in the tray menu. |
| Start at sign-in | On by default for the current account. Toggle **登入 Windows 時啟動** in the tray menu. This does not control the diagnostic task. |
| Explorer diagnostics | Samples every five minutes. At 2 GB of Explorer private memory, saves process, module and system information and attempts a full memory dump, once per Explorer process. |
| Explorer automatic restart | After two checks at or above 4 GB of private memory, a successful dump and other safeguards, warns 15 seconds before restarting Explorer. Requires exactly one Explorer process in the current session and runs at most once per Windows boot. Toggle in the tray menu. |
| Low-memory alert | Warns below 1 GB of available physical RAM, at most once every 30 minutes while it remains low. |

Windows already manages memory. Trimming can temporarily increase the available figure but may cause data to be reloaded and performance to drop temporarily. It does not fix a memory leak or guarantee against crashes. Foreground and CPU measurements cannot prove a process has no background work; trimming does not terminate it. Windows notification settings can affect alerts.

## Install

Requires 64-bit Windows 10/11, Windows PowerShell 5.1, the .NET Framework 4.x C# compiler, an interactive desktop session and administrator access on the account that will use the app. This version uses `C:\ProgramData`. Development testing was performed on Windows 11; other versions have not been fully validated. Installation compiles the C# programs; the daily tray and scheduled monitor do not start PowerShell.

1. Download `ExplorerMemoryMonitor-0.2.1.zip` from [Releases](../../releases), or download the complete source ZIP, and extract it.
2. Sign in to the intended user account and right-click `install.cmd` → **Run as administrator**.
3. Find the numeric icon in the notification area, including the hidden icons under `^`. Right-click for the controls.

Alternatively, run in an elevated PowerShell from the extracted folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

Installation places the C# programs in `%LOCALAPPDATA%\ExplorerMemoryMonitor`, stores diagnostics in `C:\ProgramData\ExplorerMemoryMonitor`, configures sign-in startup for the current account and registers the `ExplorerMemoryMonitor` scheduled task. The task runs only while that account is signed in. Reinstalling updates the programs and enables sign-in startup again; the automatic-trim preference is preserved.

## Diagnostics and privacy

Data is stored in `C:\ProgramData\ExplorerMemoryMonitor`, including `samples-csharp.csv` and `capture-*` folders. A full dump can be large and may contain file contents, sign-in state or other sensitive data. **Do not publish dumps**; review their contents and recipient before sharing. Nothing is uploaded automatically, and old records are not automatically deleted. The folder is restricted to SYSTEM, administrators and the necessary access for the installing account; other administrators on the computer can still access it.

The dump is skipped if the disk has less than 5 GB plus 2.5 times Explorer's private memory available. See `dump-status.txt` in the capture folder. A failed dump is not retried for the same process. Five-minute sampling can miss short spikes.

## Uninstall

Run `uninstall.cmd` as administrator, or use an elevated PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

The uninstaller closes the current account's tray app and removes startup registration, tasks and program scripts, but **preserves diagnostic data and user settings**. Review and delete retained data yourself if it is no longer needed.

## Troubleshooting

- Missing icon: check hidden icons, Windows notification settings and the signed-in account.
- Missing capture: inspect the `ExplorerMemoryMonitor` task's last result and `errors.log` or `dump-status.txt` in the data folder.
- To disable automatic trimming, clear **閒置且占用過高時自動整理** in the tray menu.
- Version 0.2.1 is not a backup, recovery, medical, real-time or high-availability monitoring system.

## License

MIT. See [LICENSE](LICENSE).

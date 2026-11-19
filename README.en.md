# Explorer Memory Monitor 0.1.2

**Windows memory monitoring and Explorer diagnostics** · [繁體中文](README.md)

Shows available RAM in the notification area, records Explorer memory usage and captures diagnostic information when it becomes unusually high. Manual and optional automatic working-set trimming are included. The application interface is currently in Traditional Chinese.

![Feature overview](docs/overview.svg)

This diagram illustrates the workflow; it is not a screenshot.

## Features

| Feature | Default behavior |
| --- | --- |
| Tray icon | Updates about every 10 seconds; the number is available physical memory in GB, rounded down. Hover for the precise value and total. Orange below 2 GB, red below 1 GB. |
| Manual trim | Right-click **釋放記憶體** to trim eligible processes in the current user session with at least 100 MB in their working set. |
| Automatic trim | On by default below 2 GB available physical memory. Trims at most three eligible processes, at least 30 minutes apart; excludes the foreground process and selected Windows shell processes. Toggle in the tray menu. |
| Start at sign-in | On by default for the current account. Toggle **登入 Windows 時啟動** in the tray menu. This does not control the diagnostic task. |
| Explorer diagnostics | Samples every five minutes. At 2 GB of Explorer private memory, saves process, module and system information and attempts a full memory dump, once per Explorer process. |
| Low-memory alert | Warns below 1 GB of available physical RAM, at most once every 30 minutes while it remains low. |

Windows already manages memory. Trimming can temporarily increase the available figure but may cause data to be reloaded and performance to drop temporarily. It does not fix a memory leak or guarantee against crashes. The automatic operation does not terminate processes; the manual option has a broader scope. Windows notification settings can affect alerts.

## Install

Requires Windows 10/11, Windows PowerShell 5.1, an interactive desktop session and administrator access on the account that will use the app. This version uses `C:\ProgramData`. Development testing was performed on Windows 11; other versions have not been fully validated.

1. Download `ExplorerMemoryMonitor-0.1.2.zip` from [Releases](../../releases), or download the complete source ZIP, and extract it.
2. Sign in to the intended user account and right-click `install.cmd` → **Run as administrator**.
3. Find the numeric icon in the notification area, including the hidden icons under `^`. Right-click for the controls.

Alternatively, run in an elevated PowerShell from the extracted folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

Installation copies the scripts to `C:\ProgramData\ExplorerMemoryMonitor`, configures sign-in startup for the current account and registers the `ExplorerMemoryMonitor` scheduled task. The task runs only while that account is signed in. Reinstalling updates the scripts and enables sign-in startup again; the automatic-trim preference is preserved.

## Diagnostics and privacy

Data is stored in `C:\ProgramData\ExplorerMemoryMonitor`, including `samples.csv` and `capture-*` folders. A full dump can be large and may contain file contents, sign-in state or other sensitive data. **Do not publish dumps**; review their contents and recipient before sharing. Nothing is uploaded automatically, and old records are not automatically deleted. The folder is restricted to SYSTEM, administrators and the necessary access for the installing account; other administrators on the computer can still access it.

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
- To disable automatic trimming, clear **自動釋放記憶體（低於 2 GB）** in the tray menu.
- Version 0.1.2 is not a backup, recovery, medical, real-time or high-availability monitoring system.

## License

MIT. See [LICENSE](LICENSE).

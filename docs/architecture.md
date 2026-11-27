# 程式與診斷資料

本專案以 .NET Framework 4.x 和 Windows Forms 建立，所有程式碼與測試都在 `src` 目錄，使用 C# 撰寫。

## 程式分工

| 檔案 | 功能 |
| --- | --- |
| `MemoryTray.cs` | 系統匣圖示、設定視窗、啟動選項、記憶體整理與通知。 |
| `MemoryMonitor.cs` | 定時紀錄、低記憶體通知、檔案總管診斷、傾印和重啟判斷。 |
| `MemorySetup.cs` | 安裝、更新、解除安裝、啟動項目與工作排程。 |
| `MemoryTrayTests.cs`、`MemoryMonitorTests.cs`、`SettingsTests.cs`、`MemorySetupTests.cs` | 條件判斷、隔離傾印、設定同步、安裝資料與獨立測試排程。 |

## 判斷與限制

系統匣程式每 15 秒取樣。同一登入工作階段的視窗程式必須至少 5 分鐘未在前景，且觀察期間的平均 CPU 用量低於單一核心的 1%，才視為符合閒置條件。一般情況下工作集須至少 1 GB；可用實體記憶體低於 2 GB 時門檻為 100 MB。取樣無法證明程式沒有背景工作，整理後也可能重新載入資料。

自動整理排除瀏覽器、檔案總管和部分系統程式，每個程序最多嘗試一次，兩次至少間隔 30 分鐘。記憶體持續低於 2 GB 時只嘗試一次；回升到 2.5 GB 並維持 5 分鐘才重新待命。滑鼠點選手動整理也必須符合閒置與單次條件。

強制整理是另一個需要確認的手動操作，逐一嘗試清理同帳戶、同工作階段可存取的程序工作集，包括前景程式、瀏覽器與檔案總管；自身及關鍵 Windows 程序除外。整理期間暫停閒置掃描，不終止程序，不修改自動整理的次數或冷卻紀錄。系統可用記憶體可能只暫時上升，且可能使程式短暫變慢。

診斷排程每 5 分鐘執行，檔案總管私人記憶體達 2 GB 時保存資料，空間足夠才建立完整傾印。持續達 4 GB 且符合其他保護條件時才會自動重啟；成功傾印是必要條件，每次 Windows 開機最多一次。

## 資料與權限

- `%LOCALAPPDATA%\ExplorerMemoryMonitor`：程式、使用者選項、已整理程序標記與冷卻時間。
- `C:\ProgramData\ExplorerMemoryMonitor`：取樣、診斷資料與傾印。完整傾印可能含敏感資料，請勿直接公開分享。
- `ExplorerMemoryMonitor` 排程使用安裝帳戶的互動式工作階段與最高可用權限，每 5 分鐘及登入時執行；電池供電時也可執行。最多同時執行一個實例，每次執行上限 15 分鐘。

安裝器不會刪除舊傾印或舊紀錄；解除安裝保留診斷資料和使用者設定。若從 0.1.x 的 PowerShell 系統匣版升級，安裝後重新登入 Windows，可結束先前工作階段中仍在執行的舊圖示。

## 測試

測試程式與被測來源一起編譯，使用 `/main` 指定測試入口。例如，在專案目錄執行：

```powershell
$c = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $c /nologo /codepage:65001 /target:exe /main:ExplorerMemoryMonitor.Tests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:MemoryTrayTests.exe src\MemoryTray.cs src\MemoryTrayTests.cs
.\MemoryTrayTests.exe
& $c /nologo /codepage:65001 /target:exe /main:ExplorerMemoryMonitor.SetupTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:MemorySetupTests.exe src\MemorySetup.cs src\MemorySetupTests.cs
.\MemorySetupTests.exe
```

安裝器測試會建立並刪除一個名稱隨機的測試排程，需以系統管理員執行；不會修改正式排程。完整傾印測試只對測試建立的子程序執行，並刪除測試傾印。測試通過不代表所有 Windows 版本或實際記憶體耗盡情境都已驗證。

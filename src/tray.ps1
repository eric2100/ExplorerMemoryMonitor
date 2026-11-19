Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$created = $false
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class TrayNative { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid); [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h); [DllImport("psapi.dll", SetLastError=true)] public static extern bool EmptyWorkingSet(IntPtr h); }'
$mutex = New-Object System.Threading.Mutex($true, 'Local\ExplorerMemoryMonitorTray', [ref]$created)
if (!$created) { exit }
$icon = New-Object System.Windows.Forms.NotifyIcon
$menu = New-Object System.Windows.Forms.ContextMenuStrip
$status = $menu.Items.Add('Memory')
$status.Enabled = $false
function Invoke-MemoryTrim([bool]$automatic) {
  try {
    $before = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    $session = (Get-Process -Id $PID).SessionId
    $count = 0
    [uint32]$foregroundId = 0
    [TrayNative]::GetWindowThreadProcessId([TrayNative]::GetForegroundWindow(), [ref]$foregroundId) | Out-Null
    $skip = @('explorer','dwm','csrss','winlogon','sihost','taskhostw','StartMenuExperienceHost','SearchHost','ShellExperienceHost')
    $candidates = @(Get-Process | Where-Object { $_.Id -ne $PID -and $_.SessionId -eq $session -and $_.WorkingSet64 -ge 100MB } | Sort-Object WorkingSet64 -Descending)
    foreach ($p in $candidates) {
      try {
        if ($automatic -and ($p.Id -eq $foregroundId -or $p.ProcessName -in $skip)) { continue }
        $info = Get-CimInstance Win32_Process -Filter ("ProcessId = {0}" -f $p.Id)
        $owner = Invoke-CimMethod -InputObject $info -MethodName GetOwner
        if ($owner.ReturnValue -ne 0 -or $owner.User -ne $env:USERNAME -or $owner.Domain -ne $env:USERDOMAIN) { continue }
        $handle = [TrayNative]::OpenProcess(0x500, $false, $p.Id)
        if ($handle -ne [IntPtr]::Zero) {
          try { if ([TrayNative]::EmptyWorkingSet($handle)) { $count++ } } finally { [TrayNative]::CloseHandle($handle) | Out-Null }
        }
        if ($automatic -and $count -ge 3) { break }
      } catch { continue }
    }
    $after = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    $message = '已整理 {0} 個程式的記憶體。可用記憶體：{1:F1} → {2:F1} GB。' -f $count,($before / 1MB),($after / 1MB)
    $title = if ($automatic) { '自動整理記憶體完成' } else { '記憶體整理完成' }
    $icon.ShowBalloonTip(10000, $title, $message, [System.Windows.Forms.ToolTipIcon]::Info)
  } catch {
    $icon.ShowBalloonTip(10000, '記憶體整理失敗', $_.Exception.Message, [System.Windows.Forms.ToolTipIcon]::Warning)
  }
}
$settingsDir = Join-Path $env:LOCALAPPDATA 'ExplorerMemoryMonitor'
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$disabledFile = Join-Path $settingsDir 'auto-disabled'
$cooldownFile = Join-Path $settingsDir 'last-auto-trim.txt'
$pressureLatch = Join-Path $settingsDir 'low-memory-episode.txt'
$recoveryMarker = Join-Path $settingsDir 'memory-recovery.txt'
$restartDisabledFile = Join-Path $settingsDir 'auto-explorer-restart-disabled'
$restartItem = $menu.Items.Add('檔案總管異常時自動重啟')
$restartItem.Checked = !(Test-Path $restartDisabledFile)
$restartItem.add_Click({
  try {
    if ($restartItem.Checked) { Set-Content -LiteralPath $restartDisabledFile -Value 'disabled'; $restartItem.Checked = $false }
    else { Remove-Item -LiteralPath $restartDisabledFile -Force -ErrorAction Stop; $restartItem.Checked = $true }
  } catch { $icon.ShowBalloonTip(10000, '設定失敗', $_.Exception.Message, [System.Windows.Forms.ToolTipIcon]::Warning) }
})
$autoItem = $menu.Items.Add('自動釋放記憶體（低於 2 GB）')
$autoItem.Checked = !(Test-Path $disabledFile)
$autoItem.add_Click({
  try {
    if ($autoItem.Checked) { Set-Content -LiteralPath $disabledFile -Value 'disabled'; $autoItem.Checked = $false }
    else { Remove-Item -LiteralPath $disabledFile -Force -ErrorAction Stop; $autoItem.Checked = $true }
  } catch { $icon.ShowBalloonTip(10000, '設定失敗', $_.Exception.Message, [System.Windows.Forms.ToolTipIcon]::Warning) }
})
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'ExplorerMemoryTray'
$runCommand = '"' + $env:SystemRoot + '\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\ProgramData\ExplorerMemoryMonitor\tray.ps1"'
$startupItem = $menu.Items.Add('登入 Windows 時啟動')
$startupItem.Checked = $null -ne (Get-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue)
$startupItem.add_Click({
  try {
    if ($startupItem.Checked) { Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction Stop; $startupItem.Checked = $false }
    else { New-ItemProperty -Path $runKey -Name $runName -Value $runCommand -PropertyType String -Force -ErrorAction Stop | Out-Null; $startupItem.Checked = $true }
  } catch { $icon.ShowBalloonTip(10000, '設定失敗', $_.Exception.Message, [System.Windows.Forms.ToolTipIcon]::Warning) }
})
$menu.Items.Add('釋放記憶體').add_Click({ Invoke-MemoryTrim $false; & $update })
$menu.Items.Add('開啟工作管理員').add_Click({ Start-Process taskmgr.exe })
$menu.Items.Add('開啟診斷資料夾').add_Click({ Start-Process explorer.exe 'C:\ProgramData\ExplorerMemoryMonitor' })
$menu.Items.Add('結束顯示').add_Click({ [System.Windows.Forms.Application]::ExitThread() })
$icon.ContextMenuStrip = $menu
$icon.Visible = $true
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 10000
$update = {
  try {
    $os = Get-CimInstance Win32_OperatingSystem
    $free = $os.FreePhysicalMemory / 1MB
    $total = $os.TotalVisibleMemorySize / 1MB
    $percent = [math]::Floor(100 * $os.FreePhysicalMemory / $os.TotalVisibleMemorySize)
    $label = '{0:F0}' -f $percent
    $icon.Text = '可用記憶體：{0}%（{1:F1} / {2:F1} GB）' -f $percent,$free,$total
    $status.Text = $icon.Text
    $bmp = New-Object System.Drawing.Bitmap 32,32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $bg = if ($free -lt 1) { [System.Drawing.Color]::DarkRed } elseif ($free -lt 2) { [System.Drawing.Color]::DarkOrange } else { [System.Drawing.Color]::DarkGreen }
    $g.Clear($bg)
    $fontSize = if ($label.Length -ge 3) { 13 } elseif ($label.Length -eq 2) { 20 } else { 23 }
    $font = New-Object System.Drawing.Font('Arial', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $percentFont = New-Object System.Drawing.Font('Arial', 7, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $numberWidth = $g.MeasureString($label, $font, [int]32, $fmt).Width
    $symbolWidth = $g.MeasureString('%', $percentFont, [int]32, $fmt).Width
    $left = [Math]::Max(0, (32 - $numberWidth - $symbolWidth) / 2)
    $g.DrawString($label, $font, [System.Drawing.Brushes]::White, [single]$left, [single]((32 - $font.GetHeight($g)) / 2), $fmt)
    $g.DrawString('%', $percentFont, [System.Drawing.Brushes]::White, [single]($left + $numberWidth), [single]4, $fmt)
    $handle = $bmp.GetHicon()
    $newIcon = [System.Drawing.Icon]::FromHandle($handle).Clone()
    [TrayNative]::DestroyIcon($handle) | Out-Null
    $oldIcon = $icon.Icon
    $icon.Icon = $newIcon
    if ($oldIcon) { $oldIcon.Dispose() }
    $g.Dispose(); $font.Dispose(); $percentFont.Dispose(); $fmt.Dispose(); $bmp.Dispose()
  } catch { ("{0} {1}" -f (Get-Date).ToString('o'), $_) | Add-Content 'C:\ProgramData\ExplorerMemoryMonitor\tray-errors.log' }
}
$timer.Add_Tick({
  & $update
  try {
    $availableKB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if (Test-Path $pressureLatch) {
      if ($availableKB -ge 2.5MB) {
        if (!(Test-Path $recoveryMarker)) { Set-Content -LiteralPath $recoveryMarker -Value (Get-Date).ToString('o') }
        elseif (((Get-Date) - (Get-Item $recoveryMarker).LastWriteTime).TotalMinutes -ge 5) {
          Remove-Item -LiteralPath $pressureLatch,$recoveryMarker -Force -ErrorAction Stop
        }
      } else { Remove-Item -LiteralPath $recoveryMarker -Force -ErrorAction SilentlyContinue }
    }
    if ($autoItem.Checked -and $availableKB -lt 2MB -and !(Test-Path $pressureLatch) -and (!(Test-Path $cooldownFile) -or ((Get-Date) - (Get-Item $cooldownFile).LastWriteTime).TotalMinutes -ge 30)) {
      Set-Content -LiteralPath $pressureLatch -Value (Get-Date).ToString('o')
      Set-Content -LiteralPath $cooldownFile -Value (Get-Date).ToString('o')
      Invoke-MemoryTrim $true
      & $update
    }
  } catch { ("{0} {1}" -f (Get-Date).ToString('o'), $_) | Add-Content (Join-Path $settingsDir 'auto-errors.log') }
})
& $update
$timer.Start()
try { [System.Windows.Forms.Application]::Run() } finally { $timer.Stop(); $timer.Dispose(); $icon.Visible=$false; $icon.Dispose(); $mutex.ReleaseMutex(); $mutex.Dispose() }

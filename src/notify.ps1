param([Parameter(Mandatory=$true)][string]$MessageFile)
try {
  $message = [IO.File]::ReadAllText($MessageFile, [Text.Encoding]::UTF8)
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  $icon = New-Object System.Windows.Forms.NotifyIcon
  $icon.Icon = [System.Drawing.SystemIcons]::Warning
  $icon.Visible = $true
  $icon.ShowBalloonTip(15000, '記憶體監控提醒', $message, [System.Windows.Forms.ToolTipIcon]::Warning)
  Start-Sleep -Seconds 20
  $icon.Dispose()
  Remove-Item -LiteralPath $MessageFile -Force
} catch {
  ("{0} {1}" -f (Get-Date).ToString('o'),($_ | Out-String)) | Add-Content 'C:\ProgramData\ExplorerMemoryMonitor\notification-errors.log' -Encoding UTF8
  exit 1
}

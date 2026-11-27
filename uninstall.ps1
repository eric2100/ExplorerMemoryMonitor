$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator, using the account that installed the app.' }
$userDir = Join-Path $env:LOCALAPPDATA 'ExplorerMemoryMonitor'
Get-CimInstance Win32_Process -Filter "Name='MemoryTray.exe'" | Where-Object { $_.ExecutablePath -eq (Join-Path $userDir 'MemoryTray.exe') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Remove-Item -LiteralPath (Join-Path $userDir 'MemoryTray.exe') -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -match '(?i)-File\s+"?C:\\ProgramData\\ExplorerMemoryMonitor\\tray\.ps1' } | ForEach-Object {
  $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if ($owner.ReturnValue -eq 0 -and $owner.User -eq $env:USERNAME -and $owner.Domain -eq $env:USERDOMAIN) { Stop-Process -Id $_.ProcessId -Force }
}
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $run -Name 'ExplorerMemoryTray' -ErrorAction SilentlyContinue
foreach ($name in @('ExplorerMemoryMonitor','ExplorerMemoryTray')) {
  if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
  }
}
$root = 'C:\ProgramData\ExplorerMemoryMonitor'
Remove-Item -LiteralPath (Join-Path $userDir 'MemoryMonitor.exe') -Force -ErrorAction SilentlyContinue
foreach ($file in @('tray.ps1','monitor.ps1','notify.ps1','run-monitor.vbs','MemoryTray.cs','MemoryMonitor.cs')) { Remove-Item -LiteralPath (Join-Path $root $file) -Force -ErrorAction SilentlyContinue }
Write-Host 'Uninstalled.'
Write-Host "Diagnostic captures and settings were preserved in $root and %LOCALAPPDATA%\ExplorerMemoryMonitor."

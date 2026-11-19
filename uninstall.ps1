$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator, using the account that installed the app.' }
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
foreach ($file in @('tray.ps1','monitor.ps1','notify.ps1','run-monitor.vbs')) { Remove-Item -LiteralPath (Join-Path $root $file) -Force -ErrorAction SilentlyContinue }
Write-Host 'Uninstalled.'
Write-Host "Diagnostic captures and settings were preserved in $root and %LOCALAPPDATA%\ExplorerMemoryMonitor."

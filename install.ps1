param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$root = 'C:\ProgramData\ExplorerMemoryMonitor'
$source = Join-Path $PSScriptRoot 'src'
$files = @('tray.ps1','monitor.ps1','notify.ps1','run-monitor.vbs')
foreach ($file in $files) {
  $path = Join-Path $source $file
  if (!(Test-Path -LiteralPath $path)) { throw "Missing file: $path" }
  if ($file -like '*.ps1') {
    $tokens = $null; $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$parseErrors) | Out-Null
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
  }
}
if ($CheckOnly) { Write-Host 'All source files are present; PowerShell scripts parse successfully.'; return }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as administrator, using the account that will use the tray.' }
if ($env:USERNAME -eq 'SYSTEM') { throw 'Run this installer in an interactive user session, not as SYSTEM.' }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -match '(?i)-File\s+"?C:\\ProgramData\\ExplorerMemoryMonitor\\tray\.ps1' } | ForEach-Object {
  $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if ($owner.ReturnValue -eq 0 -and $owner.User -eq $env:USERNAME -and $owner.Domain -eq $env:USERDOMAIN) { Stop-Process -Id $_.ProcessId -Force }
}
New-Item -ItemType Directory -Path $root -Force | Out-Null
& icacls.exe $root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to protect the data folder.' }
$sid = $identity.User.Value
& icacls.exe $root /grant "*${sid}:(RX)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to grant access to the installation folder.' }
foreach ($file in $files) {
  $destination = Join-Path $root $file
  Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination -Force
  & icacls.exe $destination /grant "*${sid}:(RX)" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Unable to grant access to $file" }
}
$exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\wscript.exe') -Argument "//B `"$root\run-monitor.vbs`""
$daily = New-ScheduledTaskTrigger -Daily -At 00:00
$daily.Repetition = (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 1)).Repetition
$user = "$env:USERDOMAIN\$env:USERNAME"
$logon = New-ScheduledTaskTrigger -AtLogOn -User $user
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 15) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'ExplorerMemoryMonitor' -Action $action -Trigger $daily,$logon -Principal $taskPrincipal -Settings $settings -Force | Out-Null
$oldTray = Get-ScheduledTask -TaskName 'ExplorerMemoryTray' -ErrorAction SilentlyContinue
if ($oldTray) { Stop-ScheduledTask -TaskName 'ExplorerMemoryTray' -ErrorAction SilentlyContinue; Disable-ScheduledTask -TaskName 'ExplorerMemoryTray' | Out-Null }
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = "`"$exe`" -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$root\tray.ps1`""
New-ItemProperty -Path $run -Name 'ExplorerMemoryTray' -Value $command -PropertyType String -Force | Out-Null
Start-ScheduledTask -TaskName 'ExplorerMemoryMonitor'
Start-Process -FilePath $exe -WindowStyle Hidden -ArgumentList "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$root\tray.ps1`""
Write-Host 'Installed. Check the notification area (including hidden icons).'
Write-Host 'Data: C:\ProgramData\ExplorerMemoryMonitor'

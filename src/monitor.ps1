param([switch]$TestSnapshot)
$ErrorActionPreference = 'Stop'
$base = 'C:\ProgramData\ExplorerMemoryMonitor'
$log = Join-Path $base 'samples.csv'
$now = Get-Date
function Send-MemoryAlert([string]$message) {
  $path = Join-Path $base ("alert-{0}.txt" -f [guid]::NewGuid().ToString('N'))
  [IO.File]::WriteAllText($path, $message, [Text.Encoding]::UTF8)
  Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -WindowStyle Hidden -ArgumentList "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:\ProgramData\ExplorerMemoryMonitor\notify.ps1 -MessageFile $path" | Out-Null
}
try {
  $os = Get-CimInstance Win32_OperatingSystem
  $lowMarker = Join-Path $base 'low-memory-alert.txt'
  if ($os.FreePhysicalMemory -lt 1MB -and (!(Test-Path $lowMarker) -or ((Get-Date) - (Get-Item $lowMarker).LastWriteTime).TotalMinutes -ge 30)) {
    Send-MemoryAlert ("可用實體記憶體剩下 {0:N2} GB，請儲存工作並檢查工作管理員。" -f ($os.FreePhysicalMemory / 1MB))
    Set-Content $lowMarker (Get-Date).ToString('o')
  }
  foreach ($p in @(Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    $p.Refresh()
    $start = $p.StartTime.ToUniversalTime().ToString('yyyyMMddHHmmss')
    $sample = [pscustomobject]@{ Time=$now.ToString('o'); PID=$p.Id; StartUTC=$start; PrivateBytes=$p.PrivateMemorySize64; WorkingSetBytes=$p.WorkingSet64; Handles=$p.HandleCount; FreePhysicalKB=$os.FreePhysicalMemory; FreeVirtualKB=$os.FreeVirtualMemory }
    $sample | Export-Csv -LiteralPath $log -NoTypeInformation -Append -Encoding UTF8
    $marker = Join-Path $base ("captured-{0}-{1}.txt" -f $p.Id,$start)
    if (($p.PrivateMemorySize64 -ge 2GB -and !(Test-Path $marker)) -or $TestSnapshot) {
      if (!$TestSnapshot) { Send-MemoryAlert ("檔案總管私人記憶體已達 {0:N2} GB，正在保存診斷資料。" -f ($p.PrivateMemorySize64 / 1GB)) }
      $dir = Join-Path $base ("capture-{0}-{1}-{2}" -f $now.ToString('yyyyMMdd-HHmmss'),$p.Id,([guid]::NewGuid().ToString('N').Substring(0,8)))
      New-Item -ItemType Directory -Path $dir | Out-Null
      $sample | Format-List | Out-File (Join-Path $dir 'process.txt') -Encoding UTF8
      $p | Select-Object Id,ProcessName,StartTime,PrivateMemorySize64,WorkingSet64,VirtualMemorySize64,PagedMemorySize64,HandleCount,Threads | Format-List | Out-File (Join-Path $dir 'process.txt') -Append -Encoding UTF8
      try { $p.Modules | Select-Object ModuleName,FileName,FileVersionInfo | Export-Csv (Join-Path $dir 'modules.csv') -NoTypeInformation -Encoding UTF8 } catch { $_ | Out-File (Join-Path $dir 'modules-error.txt') }
      Get-Process | Sort-Object PrivateMemorySize64 -Descending | Select-Object -First 30 Id,ProcessName,PrivateMemorySize64,WorkingSet64 | Export-Csv (Join-Path $dir 'top-processes.csv') -NoTypeInformation -Encoding UTF8
      $os | Select-Object Caption,LastBootUpTime,TotalVisibleMemorySize,FreePhysicalMemory,TotalVirtualMemorySize,FreeVirtualMemory | Format-List | Out-File (Join-Path $dir 'system.txt') -Encoding UTF8
      Get-CimInstance Win32_PageFileUsage | Format-List Name,AllocatedBaseSize,CurrentUsage,PeakUsage | Out-File (Join-Path $dir 'system.txt') -Append -Encoding UTF8
      if (!$TestSnapshot) {
        try {
          $free = (Get-CimInstance Win32_LogicalDisk -Filter "DeviceID = 'C:'").FreeSpace
          $required = [long](5GB + 2.5 * $p.PrivateMemorySize64)
          if ($free -lt $required) { throw "Insufficient free space. Required: $required; available: $free" }
          Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class ExplorerDump {
 [DllImport("dbghelp.dll", SetLastError=true)]
 static extern bool MiniDumpWriteDump(IntPtr process, int pid, IntPtr file, uint type, IntPtr ex, IntPtr user, IntPtr callback);
 public static void Write(int pid, string path) {
  using (var p = Process.GetProcessById(pid))
  using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
   if (!MiniDumpWriteDump(p.Handle, pid, f.SafeFileHandle.DangerousGetHandle(), 0x00000002 | 0x00001000 | 0x00000800, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  }
 }
}
'@
          [ExplorerDump]::Write($p.Id,(Join-Path $dir 'explorer-full.dmp'))
          'Full dump completed.' | Out-File (Join-Path $dir 'dump-status.txt')
        } catch { $_ | Out-String | Out-File (Join-Path $dir 'dump-status.txt'); Remove-Item (Join-Path $dir 'explorer-full.dmp') -Force -ErrorAction SilentlyContinue }
        $dir | Out-File $marker -Encoding UTF8
      }
    }
  }
} catch {
  ("{0} {1}" -f (Get-Date).ToString('o'),($_ | Out-String)) | Add-Content (Join-Path $base 'errors.log') -Encoding UTF8
  exit 1
}

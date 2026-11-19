Option Explicit
Dim shell, exe, script, command, result
Set shell = CreateObject("WScript.Shell")
exe = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
script = "C:\ProgramData\ExplorerMemoryMonitor\monitor.ps1"
command = Chr(34) & exe & Chr(34) & " -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " & Chr(34) & script & Chr(34)
result = shell.Run(command, 0, True)
WScript.Quit result

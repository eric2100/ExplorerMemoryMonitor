using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

namespace ExplorerMemoryMonitor {
  static class MonitorProgram {
    const long GiB=1024L*1024*1024;
    static readonly string root=@"C:\ProgramData\ExplorerMemoryMonitor";
    static readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ExplorerMemoryMonitor");
    internal static bool RestartEligible(long privateBytes, bool disabled, double age, bool restartedThisBoot, bool dumpReady, bool sameSession, int explorers) {
      return privateBytes>=4*GiB && !disabled && age>=4 && age<=20 && !restartedThisBoot && dumpReady && sameSession && explorers==1;
    }
    [StructLayout(LayoutKind.Sequential)] class MemoryStatus { public uint length=(uint)Marshal.SizeOf(typeof(MemoryStatus)), load; public ulong total,available,totalPage,availablePage,totalVirtual,availableVirtual,extended; }
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus m);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("dbghelp.dll",SetLastError=true)] static extern bool MiniDumpWriteDump(IntPtr process,int pid,IntPtr file,uint type,IntPtr ex,IntPtr user,IntPtr callback);
    [STAThread] static int Main(string[] args) {
      try { Directory.CreateDirectory(root); Run(args.Contains("--test-snapshot")); return 0; }
      catch(Exception e) { try { File.AppendAllText(Path.Combine(root,"monitor-csharp-errors.log"),DateTime.Now.ToString("o")+" "+e+Environment.NewLine); } catch {} return 1; }
    }
    static void Alert(string text) {
      try { using(var n=new NotifyIcon()) { n.Icon=SystemIcons.Warning; n.Visible=true; n.ShowBalloonTip(10000,"記憶體監控提醒",text,ToolTipIcon.Warning); Thread.Sleep(3000); n.Visible=false; } } catch {} 
    }
    static void Run(bool test) {
      var m=new MemoryStatus(); if(!GlobalMemoryStatusEx(m)) throw new InvalidOperationException("Cannot read physical memory.");
      DateTime now=DateTime.Now; string low=Path.Combine(root,"low-memory-alert.txt");
      if(!test && m.available<(ulong)GiB && (!File.Exists(low)||(now-File.GetLastWriteTime(low)).TotalMinutes>=30)) { File.WriteAllText(low,now.ToString("o")); Alert("可用實體記憶體不足 1 GB，請儲存工作並檢查工作管理員。"); }
      int session=Process.GetCurrentProcess().SessionId;
      foreach(var p in Process.GetProcessesByName("explorer")) using(p) {
        try {
          p.Refresh(); string start=p.StartTime.ToUniversalTime().ToString("yyyyMMddHHmmss");
          string marker=Path.Combine(root,String.Format("captured-{0}-{1}.txt",p.Id,start));
          if(!test) File.AppendAllText(Path.Combine(root,"samples-csharp.csv"),String.Format("{0:o},{1},{2},{3},{4},{5}\r\n",now,p.Id,start,p.PrivateMemorySize64,p.WorkingSet64,m.available));
          if(test || (p.PrivateMemorySize64>=2*GiB && !File.Exists(marker))) {
            if(!test) Alert("檔案總管的私人記憶體已達 2 GB，正在保存診斷資料。");
            string dir=Path.Combine(root,"capture-"+now.ToString("yyyyMMdd-HHmmss")+"-"+p.Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"process.txt"),String.Format("Time: {0:o}\r\nPID: {1}\r\nStartUTC: {2}\r\nPrivateBytes: {3}\r\nWorkingSetBytes: {4}\r\nAvailablePhysical: {5}\r\n",now,p.Id,start,p.PrivateMemorySize64,p.WorkingSet64,m.available));
            try { using(var w=new StreamWriter(Path.Combine(dir,"modules.csv"))) foreach(ProcessModule mod in p.Modules) w.WriteLine("\""+mod.ModuleName.Replace("\"","\"\"")+"\",\""+mod.FileName.Replace("\"","\"\"")+"\""); } catch(Exception e) { File.WriteAllText(Path.Combine(dir,"modules-error.txt"),e.Message); }
            using(var w=new StreamWriter(Path.Combine(dir,"top-processes.csv"))) { foreach(var q in Process.GetProcesses().OrderByDescending(x => {try{return x.PrivateMemorySize64;}catch{return 0;}}).Take(30)) using(q) { try {w.WriteLine("{0},{1},{2},{3}",q.Id,q.ProcessName,q.PrivateMemorySize64,q.WorkingSet64);} catch{} } }
            if(!test) {
              string path=Path.Combine(dir,"explorer-full.dmp");
              try {
                var drive=new DriveInfo(Path.GetPathRoot(root)); long need=5*GiB+(long)(2.5*p.PrivateMemorySize64); if(drive.AvailableFreeSpace<need) throw new IOException("Insufficient disk space for full dump.");
                using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) if(!MiniDumpWriteDump(p.Handle,p.Id,f.SafeFileHandle.DangerousGetHandle(),0x00000002|0x00001000|0x00000800,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                File.WriteAllText(Path.Combine(dir,"dump-status.txt"),"Full dump completed.");
              } catch(Exception e) { File.WriteAllText(Path.Combine(dir,"dump-status.txt"),e.ToString()); try {File.Delete(path);}catch{} }
              File.WriteAllText(marker,dir);
            }
          }
          if(!test) CheckRestart(p,start,marker,session,now);
        } catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {} 
      }
    }
    static void CheckRestart(Process p,string start,string marker,int session,DateTime now) {
      string disabled=Path.Combine(settings,"auto-explorer-restart-disabled"), high=Path.Combine(root,String.Format("high-{0}-{1}.txt",p.Id,start)), last=Path.Combine(root,"last-explorer-restart.txt");
      if(p.PrivateMemorySize64<4*GiB || File.Exists(disabled)) { if(File.Exists(high)) File.Delete(high); return; }
      if(!File.Exists(high)) File.WriteAllText(high,now.ToString("o"));
      double age=(now-File.GetLastWriteTime(high)).TotalMinutes;
      if(age>20) {File.WriteAllText(high,now.ToString("o"));return;}
      DateTime boot=DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64());
      bool restarted=File.Exists(last) && File.GetLastWriteTimeUtc(last)>=boot, dumpReady=false;
      if(File.Exists(marker)) { string dir=File.ReadAllText(marker).Trim(), dump=Path.Combine(dir,"explorer-full.dmp"), status=Path.Combine(dir,"dump-status.txt"); dumpReady=File.Exists(dump)&&new FileInfo(dump).Length>0&&File.Exists(status)&&File.ReadAllText(status).Contains("Full dump completed"); }
      int count=0; foreach(var x in Process.GetProcessesByName("explorer")) using(x) {try {if(x.SessionId==session)count++;}catch{}}
      if(!RestartEligible(p.PrivateMemorySize64,File.Exists(disabled),age,restarted,dumpReady,p.SessionId==session,count)) return;
      Alert("檔案總管記憶體持續超過 4 GB，診斷資料已保存，15 秒後將重新啟動檔案總管。"); Thread.Sleep(12000);
      p.Refresh(); if(p.HasExited || p.StartTime.ToUniversalTime().ToString("yyyyMMddHHmmss")!=start || p.PrivateMemorySize64<4*GiB || File.Exists(disabled)) return;
      File.WriteAllText(last,DateTime.Now.ToString("o")); p.Kill(); p.WaitForExit(5000); Thread.Sleep(3000);
      bool exists=false; foreach(var x in Process.GetProcessesByName("explorer")) using(x) {try {if(x.SessionId==session)exists=true;}catch{}} if(!exists) Process.Start("explorer.exe");
      if(File.Exists(high)) File.Delete(high);
    }
  }
}

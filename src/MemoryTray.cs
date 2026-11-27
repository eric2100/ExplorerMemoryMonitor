using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExplorerMemoryMonitor {
  static class Program {
    [STAThread] static void Main() {
      bool created;
      using (var mutex = new System.Threading.Mutex(true, @"Local\ExplorerMemoryMonitorTrayCs", out created)) {
        if (!created) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
      }
    }
  }
  static class TrimPolicy {
    internal static bool Eligible(long workingSet, double observedSeconds, double foregroundSeconds, double cpuSeconds, bool completed, bool cooldownSatisfied) {
      return workingSet >= 1024L*1024*1024 && observedSeconds >= 300 && foregroundSeconds >= 300 && cpuSeconds >= 0 && cpuSeconds / observedSeconds < 0.01 && !completed && cooldownSatisfied;
    }
  }
  sealed class TrayContext : ApplicationContext {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] class MemoryStatus {
      public uint length= (uint)Marshal.SizeOf(typeof(MemoryStatus)); public uint load;
      public ulong totalPhysical, availablePhysical, totalPage, availablePage, totalVirtual, availableVirtual, extended;
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus s);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("psapi.dll", SetLastError=true)] static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    const long MiB=1024L*1024, GiB=1024L*1024*1024;
    static readonly HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "explorer", "dwm", "csrss", "winlogon", "sihost", "taskhostw", "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost", "msedge", "chrome", "firefox", "MemoryTray" };
    readonly string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExplorerMemoryMonitor");
    readonly string root = @"C:\ProgramData\ExplorerMemoryMonitor";
    readonly SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
    readonly int session = Process.GetCurrentProcess().SessionId;
    readonly NotifyIcon icon = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly ToolStripMenuItem status = new ToolStripMenuItem("記憶體") { Enabled=false };
    readonly ToolStripMenuItem automatic = new ToolStripMenuItem("閒置且占用過高時自動整理");
    readonly ToolStripMenuItem restart = new ToolStripMenuItem("檔案總管異常時自動重啟");
    readonly ToolStripMenuItem startup = new ToolStripMenuItem("登入 Windows 時啟動");
    readonly Timer timer = new Timer();
    readonly Dictionary<string, Sample> samples = new Dictionary<string, Sample>();
    DateTime lastAction = DateTime.MinValue;
    class Sample { public DateTime Since; public TimeSpan Cpu; public DateTime LastForeground; }
    string Disabled { get { return Path.Combine(config,"idle-trim-disabled"); } }
    string RestartDisabled { get { return Path.Combine(config,"auto-explorer-restart-disabled"); } }
    string ActionTime { get { return Path.Combine(config,"idle-trim-last.txt"); } }
    string DoneDir { get { return Path.Combine(config,"idle-trim-completed"); } }
    public TrayContext() {
      Directory.CreateDirectory(config); Directory.CreateDirectory(DoneDir);
      if (File.Exists(ActionTime)) lastAction=File.GetLastWriteTimeUtc(ActionTime);
      automatic.Checked=!File.Exists(Disabled); restart.Checked=!File.Exists(RestartDisabled);
      automatic.Click += delegate { Toggle(automatic, Disabled); };
      restart.Click += delegate { Toggle(restart, RestartDisabled); };
      startup.Checked=IsStartup(); startup.Click += delegate { SetStartup(!startup.Checked); };
      menu.Items.Add(status); menu.Items.Add(automatic); menu.Items.Add(restart); menu.Items.Add(startup);
      menu.Items.Add("整理目前符合條件的閒置程式", null, delegate { Scan(true); });
      menu.Items.Add("開啟工作管理員", null, delegate { Process.Start("taskmgr.exe"); });
      menu.Items.Add("開啟診斷資料夾", null, delegate { Process.Start("explorer.exe", root); });
      menu.Items.Add("結束顯示", null, delegate { ExitThread(); });
      icon.ContextMenuStrip=menu; icon.Visible=true;
      timer.Interval=15000; timer.Tick += delegate { RefreshIcon(); Scan(false); };
      RefreshIcon(); timer.Start();
    }
    void Toggle(ToolStripMenuItem item, string path) {
      try { if (item.Checked) File.WriteAllText(path,"disabled"); else if(File.Exists(path)) File.Delete(path); item.Checked=!item.Checked; }
      catch(Exception e) { Notify("設定失敗", e.Message, ToolTipIcon.Warning); }
    }
    bool IsStartup() { using (var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k!=null && k.GetValue("ExplorerMemoryTray")!=null; }
    void SetStartup(bool enabled) {
      try { using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(enabled) k.SetValue("ExplorerMemoryTray", "\""+Application.ExecutablePath+"\""); else k.DeleteValue("ExplorerMemoryTray",false); } startup.Checked=enabled; }
      catch(Exception e) { Notify("設定失敗",e.Message,ToolTipIcon.Warning); }
    }
    void Notify(string title, string message, ToolTipIcon kind) { icon.ShowBalloonTip(10000,title,message,kind); }
    void RefreshIcon() {
      var m=new MemoryStatus(); if(!GlobalMemoryStatusEx(m) || m.totalPhysical==0) return;
      int percent=(int)(m.availablePhysical*100/m.totalPhysical);
      icon.Text=String.Format("可用記憶體：{0}%（{1:F1} / {2:F1} GB）",percent,(double)m.availablePhysical/GiB,(double)m.totalPhysical/GiB);
      status.Text=icon.Text;
      using(var bmp=new Bitmap(32,32)) using(var g=Graphics.FromImage(bmp)) using(var num=new Font("Arial",percent>=100?13:percent>=10?20:23,FontStyle.Bold,GraphicsUnit.Pixel)) using(var sign=new Font("Arial",7,FontStyle.Bold,GraphicsUnit.Pixel)) using(var fmt=(StringFormat)StringFormat.GenericTypographic.Clone()) {
        g.Clear(m.availablePhysical<(ulong)GiB?Color.DarkRed:m.availablePhysical<(ulong)(2*GiB)?Color.DarkOrange:Color.DarkGreen);
        string value=percent.ToString(); float w=g.MeasureString(value,num,32,fmt).Width, sw=g.MeasureString("%",sign,32,fmt).Width, x=Math.Max(0,(32-w-sw)/2);
        g.DrawString(value,num,Brushes.White,x,(32-num.GetHeight(g))/2,fmt); g.DrawString("%",sign,Brushes.White,x+w,4,fmt);
        IntPtr h=bmp.GetHicon(); using(var temporary=Icon.FromHandle(h)) { Icon next=(Icon)temporary.Clone(); Icon old=icon.Icon; icon.Icon=next; if(old!=null) old.Dispose(); } DestroyIcon(h);
      }
    }
    bool SameOwner(IntPtr process) {
      IntPtr token; if(!OpenProcessToken(process,8,out token)) return false;
      try { using(var id=new WindowsIdentity(token)) return currentUser.Equals(id.User); } finally { CloseHandle(token); }
    }
    void Scan(bool manual) {
      if(!manual && !automatic.Checked) return;
      bool acted=false;
      DateTime now=DateTime.UtcNow; uint foreground; GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
      var live=new HashSet<string>();
      foreach(var p in Process.GetProcesses()) using(p) {
        try {
          if(p.Id==Process.GetCurrentProcess().Id || p.SessionId!=session || excluded.Contains(p.ProcessName) || p.MainWindowHandle==IntPtr.Zero) continue;
          string key=p.Id+"-"+p.StartTime.ToUniversalTime().Ticks; live.Add(key);
          Sample sample; TimeSpan cpu=p.TotalProcessorTime;
          if(!samples.TryGetValue(key,out sample)) { sample=new Sample { Since=now, Cpu=cpu, LastForeground=now }; samples[key]=sample; }
          if(p.Id==(int)foreground) { sample.Since=now; sample.Cpu=cpu; sample.LastForeground=now; continue; }
          double seconds=(now-sample.Since).TotalSeconds;
          if(seconds<300 || (now-sample.LastForeground).TotalSeconds<300) continue;
          double cpuSeconds=(cpu-sample.Cpu).TotalSeconds;
          if(cpuSeconds<0 || cpuSeconds/seconds>=0.01) { sample.Since=now; sample.Cpu=cpu; continue; }
          if(!TrimPolicy.Eligible(p.WorkingSet64,seconds,(now-sample.LastForeground).TotalSeconds,cpuSeconds,File.Exists(Path.Combine(DoneDir,key+".txt")),manual || (now-lastAction).TotalMinutes>=30)) continue;
          uint foregroundAgain; GetWindowThreadProcessId(GetForegroundWindow(),out foregroundAgain);
          if(p.Id==(int)foregroundAgain) continue;
          IntPtr h=OpenProcess(0x1500,false,p.Id); if(h==IntPtr.Zero) continue;
          bool succeeded=false;
          try { if(SameOwner(h)) { File.WriteAllText(Path.Combine(DoneDir,key+".txt"),now.ToString("o")); File.WriteAllText(ActionTime,now.ToString("o")); lastAction=now; succeeded=EmptyWorkingSet(h); } }
          finally { CloseHandle(h); }
          if(succeeded) { acted=true; Notify("已整理閒置程式",p.ProcessName+" 閒置且記憶體占用超過 1 GB，已嘗試整理一次。",ToolTipIcon.Info); }
          if(succeeded || lastAction==now) break;
        } catch { }
      }
      var stale=new List<string>(); foreach(var pair in samples) if(!live.Contains(pair.Key)) stale.Add(pair.Key); foreach(var key in stale) samples.Remove(key);
      if(manual && !acted) Notify("沒有需要整理的程式","目前沒有符合閒置及記憶體條件的程式。",ToolTipIcon.Info);
    }
    protected override void ExitThreadCore() { timer.Stop(); timer.Dispose(); icon.Visible=false; if(icon.Icon!=null) icon.Icon.Dispose(); icon.Dispose(); menu.Dispose(); base.ExitThreadCore(); }
  }
}

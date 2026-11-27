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
    internal static bool Eligible(long workingSet, long threshold, double observedSeconds, double foregroundSeconds, double cpuSeconds, bool completed, bool cooldownSatisfied) {
      return workingSet >= threshold && observedSeconds >= 300 && foregroundSeconds >= 300 && cpuSeconds >= 0 && cpuSeconds / observedSeconds < 0.01 && !completed && cooldownSatisfied;
    }
    internal static bool AutomaticAllowed(bool pressure, bool latch, double minutesSinceAction) { return minutesSinceAction>=30 && (!pressure || !latch); }
    internal static bool RecoveryComplete(double minutesAboveThreshold) { return minutesAboveThreshold>=5; }
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
    readonly string config;
    readonly string root = @"C:\ProgramData\ExplorerMemoryMonitor";
    readonly SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
    readonly int session = Process.GetCurrentProcess().SessionId;
    readonly NotifyIcon icon = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly ToolStripMenuItem status = new ToolStripMenuItem("記憶體") { Enabled=false };
    readonly ToolStripMenuItem automatic = new ToolStripMenuItem("自動整理占用偏高的閒置程式");
    readonly ToolStripMenuItem restart = new ToolStripMenuItem("檔案總管記憶體過高時自動重啟");
    readonly ToolStripMenuItem startup = new ToolStripMenuItem("登入 Windows 時啟動");
    readonly Timer timer = new Timer();
    readonly Dictionary<string, Sample> samples = new Dictionary<string, Sample>();
    Form settingsWindow;
    Label settingsMemory;
    DateTime lastAction = DateTime.MinValue;
    class Sample { public DateTime Since; public TimeSpan Cpu; public DateTime LastForeground; }
    string Disabled { get { return Path.Combine(config,"idle-trim-disabled"); } }
    string RestartDisabled { get { return Path.Combine(config,"auto-explorer-restart-disabled"); } }
    string ActionTime { get { return Path.Combine(config,"idle-trim-last.txt"); } }
    string PressureLatch { get { return Path.Combine(config,"low-memory-episode.txt"); } }
    string RecoveryMarker { get { return Path.Combine(config,"memory-recovery.txt"); } }
    string DoneDir { get { return Path.Combine(config,"idle-trim-completed"); } }
    public TrayContext() : this(null) { }
    internal TrayContext(string configOverride) {
      config=configOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExplorerMemoryMonitor");
      Directory.CreateDirectory(config); Directory.CreateDirectory(DoneDir);
      if (File.Exists(ActionTime)) lastAction=File.GetLastWriteTimeUtc(ActionTime);
      automatic.Checked=!File.Exists(Disabled); restart.Checked=!File.Exists(RestartDisabled);
      automatic.Click += delegate { Toggle(automatic, Disabled); };
      restart.Click += delegate { Toggle(restart, RestartDisabled); };
      startup.Checked=IsStartup(); startup.Click += delegate { SetStartup(!startup.Checked); };
      menu.Items.Add(status);
      menu.Items.Add("開啟設定", null, delegate { ShowSettings(); });
      menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add(automatic); menu.Items.Add(restart); menu.Items.Add(startup);
      menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add("立即整理符合條件的閒置程式", null, delegate { SafeScan(true); });
      menu.Items.Add("開啟工作管理員", null, delegate { Process.Start("taskmgr.exe"); });
      menu.Items.Add("開啟診斷資料夾", null, delegate { Process.Start("explorer.exe", root); });
      menu.Items.Add("結束系統匣程式", null, delegate { ExitThread(); });
      icon.ContextMenuStrip=menu; icon.Visible=true;
      icon.MouseClick += delegate(object sender, MouseEventArgs e) { if(e.Button==MouseButtons.Left) ShowSettings(); };
      timer.Interval=15000; timer.Tick += delegate { RefreshIcon(); SafeScan(false); };
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
    internal void ShowSettings() {
      if(settingsWindow!=null && !settingsWindow.IsDisposed) { settingsWindow.Show(); settingsWindow.Activate(); return; }
      var form=new Form { Text="記憶體監控", ClientSize=new Size(480,540), MinimumSize=new Size(420,500), StartPosition=FormStartPosition.CenterScreen, BackColor=Color.FromArgb(246,248,250), Font=new Font("Microsoft JhengHei",10f), ShowInTaskbar=true };
      settingsWindow=form;
      var layout=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24,20,24,18), ColumnCount=1, RowCount=8, AutoScroll=true };
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,82)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
      var title=new Label { Text="記憶體監控", Dock=DockStyle.Fill, Font=new Font("Microsoft JhengHei",17f,FontStyle.Bold), ForeColor=Color.FromArgb(21,36,50), TextAlign=ContentAlignment.MiddleLeft };
      settingsMemory=new Label { Text=icon.Text, Dock=DockStyle.Fill, Font=new Font("Microsoft JhengHei",14f,FontStyle.Bold), ForeColor=Color.FromArgb(20,83,74), TextAlign=ContentAlignment.MiddleLeft, AccessibleName="目前可用記憶體" };
      var hint=new Label { Text="數值約每 15 秒更新。整理可能只暫時增加可用記憶體。", Dock=DockStyle.Fill, ForeColor=Color.FromArgb(63,78,91), TextAlign=ContentAlignment.MiddleLeft };
      layout.Controls.Add(title,0,0); layout.Controls.Add(settingsMemory,0,1); layout.Controls.Add(hint,0,2);
      AddSetting(layout,3,automatic); AddSetting(layout,4,restart); AddSetting(layout,5,startup);
      var actions=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.LeftToRight, WrapContents=true, Padding=new Padding(0,12,0,0) };
      var clean=new Button { Text="立即整理符合條件的閒置程式", AutoSize=true, MinimumSize=new Size(230,40), BackColor=Color.FromArgb(23,92,79), ForeColor=Color.White, FlatStyle=FlatStyle.Flat };
      clean.FlatAppearance.BorderSize=0; clean.Click += delegate { SafeScan(true); };
      var folder=new Button { Text="開啟診斷資料夾", AutoSize=true, MinimumSize=new Size(145,40), FlatStyle=FlatStyle.System };
      folder.Click += delegate { Process.Start("explorer.exe",root); }; actions.Controls.Add(clean); actions.Controls.Add(folder); layout.Controls.Add(actions,0,6);
      var foot=new Label { Text="關閉這個視窗後，程式會繼續在工作列執行。", Dock=DockStyle.Fill, ForeColor=Color.FromArgb(63,78,91), TextAlign=ContentAlignment.MiddleLeft };
      layout.Controls.Add(foot,0,7); form.Controls.Add(layout);
      form.FormClosed += delegate { settingsWindow=null; settingsMemory=null; };
      form.Show();
    }
    void AddSetting(TableLayoutPanel layout, int row, ToolStripMenuItem item) {
      var check=new CheckBox { Text=item.Text, Checked=item.Checked, AutoSize=false, Dock=DockStyle.Fill, ForeColor=Color.FromArgb(21,36,50), UseVisualStyleBackColor=true };
      bool updating=false;
      check.CheckedChanged += delegate { if(updating || check.Checked==item.Checked) return; item.PerformClick(); updating=true; check.Checked=item.Checked; updating=false; };
      EventHandler sync = delegate { if(check.IsDisposed) return; updating=true; check.Checked=item.Checked; updating=false; };
      item.CheckedChanged += sync;
      check.Disposed += delegate { item.CheckedChanged -= sync; };
      layout.Controls.Add(check,0,row);
    }
    void RefreshIcon() {
      var m=new MemoryStatus(); if(!GlobalMemoryStatusEx(m) || m.totalPhysical==0) return;
      int percent=(int)(m.availablePhysical*100/m.totalPhysical);
      icon.Text=String.Format("可用記憶體：{0}%（{1:F1} / {2:F1} GB）",percent,(double)m.availablePhysical/GiB,(double)m.totalPhysical/GiB);
      status.Text=icon.Text;
      if(settingsMemory!=null && !settingsMemory.IsDisposed) settingsMemory.Text=icon.Text;
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
    void SafeScan(bool manual) {
      try { Scan(manual); } catch(Exception e) { try {File.AppendAllText(Path.Combine(config,"idle-trim-errors.log"),DateTime.Now.ToString("o")+" "+e.Message+Environment.NewLine);} catch {} if(manual) Notify("整理失敗",e.Message,ToolTipIcon.Warning); }
    }
    void Scan(bool manual) {
      if(!manual && !automatic.Checked) return;
      bool acted=false;
      DateTime now=DateTime.UtcNow; uint foreground; GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
      bool pressure=false;
      if(!manual) {
        var memory=new MemoryStatus(); if(!GlobalMemoryStatusEx(memory)) return;
        if(File.Exists(PressureLatch)) {
          if(memory.availablePhysical >= (ulong)(2.5*GiB)) {
            if(!File.Exists(RecoveryMarker)) File.WriteAllText(RecoveryMarker,now.ToString("o"));
            else if(TrimPolicy.RecoveryComplete((now-File.GetLastWriteTimeUtc(RecoveryMarker)).TotalMinutes)) {File.Delete(PressureLatch);File.Delete(RecoveryMarker);}
          } else if(File.Exists(RecoveryMarker)) File.Delete(RecoveryMarker);
        }
        pressure=memory.availablePhysical < (ulong)(2*GiB);
      }
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
          long threshold=pressure ? 100*MiB : GiB;
          bool cooldown=manual || TrimPolicy.AutomaticAllowed(pressure,File.Exists(PressureLatch),(now-lastAction).TotalMinutes);
          if(!TrimPolicy.Eligible(p.WorkingSet64,threshold,seconds,(now-sample.LastForeground).TotalSeconds,cpuSeconds,File.Exists(Path.Combine(DoneDir,key+".txt")),cooldown)) continue;
          uint foregroundAgain; GetWindowThreadProcessId(GetForegroundWindow(),out foregroundAgain);
          if(p.Id==(int)foregroundAgain) continue;
          IntPtr h=OpenProcess(0x1500,false,p.Id); if(h==IntPtr.Zero) continue;
          bool succeeded=false;
          try { if(SameOwner(h)) { if(pressure) File.WriteAllText(PressureLatch,now.ToString("o")); File.WriteAllText(Path.Combine(DoneDir,key+".txt"),now.ToString("o")); File.WriteAllText(ActionTime,now.ToString("o")); lastAction=now; succeeded=EmptyWorkingSet(h); } }
          finally { CloseHandle(h); }
          if(succeeded) { acted=true; Notify("已整理閒置程式",p.ProcessName+" 符合整理條件，已嘗試整理一次。",ToolTipIcon.Info); }
          if(succeeded || lastAction==now) break;
        } catch { }
      }
      var stale=new List<string>(); foreach(var pair in samples) if(!live.Contains(pair.Key)) stale.Add(pair.Key); foreach(var key in stale) samples.Remove(key);
      if(manual && !acted) Notify("沒有符合條件的程式","程式需要符合閒置時間與記憶體條件，才會進行整理。",ToolTipIcon.Info);
    }
    protected override void ExitThreadCore() { timer.Stop(); timer.Dispose(); icon.Visible=false; if(icon.Icon!=null) icon.Icon.Dispose(); icon.Dispose(); menu.Dispose(); base.ExitThreadCore(); }
  }
}

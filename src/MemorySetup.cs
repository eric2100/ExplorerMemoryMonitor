using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;

namespace ExplorerMemoryMonitor {
  static class SetupProgram {
    static readonly string userDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ExplorerMemoryMonitor");
    static readonly string dataDir=@"C:\ProgramData\ExplorerMemoryMonitor";
    static readonly string runKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string taskName="ExplorerMemoryMonitor";
    [STAThread] static void Main() {
      Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
      var identity=WindowsIdentity.GetCurrent();
      if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) {
        try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) {UseShellExecute=true,Verb="runas"}); }
        catch(Exception e) { MessageBox.Show("請允許系統管理員權限後再執行。\n"+e.Message,"記憶體監控安裝程式",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        return;
      }
      Application.Run(new SetupForm());
    }
    internal static void ValidatePayload(string dir) {
      foreach(string name in new[]{"MemoryTray.exe","MemoryMonitor.exe"}) if(!File.Exists(Path.Combine(dir,name))) throw new FileNotFoundException("安裝包缺少 "+name+"，請重新解壓縮完整安裝包。");
    }
    internal static void CopyPayload(string source,string dest) { ValidatePayload(source); Directory.CreateDirectory(dest); foreach(string name in new[]{"MemoryTray.exe","MemoryMonitor.exe"}) File.Copy(Path.Combine(source,name),Path.Combine(dest,name),true); }
    static void StopTray() {
      string expected=Path.Combine(userDir,"MemoryTray.exe");
      foreach(var p in Process.GetProcessesByName("MemoryTray")) using(p) { try { if(String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(10000); } } catch(InvalidOperationException) {} }
    }
    internal static int Task(string arguments,bool check) {
      var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"schtasks.exe"),arguments) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
      using(var p=Process.Start(info)) { string stdout=p.StandardOutput.ReadToEnd(), stderr=p.StandardError.ReadToEnd(); p.WaitForExit(); if(check && p.ExitCode!=0)throw new InvalidOperationException("工作排程設定失敗："+stdout+" "+stderr); return p.ExitCode; }
    }
    internal static string BuildTaskXml(string executable,string sid,DateTime start) {
      string path=SecurityElement.Escape(executable), user=SecurityElement.Escape(sid), boundary=start.ToString("yyyy-MM-ddTHH:mm:ss");
      return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>"+
        "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"+
        "<Triggers><CalendarTrigger><Repetition><Interval>PT5M</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>"+boundary+"</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>"+
        "<LogonTrigger><Enabled>true</Enabled><UserId>"+user+"</UserId></LogonTrigger></Triggers>"+
        "<Principals><Principal id=\"Author\"><UserId>"+user+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>"+
        "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled><ExecutionTimeLimit>PT15M</ExecutionTimeLimit></Settings>"+
        "<Actions Context=\"Author\"><Exec><Command>"+path+"</Command></Exec></Actions></Task>";
    }
    internal static void CreateTask(string name,string executable,string sid) {
      string temp=Path.Combine(Path.GetTempPath(),"memory-monitor-task-"+Guid.NewGuid().ToString("N")+".xml");
      try {File.WriteAllText(temp,BuildTaskXml(executable,sid,DateTime.Today),Encoding.Unicode); Task("/Create /TN \""+name+"\" /XML \""+temp+"\" /F",true);}
      finally {if(File.Exists(temp))File.Delete(temp);}
    }
    static void WaitForMonitor() {
      string expected=Path.Combine(userDir,"MemoryMonitor.exe");
      foreach(var p in Process.GetProcessesByName("MemoryMonitor")) using(p) {
        try { if(String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase) && !p.WaitForExit(10000)) throw new InvalidOperationException("診斷排程仍在執行，請稍後再試。"); }
        catch(InvalidOperationException) { if(!p.HasExited) throw; }
      }
    }
    internal static void SecureDataDirectory(string path) {
      Directory.CreateDirectory(path);
      var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
      var flags=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
      acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,flags,PropagationFlags.None,AccessControlType.Allow));
      acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),FileSystemRights.FullControl,flags,PropagationFlags.None,AccessControlType.Allow));
      acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.ReadAndExecute,InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));
      Directory.SetAccessControl(path,acl);
    }
    internal static void Install(string source) {
      ValidatePayload(source);
      bool existed=File.Exists(Path.Combine(userDir,"MemoryTray.exe")); bool startup;
      using(var key=Registry.CurrentUser.OpenSubKey(runKey)) startup=key!=null && key.GetValue("ExplorerMemoryTray")!=null;
      SecureDataDirectory(dataDir); Directory.CreateDirectory(userDir);
      StopTray(); Task("/End /TN \""+taskName+"\"",false); WaitForMonitor();
      CopyPayload(source,userDir);
      string migration=Path.Combine(userDir,"idle-trim-migrated");
      if(!File.Exists(migration)) { if(File.Exists(Path.Combine(userDir,"auto-disabled")) && !File.Exists(Path.Combine(userDir,"idle-trim-disabled"))) File.WriteAllText(Path.Combine(userDir,"idle-trim-disabled"),"disabled"); File.WriteAllText(migration,"done"); }
      string executable=Path.Combine(userDir,"MemoryMonitor.exe");
      CreateTask(taskName,executable,WindowsIdentity.GetCurrent().User.Value);
      using(var key=Registry.CurrentUser.CreateSubKey(runKey)) { if(!existed || startup)key.SetValue("ExplorerMemoryTray","\""+Path.Combine(userDir,"MemoryTray.exe")+"\""); }
      Task("/Run /TN \""+taskName+"\"",true);
      Process.Start(new ProcessStartInfo(Path.Combine(userDir,"MemoryTray.exe")){UseShellExecute=true});
    }
    internal static void Uninstall() {
      StopTray();
      Task("/End /TN \""+taskName+"\"",false); WaitForMonitor();
      Task("/Delete /TN \""+taskName+"\" /F",false);
      Task("/Delete /TN \"ExplorerMemoryTray\" /F",false);
      using(var key=Registry.CurrentUser.OpenSubKey(runKey,true)) { if(key!=null)key.DeleteValue("ExplorerMemoryTray",false); }
      foreach(string file in new[]{"MemoryTray.exe","MemoryMonitor.exe"}) {string path=Path.Combine(userDir,file); if(File.Exists(path))File.Delete(path);}
    }
  }
  sealed class SetupForm : Form {
    readonly Label result=new Label();
    public SetupForm() {
      Text="記憶體監控安裝程式"; ClientSize=new Size(480,245); StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;
      Font=new Font("Microsoft JhengHei",10f); BackColor=Color.FromArgb(246,248,250);
      var title=new Label {Text="記憶體監控",Font=new Font("Microsoft JhengHei",16f,FontStyle.Bold),Location=new Point(24,20),AutoSize=true};
      var info=new Label {Text="安裝或移除系統匣程式與診斷排程。\n移除時會保留診斷資料與設定。",Location=new Point(26,65),Size=new Size(420,55)};
      var install=new Button {Text="安裝或更新",Location=new Point(28,130),Size=new Size(160,38)};
      var uninstall=new Button {Text="解除安裝",Location=new Point(200,130),Size=new Size(160,38)};
      result.Location=new Point(28,180); result.Size=new Size(420,45);
      install.Click += delegate { Execute(delegate { SetupProgram.Install(AppDomain.CurrentDomain.BaseDirectory); },"安裝完成，請查看工作列通知區。"); };
      uninstall.Click += delegate { Execute(SetupProgram.Uninstall,"已解除安裝，診斷資料與設定已保留。"); };
      Controls.AddRange(new Control[]{title,info,install,uninstall,result});
    }
    void Execute(Action action,string success) { try { action(); result.Text=success; } catch(Exception e) { result.Text="無法完成，請查看錯誤訊息。"; MessageBox.Show(e.Message,"記憶體監控安裝程式",MessageBoxButtons.OK,MessageBoxIcon.Error); } }
  }
}

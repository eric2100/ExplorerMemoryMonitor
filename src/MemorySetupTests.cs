using System;
using System.IO;
namespace ExplorerMemoryMonitor {
  static class SetupTests {
    static void Main() {
      string temp=Path.GetFullPath(Path.GetTempPath());
      string dir=Path.GetFullPath(Path.Combine(temp,"memory-setup-test-"+Guid.NewGuid().ToString("N")));
      if(!dir.StartsWith(temp,StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test directory");
      string name="ExplorerMemoryMonitorTest-"+Guid.NewGuid().ToString("N");
      try {
        string source=Path.Combine(dir,"source"), target=Path.Combine(dir,"target"); Directory.CreateDirectory(source);
        bool rejected=false; try {SetupProgram.ValidatePayload(source);} catch(FileNotFoundException) {rejected=true;} if(!rejected)throw new Exception("Missing payload accepted");
        File.WriteAllText(Path.Combine(source,"MemoryTray.exe"),"tray"); File.WriteAllText(Path.Combine(source,"MemoryMonitor.exe"),"monitor");
        SetupProgram.CopyPayload(source,target);
        if(File.ReadAllText(Path.Combine(target,"MemoryTray.exe"))!="tray" || File.ReadAllText(Path.Combine(target,"MemoryMonitor.exe"))!="monitor")throw new Exception("Payload copy failed");
        string secure=Path.Combine(dir,"secure"); SetupProgram.SecureDataDirectory(secure);
        if(!Directory.GetAccessControl(secure).AreAccessRulesProtected) throw new Exception("Data directory inherits access");
        string exe=@"C:\Program Files\Example & App\MemoryMonitor.exe";
        SetupProgram.CreateTask(name,exe,System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);
        var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"schtasks.exe"),"/Query /TN \""+name+"\" /XML") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
        using(var p=System.Diagnostics.Process.Start(info)){string xml=p.StandardOutput.ReadToEnd();p.WaitForExit();var doc=new System.Xml.XmlDocument();doc.LoadXml(xml);var ns=new System.Xml.XmlNamespaceManager(doc.NameTable);ns.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");var command=doc.SelectSingleNode("//t:Actions/t:Exec/t:Command",ns);if(p.ExitCode!=0 || command==null || command.InnerText!=exe || !xml.Contains("PT5M") || !xml.Contains("InteractiveToken") || !xml.Contains("HighestAvailable") || !xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>") || !xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>") || !xml.Contains("<LogonTrigger>") || !xml.Contains("PT15M"))throw new Exception("Task settings not preserved");}
        Console.WriteLine("Payload validation, copying, data ACL and scheduled task passed");
      } finally { SetupProgram.Task("/Delete /TN \""+name+"\" /F",false); if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }
  }
}

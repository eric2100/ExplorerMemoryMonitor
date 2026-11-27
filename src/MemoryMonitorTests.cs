using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
namespace ExplorerMemoryMonitor {
  static class MonitorTests {
    static void Check(bool actual,bool expected,string label){if(actual!=expected)throw new Exception(label);}
    static void Main(string[] args) {
      if(args.Length>0 && args[0]=="child") {Thread.Sleep(30000);return;}
      long g=1024L*1024*1024;
      Check(MonitorProgram.RestartEligible(4*g,false,5,false,true,true,1),true,"eligible");
      Check(MonitorProgram.RestartEligible(4*g-1,false,5,false,true,true,1),false,"threshold");
      Check(MonitorProgram.RestartEligible(4*g,true,5,false,true,true,1),false,"disabled");
      Check(MonitorProgram.RestartEligible(4*g,false,3,false,true,true,1),false,"wait");
      Check(MonitorProgram.RestartEligible(4*g,false,5,true,true,true,1),false,"once per boot");
      Check(MonitorProgram.RestartEligible(4*g,false,5,false,false,true,1),false,"dump required");
      Check(MonitorProgram.RestartEligible(4*g,false,5,false,true,false,1),false,"session");
      Check(MonitorProgram.RestartEligible(4*g,false,5,false,true,true,2),false,"one shell");
      string dump=Path.Combine(Path.GetTempPath(),"memory-monitor-test-"+Guid.NewGuid().ToString("N")+".dmp");
      var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"child") { UseShellExecute=false,CreateNoWindow=true };
      using(var child=Process.Start(start)) try {
        Thread.Sleep(1000);
        var fn=typeof(MonitorProgram).GetMethod("MiniDumpWriteDump",BindingFlags.NonPublic|BindingFlags.Static);
        using(var file=new FileStream(dump,FileMode.CreateNew,FileAccess.Write)) {
          bool ok=(bool)fn.Invoke(null,new object[]{child.Handle,child.Id,file.SafeFileHandle.DangerousGetHandle(),(uint)0x00000002,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero});
          if(!ok) throw new Exception("Synthetic child full dump failed: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
        if(new FileInfo(dump).Length==0)throw new Exception("Empty test dump");
      } finally { if(!child.HasExited)child.Kill(); child.WaitForExit(); if(File.Exists(dump))File.Delete(dump); }
      Console.WriteLine("8 restart safeguards and synthetic full dump passed");
    }
  }
}

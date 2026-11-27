using System;
using System.Diagnostics;
using System.Security.Principal;

namespace ExplorerMemoryMonitor {
  static class ForceCleanerTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main(string[] args) {
      if (args.Length > 0) {
        byte[] data = new byte[32 * 1024 * 1024];
        for (int i = 0; i < data.Length; i += 4096) data[i] = 123;
        Console.WriteLine("ready"); Console.ReadLine();
        long sum = 0; for (int i = 0; i < data.Length; i += 4096) sum += data[i];
        Console.WriteLine(sum); return 0;
      }
      var info = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "child") {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
      };
      using (var child = Process.Start(info)) {
        try {
          Assert(child.StandardOutput.ReadLine() == "ready", "Child did not start");
          var user = WindowsIdentity.GetCurrent().User;
          int session = Process.GetCurrentProcess().SessionId, own = Process.GetCurrentProcess().Id;
          Assert(!ForceCleaner.TryTrim(child, user, session + 1, own), "Wrong session accepted");
          Assert(!ForceCleaner.TryTrim(child, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), session, own), "Wrong owner accepted");
          Assert(!ForceCleaner.TryTrim(child, user, session, child.Id), "Own process accepted");
          Assert(ForceCleaner.TryTrim(child, user, session, own), "Child trim failed");
          Assert(!child.HasExited, "Child was terminated");
          child.StandardInput.WriteLine("continue");
          Assert(child.StandardOutput.ReadLine() == (8192L * 123).ToString(), "Child data changed");
          Assert(child.WaitForExit(5000) && child.ExitCode == 0, "Child did not finish");
        } finally { if (!child.HasExited) child.Kill(); }
      }
      Console.WriteLine("ForceCleaner tests passed"); return 0;
    }
  }
}

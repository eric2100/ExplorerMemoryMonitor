using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace ExplorerMemoryMonitor {
  static class SettingsTests {
    [STAThread] static void Main() {
      string tempRoot=Path.GetFullPath(Path.GetTempPath());
      string dir=Path.GetFullPath(Path.Combine(tempRoot,"memory-settings-test-"+Guid.NewGuid().ToString("N")));
      if(!dir.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test path");
      var app=new TrayContext(dir);
      try {
        app.ShowSettings(); Application.DoEvents();
        var form=(Form)typeof(TrayContext).GetField("settingsWindow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
        var item=(ToolStripMenuItem)typeof(TrayContext).GetField("automatic",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
        CheckBox check=null;
        foreach(Control c in ((TableLayoutPanel)form.Controls[0]).Controls) { var b=c as CheckBox; if(b!=null && b.Text==item.Text) check=b; }
        if(check==null || !check.Checked || !item.Checked) throw new Exception("Initial toggle is incorrect");
        check.Checked=false;
        if(item.Checked || !File.Exists(Path.Combine(dir,"idle-trim-disabled"))) throw new Exception("Disabling did not persist");
        item.PerformClick();
        if(!check.Checked || !item.Checked || File.Exists(Path.Combine(dir,"idle-trim-disabled"))) throw new Exception("Menu and settings are not synchronized");
        form.Close(); app.ExitThread();
        Console.WriteLine("Settings and tray toggles synchronize and persist");
      } finally { app.ExitThread(); if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }
  }
}

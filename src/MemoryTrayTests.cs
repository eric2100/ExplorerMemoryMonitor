using System;
namespace ExplorerMemoryMonitor {
  static class Tests {
    static void Check(bool actual, bool expected, string name) { if(actual!=expected) throw new Exception(name); }
    static void Main() {
      long g=1024L*1024*1024;
      Check(TrimPolicy.Eligible(g,300,300,1,false,true),true,"idle target");
      Check(TrimPolicy.Eligible(g-1,300,300,1,false,true),false,"small working set");
      Check(TrimPolicy.Eligible(g,299,300,1,false,true),false,"observation duration");
      Check(TrimPolicy.Eligible(g,300,299,1,false,true),false,"recent foreground");
      Check(TrimPolicy.Eligible(g,300,300,3,false,true),false,"CPU active");
      Check(TrimPolicy.Eligible(g,300,300,1,true,true),false,"once per process");
      Check(TrimPolicy.Eligible(g,300,300,1,false,false),false,"cooldown");
      Console.WriteLine("7 idle eligibility checks passed");
    }
  }
}

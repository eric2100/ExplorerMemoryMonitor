using System;
namespace ExplorerMemoryMonitor {
  static class Tests {
    static void Check(bool actual, bool expected, string name) { if(actual!=expected) throw new Exception(name); }
    static void Main() {
      long g=1024L*1024*1024;
      Check(TrimPolicy.Eligible(g,g,300,300,1,false,true),true,"idle target");
      Check(TrimPolicy.Eligible(g-1,g,300,300,1,false,true),false,"small working set");
      Check(TrimPolicy.Eligible(g,g,299,300,1,false,true),false,"observation duration");
      Check(TrimPolicy.Eligible(g,g,300,299,1,false,true),false,"recent foreground");
      Check(TrimPolicy.Eligible(g,g,300,300,3,false,true),false,"CPU active");
      Check(TrimPolicy.Eligible(g,g,300,300,1,true,true),false,"once per process");
      Check(TrimPolicy.Eligible(g,g,300,300,1,false,false),false,"cooldown");
      Check(TrimPolicy.Eligible(100*1024*1024,100*1024*1024,300,300,1,false,true),true,"low-memory threshold");
      Check(TrimPolicy.AutomaticAllowed(true,true,60),false,"one attempt per low-memory episode");
      Check(TrimPolicy.AutomaticAllowed(true,false,29),false,"low-memory cooldown");
      Check(TrimPolicy.AutomaticAllowed(true,false,30),true,"first attempt after cooldown");
      Check(TrimPolicy.RecoveryComplete(4.9),false,"recovery wait");
      Check(TrimPolicy.RecoveryComplete(5),true,"recovery rearm");
      Console.WriteLine("13 idle and low-memory policy checks passed");
    }
  }
}

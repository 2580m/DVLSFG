using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
namespace DVLSFG {
    static class BootstrapWorker {
        static int Main(string[] args) {
            string mod=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            try {
                if(args.Length!=2) throw new Exception("Expected game PID and start ticks");
                string root=Path.GetFullPath(Path.Combine(mod,"..",".."));
                int pid=int.Parse(args[0]); long ticks=long.Parse(args[1]);
                string mutexName="Local\\DVLSFG_bootstrap_"+root.ToUpperInvariant().GetHashCode().ToString("X8");
                using(var mutex=new Mutex(false,mutexName)) {
                    try { mutex.WaitOne(); } catch(AbandonedMutexException) {}
                    try {
                        Process game=null; if(pid>0) try { game=Process.GetProcessById(pid); } catch(ArgumentException) {}
                        if(game!=null) using(game) if(game.StartTime.ToUniversalTime().Ticks==ticks) game.WaitForExit();
                        string request=Path.Combine(mod,"bootstrap.pending"); if(!File.Exists(request)) return 0;
                        foreach(var other in Process.GetProcessesByName("DerailValley")) using(other) if(!other.HasExited) throw new IOException("A new game is running; deployment deferred");
                        string action=File.ReadAllText(request).Trim();
                        if(action!="install" && action!="remove") throw new IOException("Invalid bootstrap request");
                        bool active=action=="install" && !File.Exists(Path.Combine(mod,"bootstrap.disabled"));
                        BootstrapDeployment.Apply(root,mod,active);
                        File.Delete(request);
                        File.WriteAllText(Path.Combine(mod,"deployment.log"),active?"Automatic startup installed":"Automatic startup removed");
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch(Exception e) { File.WriteAllText(Path.Combine(mod,"deployment.log"),e.ToString()); return 1; }
        }
    }
}

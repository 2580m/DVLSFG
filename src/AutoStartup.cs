using System;
using System.IO;
using System.Diagnostics;
using UnityModManagerNet;
namespace DVLSFG {
    static class AutoStartup {
        static string mod,root;
        static bool workerStarted;
        static bool installed;
        internal static string Error;
        internal static bool Installed { get { return installed; } }
        internal static void Initialize(UnityModManager.ModEntry entry) {
            mod=Path.GetFullPath(entry.Path).TrimEnd(Path.DirectorySeparatorChar); root=Path.GetFullPath(Path.Combine(mod,"..",".."));
            if(entry.Enabled) Request(entry,true);
        }
        internal static void Request(UnityModManager.ModEntry entry,bool enabled) {
            try {
                BootstrapDeployment.Validate(root,mod,enabled);
                string disabled=Path.Combine(mod,"bootstrap.disabled");
                if(enabled) { if(File.Exists(disabled)) File.Delete(disabled); }
                else File.WriteAllText(disabled,"Disabled by UMM");
                File.WriteAllText(Path.Combine(mod,"bootstrap.pending"),enabled?"install":"remove");
                // A missing bootstrap can be copied now, but only a subsequent
                // ordinary launch will load it before graphics initialization.
                if(enabled && !File.Exists(Path.Combine(root,"vulkan-1.dll"))) BootstrapDeployment.Apply(root,mod,true);
                installed=BootstrapDeployment.Installed(root,mod);
                if(!workerStarted) {
                    using(var game=Process.GetCurrentProcess()) {
                        var start=new ProcessStartInfo(Path.Combine(mod,"DVLSFG.BootstrapWorker.exe"),game.Id+" "+game.StartTime.ToUniversalTime().Ticks);
                        start.UseShellExecute=false; start.CreateNoWindow=true; start.WindowStyle=ProcessWindowStyle.Hidden;
                        using(var worker=Process.Start(start)) { if(worker==null) throw new IOException("Automatic startup worker failed to start"); }
                    }
                    workerStarted=true;
                }
                Error=null;
                entry.Logger.Log(enabled?"Automatic LSFG startup installation requested; ordinary launches work after restart":"Automatic LSFG startup removal queued for game exit");
            } catch(Exception e) {Error=e.Message;entry.Logger.Error("Automatic LSFG startup: "+e.Message);}
        }
    }
}

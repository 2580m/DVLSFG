using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml;
namespace DVLSFG {
    class Launcher {
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Probe(IntPtr path,StringBuilder message,uint size);
        static int Main(string[] args) {
            string mod=AppDomain.CurrentDomain.BaseDirectory;
            try {
                bool probe=args.Length==1 && args[0]=="--probe";
                bool plain=args.Length==1 && args[0]=="--without-lsfg";
                var s=plain?new Settings():Settings.Read(Path.Combine(mod,"config.ini"));
                if(probe) return Check(mod,s.DllPath) ? 0 : 1;
                string root=Path.GetFullPath(Path.Combine(mod,"..",".."));
                string exe=Path.Combine(root,"DerailValley.exe");
                if(!File.Exists(exe)) throw new Exception("Install DVLSFG under Derail Valley/Mods/dv-scaling first.");
                foreach(var p in Process.GetProcessesByName("DerailValley")) using(p) { throw new Exception("Exit Derail Valley before launching."); }
                if(!plain) {
                    string umm=Path.Combine(root,"DerailValley_Data","Managed","UnityModManager","Params.xml");
                    if(File.Exists(umm)) {
                        var xml=new XmlDocument(); xml.XmlResolver=null;
                        var readerSettings=new XmlReaderSettings(); readerSettings.DtdProcessing=DtdProcessing.Prohibit; readerSettings.XmlResolver=null;
                        using(var reader=XmlReader.Create(umm,readerSettings)) xml.Load(reader);
                        foreach(string id in new[]{"DVVulkan","DVLSFG"}) {
                            var node=xml.SelectSingleNode("/Param/ModParams/Mod[@Id='"+id+"']");
                            if(node!=null && node.Attributes["Enabled"]!=null && node.Attributes["Enabled"].Value=="false") throw new Exception("Enable " + id + " in UMM first.");
                        }
                    }
                    if(!s.Enabled) throw new Exception("Enable DVLSFG in UMM and save settings first.");
                    if(s.FrameGenerationTechnology==1 && !Check(mod,s.DllPath)) throw new Exception("Lossless.dll is incompatible. See probe.log.");
                    foreach(string name in new[]{"d3d11.dll","dxgi.dll"}) {
                        string expected=Path.Combine(root,"Mods","DVVulkan","dxvk",name), deployed=Path.Combine(root,name);
                        if(!File.Exists(expected) || !File.Exists(deployed) || Hash(expected)!=Hash(deployed)) throw new Exception("Enable DVVulkan and restart first: missing or mismatched " + name);
                    }
                }
                var start=new ProcessStartInfo(exe,"-force-d3d11");
                start.UseShellExecute=false; start.WorkingDirectory=root;
                if(!plain) {
                    start.EnvironmentVariables.Remove("DVLSFG_NO_FRAMEGEN");
                    start.EnvironmentVariables["DVLSFG_CONFIG"]=Path.Combine(mod,"config.ini");
                    string layers=start.EnvironmentVariables["VK_INSTANCE_LAYERS"];
                    start.EnvironmentVariables["VK_INSTANCE_LAYERS"]=string.IsNullOrEmpty(layers) ? "VK_LAYER_DVLSFG_frame_generation" : layers + ";VK_LAYER_DVLSFG_frame_generation";
                    // Add our manifest without replacing existing explicit-layer discovery.
                    string paths=start.EnvironmentVariables["VK_ADD_LAYER_PATH"];
                    start.EnvironmentVariables["VK_ADD_LAYER_PATH"]=string.IsNullOrEmpty(paths) ? mod : paths + ";" + mod;
                } else {
                    start.EnvironmentVariables["DVLSFG_NO_FRAMEGEN"]="1";
                    start.EnvironmentVariables.Remove("DVLSFG_CONFIG");
                    string layers=start.EnvironmentVariables["VK_INSTANCE_LAYERS"];
                    if(!string.IsNullOrEmpty(layers)) start.EnvironmentVariables["VK_INSTANCE_LAYERS"]=layers.Replace("VK_LAYER_DVLSFG_frame_generation","");
                }
                using(var process=Process.Start(start)) { if(process==null) throw new Exception("Game launch failed."); }
                return 0;
            } catch(Exception e) { Console.Error.WriteLine(e.Message); File.WriteAllText(Path.Combine(mod,"launcher.log"),e.ToString()); return 1; }
        }
        static string Hash(string path) { using(var sha=SHA256.Create()) using(var stream=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)); }
        static bool Check(string mod,string path) {
            string message;
            try {
                if(!File.Exists(path)) throw new Exception("Set the full path to your installed Lossless.dll.");
                IntPtr dll=LoadLibrary(Path.Combine(mod,"DVLSFG.Native.dll"));
                if(dll==IntPtr.Zero) throw new Exception("Cannot load native backend: " + Marshal.GetLastWin32Error());
                var check=(Probe)Marshal.GetDelegateForFunctionPointer(GetProcAddress(dll,"DVLSFG_Probe"),typeof(Probe));
                byte[] bytes=Encoding.UTF8.GetBytes(path + "\0"); IntPtr utf8=Marshal.AllocHGlobal(bytes.Length);
                try {
                    Marshal.Copy(bytes,0,utf8,bytes.Length); var output=new StringBuilder(4096);
                    int result=check(utf8,output,4096); message=output.ToString();
                    File.WriteAllText(Path.Combine(mod,"probe.log"),DateTimeOffset.Now.ToString("o") + "\n" + path + "\n" + message,new UTF8Encoding(false));
                    Console.WriteLine(message); return result==1;
                } finally { Marshal.FreeHGlobal(utf8); }
            } catch(Exception e) { message=e.Message; }
            File.WriteAllText(Path.Combine(mod,"probe.log"),message); Console.Error.WriteLine(message); return false;
        }
    }
}

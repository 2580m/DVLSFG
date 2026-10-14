using System;
using System.IO;
using System.Security.Cryptography;
namespace DVLSFG {
    public static class BootstrapDeployment {
        public static string Hash(string path) { using(var sha=SHA256.Create()) using(var stream=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""); }
        static void ValidateRoot(string root,string mod) {
            root=Path.GetFullPath(root); mod=Path.GetFullPath(mod);
            string directory=mod.TrimEnd(Path.DirectorySeparatorChar);
            bool supported=string.Equals(directory,Path.Combine(root,"Mods","dv-scaling"),StringComparison.OrdinalIgnoreCase)
                || string.Equals(directory,Path.Combine(root,"Mods","DVLSFG"),StringComparison.OrdinalIgnoreCase);
            if(!File.Exists(Path.Combine(root,"DerailValley.exe")) || !supported)
                throw new InvalidOperationException("Bootstrap deployment requires Derail Valley/Mods/dv-scaling");
        }
        public static bool Installed(string root,string mod) {
            string file=Path.Combine(root,"vulkan-1.dll"),payload=Path.Combine(mod,"DVLSFG.Bootstrap.dll");
            return File.Exists(file) && File.Exists(payload) && Hash(file)==Hash(payload);
        }
        public static void Validate(string root,string mod,bool enabled) {
            ValidateRoot(root,mod);
            string target=Path.Combine(root,"vulkan-1.dll"),payload=Path.Combine(mod,"DVLSFG.Bootstrap.dll"),marker=target+".dvlsfg.sha256";
            if(enabled && !File.Exists(payload)) throw new FileNotFoundException("Bootstrap payload missing",payload);
            string wanted=File.Exists(payload)?Hash(payload):null,current=File.Exists(target)?Hash(target):null,owner=File.Exists(marker)?File.ReadAllText(marker).Trim():null;
            if(current!=null && current!=wanted && current!=owner) throw new IOException("Existing vulkan-1.dll belongs to another application; it was not overwritten");
            if(enabled) foreach(string name in new[]{"d3d11.dll","dxgi.dll"}) {
                string expected=Path.Combine(root,"Mods","DVVulkan","dxvk",name),deployed=Path.Combine(root,name);
                if(!File.Exists(expected) || !File.Exists(deployed) || Hash(expected)!=Hash(deployed)) throw new IOException("DVVulkan deployment is incomplete or conflicting: "+name);
            }
        }
        public static void Apply(string root,string mod,bool enabled) {
            Validate(root,mod,enabled);
            string target=Path.Combine(root,"vulkan-1.dll"),marker=target+".dvlsfg.sha256",payload=Path.Combine(mod,"DVLSFG.Bootstrap.dll");
            if(enabled && !File.Exists(payload)) throw new FileNotFoundException("Bootstrap payload missing",payload);
            string wanted=File.Exists(payload)?Hash(payload):null;
            string current=File.Exists(target)?Hash(target):null;
            string owner=File.Exists(marker)?File.ReadAllText(marker).Trim():null;
            if(current!=null && current!=wanted && current!=owner) throw new IOException("Existing vulkan-1.dll belongs to another application; it was not overwritten");
            if(enabled) {
                foreach(string name in new[]{"d3d11.dll","dxgi.dll"}) {
                    string expected=Path.Combine(root,"Mods","DVVulkan","dxvk",name),deployed=Path.Combine(root,name);
                    if(!File.Exists(expected) || !File.Exists(deployed) || Hash(expected)!=Hash(deployed)) throw new IOException("DVVulkan deployment is incomplete or conflicting: "+name);
                }
                if(current!=wanted) {
                    string temp=target+".dvlsfg.tmp";
                    try { File.Copy(payload,temp,true); if(Hash(temp)!=wanted) throw new IOException("Bootstrap copy verification failed"); if(current==null) File.Move(temp,target); else File.Replace(temp,target,null); }
                    finally { if(File.Exists(temp)) File.Delete(temp); }
                }
                // Written only after a verified installation. It identifies the
                // exact bytes we own, including a previous version during upgrade.
                File.WriteAllText(marker,wanted);
            } else {
                if(current!=null) File.Delete(target);
                if(File.Exists(marker)) File.Delete(marker);
            }
        }
    }
}

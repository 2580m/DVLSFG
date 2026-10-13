using System;
using System.IO;
using DVLSFG;
class BootstrapDeploymentTests {
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Refused(Action action) { try {action();} catch(IOException){return;} throw new Exception("Unsafe deployment accepted"); }
    static int Main(string[] args) {
        string root=Path.Combine(args[0],"deployment-"+Guid.NewGuid().ToString("N"));
        string mod=Path.Combine(root,"Mods","DVLSFG"),dxvk=Path.Combine(root,"Mods","DVVulkan","dxvk");
        Directory.CreateDirectory(mod); Directory.CreateDirectory(dxvk);
        File.WriteAllText(Path.Combine(root,"DerailValley.exe"),"fixture only");
        foreach(string name in new[]{"d3d11.dll","dxgi.dll"}) {File.WriteAllText(Path.Combine(dxvk,name),name);File.Copy(Path.Combine(dxvk,name),Path.Combine(root,name));}
        string payload=Path.Combine(mod,"DVLSFG.Bootstrap.dll"),target=Path.Combine(root,"vulkan-1.dll"),owner=target+".dvlsfg.sha256";
        File.WriteAllText(payload,"Owned payload v1");
        BootstrapDeployment.Apply(root,mod,true); Check(BootstrapDeployment.Installed(root,mod),"Initial installation failed");
        BootstrapDeployment.Apply(root,mod,true); Check(File.ReadAllText(target)=="Owned payload v1","Idempotent installation changed bytes");
        File.WriteAllText(payload,"Owned payload v2"); BootstrapDeployment.Apply(root,mod,true);
        Check(File.ReadAllText(target)=="Owned payload v2" && File.ReadAllText(owner)==BootstrapDeployment.Hash(payload),"Owned upgrade failed");
        BootstrapDeployment.Apply(root,mod,false); Check(!File.Exists(target) && !File.Exists(owner),"Owned uninstall failed");
        File.WriteAllText(target,"Foreign Vulkan loader");
        Refused(delegate{BootstrapDeployment.Apply(root,mod,true);}); Refused(delegate{BootstrapDeployment.Apply(root,mod,false);});
        Check(File.ReadAllText(target)=="Foreign Vulkan loader","Foreign DLL was changed"); File.Delete(target);
        File.WriteAllText(Path.Combine(root,"dxgi.dll"),"Conflicting DXVK");
        Refused(delegate{BootstrapDeployment.Apply(root,mod,true);}); Check(!File.Exists(target),"Bootstrap installed with conflicting dependency");
        File.Copy(Path.Combine(dxvk,"dxgi.dll"),Path.Combine(root,"dxgi.dll"),true);
        BootstrapDeployment.Apply(root,mod,true); File.WriteAllText(target,"Replaced after installation");
        Refused(delegate{BootstrapDeployment.Apply(root,mod,false);}); Check(File.ReadAllText(target)=="Replaced after installation","Uninstall deleted replaced DLL");
        Console.WriteLine("Deployment checks passed: install, repeat, upgrade, uninstall, foreign DLL protection, dependency conflict, replaced-file protection.");
        return 0;
    }
}

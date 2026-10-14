using System;
using System.IO;
using System.Globalization;
using DVLSFG;
class SettingsTests {
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    static int Main(string[] args) {
        string path=Path.Combine(args[0],"settings-test.ini");
        var settings=new Settings { Enabled=true,DllPath="C:\\测试 路径\\Lossless.dll",Multiplier=4,Performance=true,FlowScale=.5f,FsrMode=2,FsrSharpness=.3f };
        var original=CultureInfo.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
            settings.Save(path); settings.Save(path);
            var restored=Settings.Read(path);
            Assert(restored.Enabled && restored.Multiplier==4 && restored.Performance && restored.FlowScale==.5f && restored.DllPath==settings.DllPath,"UTF8/invariant roundtrip failed");
            Assert(!File.Exists(path+".tmp"),"Atomic save left temp file");
            Assert(restored.FsrMode==2 && restored.FsrSharpness==.3f,"FSR persistence failed");
            settings.FsrMode=0; settings.FsrQuality=4; settings.Save(path);
            restored=Settings.Read(path); Assert(restored.FsrMode==0 && restored.FsrQuality==4,"Off technology lost its quality selection");
            settings.FsrMode=5; settings.Save(path);
            restored=Settings.Read(path); Assert(restored.FsrMode==5 && restored.FsrQuality==5,"Ultra Performance persistence failed");
            settings.FsrMode=0; settings.FsrQuality=5; settings.Save(path);
            restored=Settings.Read(path); Assert(restored.FsrMode==0 && restored.FsrQuality==5,"Off lost Ultra Performance selection");
            File.WriteAllText(path,"Multiplier=2\nFlowScale=1\nFsrMode=3\n");
            Assert(Settings.Read(path).FsrQuality==3,"Existing enabled FSR mode was not preserved");
            File.WriteAllText(path,"Enabled=1\nMultiplier=4\nFlowScale=0.75\n");
            var legacy=Settings.Read(path);
            Assert(legacy.FsrMode==0 && legacy.FsrSharpness==.2f && legacy.Multiplier==4,"Legacy config migration changed behavior");
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f,1.1f}) {
                settings.FsrSharpness=invalid;
                bool rejected=false; try {settings.Validate();} catch(InvalidDataException) {rejected=true;}
                Assert(rejected,"Invalid FSR sharpness accepted");
            }
            settings.FsrSharpness=.2f;
            foreach(int invalid in new[]{-1,6}) {
                settings.FsrMode=invalid;
                bool rejected=false; try {settings.Validate();} catch(InvalidDataException) {rejected=true;}
                Assert(rejected,"Invalid FSR mode accepted");
            }
            settings.FsrMode=0;
            foreach(int invalid in new[]{0,6}) {
                settings.FsrQuality=invalid;
                bool rejected=false; try {settings.Validate();} catch(InvalidDataException) {rejected=true;}
                Assert(rejected,"Invalid FSR quality accepted");
            }
            settings.FsrQuality=2;
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,0f,1.1f}) {
                settings.FlowScale=invalid;
                bool rejected=false; try { settings.Validate(); } catch(InvalidDataException) { rejected=true; }
                Assert(rejected,"Invalid flow value accepted");
            }
            settings.FlowScale=1; settings.DllPath="C:\\x\nEnabled=1";
            bool badPath=false; try { settings.Validate(); } catch(InvalidDataException) { badPath=true; }
            Assert(badPath,"Config line injection accepted");
            Console.WriteLine("Settings checks passed: UTF8, locale, atomic replacement, invalid values and line injection."); return 0;
        } finally { System.Threading.Thread.CurrentThread.CurrentCulture=original; }
    }
}

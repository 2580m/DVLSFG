using System;
using System.IO;
using System.Globalization;
using DVLSFG;
class SettingsTests {
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    static int Main(string[] args) {
        string path=Path.Combine(args[0],"settings-test.ini");
        var settings=new Settings { Enabled=true,DllPath="C:\\测试 路径\\Lossless.dll",Multiplier=4,Performance=true,FlowScale=.5f };
        var original=CultureInfo.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
            settings.Save(path); settings.Save(path);
            var restored=Settings.Read(path);
            Assert(restored.Enabled && restored.Multiplier==4 && restored.Performance && restored.FlowScale==.5f && restored.DllPath==settings.DllPath,"UTF8/invariant roundtrip failed");
            Assert(!File.Exists(path+".tmp"),"Atomic save left temp file");
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

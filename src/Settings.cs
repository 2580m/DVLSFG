using System;
using System.IO;
using System.Globalization;
using System.Text;
namespace DVLSFG {
    public sealed class Settings {
        public bool Enabled;
        public string DllPath = "";
        public int Multiplier = 2;
        public bool Performance;
        public float FlowScale = 1f;
        public int FsrMode;
        public int FsrTechnology;
        public int FsrQuality=2;
        public float FsrSharpness=.2f;
        public static Settings Read(string path) {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            bool technologyPresent=false;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8)) {
                int eq = line.IndexOf('='); if (eq < 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                switch(key) {
                    case "Enabled": s.Enabled = value == "1"; break;
                    case "DllPath": s.DllPath = value; break;
                    case "Multiplier": s.Multiplier = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "Performance": s.Performance = value == "1"; break;
                    case "FlowScale": s.FlowScale = float.Parse(value, CultureInfo.InvariantCulture); break;
                    case "FsrMode": s.FsrMode=int.Parse(value,CultureInfo.InvariantCulture); break;
                    case "FsrTechnology": s.FsrTechnology=int.Parse(value,CultureInfo.InvariantCulture); technologyPresent=true; break;
                    case "FsrQuality": s.FsrQuality=int.Parse(value,CultureInfo.InvariantCulture); break;
                    case "FsrSharpness": s.FsrSharpness=float.Parse(value,CultureInfo.InvariantCulture); break;
                }
            }
            if(s.FsrMode>0) s.FsrQuality=s.FsrMode;
            if(!technologyPresent) s.FsrTechnology=s.FsrMode>0 ? 1 : 0;
            s.Validate(); return s;
        }
        public void Validate() {
            DllPath = (DllPath ?? "").Trim().Trim('"');
            if (DllPath.IndexOfAny(new char[] {'\r','\n','\0'}) >= 0) throw new InvalidDataException("Invalid DLL path");
            if (Multiplier < 2 || Multiplier > 4) throw new InvalidDataException("Multiplier must be 2..4");
            if (float.IsNaN(FlowScale) || float.IsInfinity(FlowScale) || FlowScale < .25f || FlowScale > 1f) throw new InvalidDataException("Flow scale must be 0.25..1");
            if(FsrMode<0 || FsrMode>5) throw new InvalidDataException("FSR mode must be 0..5");
            if(FsrTechnology<0 || FsrTechnology>2) throw new InvalidDataException("FSR technology must be 0..2");
            if(FsrQuality<1 || FsrQuality>5) throw new InvalidDataException("FSR quality must be 1..5");
            if(float.IsNaN(FsrSharpness) || float.IsInfinity(FsrSharpness) || FsrSharpness<0 || FsrSharpness>1) throw new InvalidDataException("FSR sharpness must be 0..1");
        }
        public void Save(string path) {
            Validate();
            string text = "Enabled=" + (Enabled ? "1" : "0") + "\nDllPath=" + DllPath
                + "\nMultiplier=" + Multiplier + "\nPerformance=" + (Performance ? "1" : "0")
                + "\nFlowScale=" + FlowScale.ToString("R",CultureInfo.InvariantCulture)
                + "\nFsrMode="+FsrMode+"\nFsrTechnology="+(FsrMode==0 ? 0 : FsrTechnology==2 ? 2 : 1)+"\nFsrQuality="+(FsrMode>0 ? FsrMode : FsrQuality)+"\nFsrSharpness="+FsrSharpness.ToString("R",CultureInfo.InvariantCulture)+"\n";
            string temp = path + ".tmp";
            File.WriteAllText(temp,text,new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
    }
}

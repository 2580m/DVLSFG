using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityModManagerNet;
using I2.Loc;
using HarmonyLib;
namespace DVLSFG {
    public static class Main {
        static string directory, config, error;
        internal static Settings settings;
        internal static Settings runtimeSettings;
        internal static UnityModManager.ModEntry modEntry;
        static bool loaded;
        static Status status;
        static Enable enable;
        static Capture capture;
        static Metrics metrics;
        static InputFrameLimit inputFrameLimit;
        static SetMultiplier setMultiplier;
        static GetMultiplier getMultiplier;
        static SetGenerationSettings setGenerationSettings;
        static GetGenerationSettings getGenerationSettings;
        [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Status(StringBuilder text,uint size,out ulong real,out ulong generated);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Enable(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetMultiplier(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetMultiplier(out int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetGenerationSettings(int multiplier,int performance,float flowScale);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetGenerationSettings(out int multiplier,out int performance,out float flowScale);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Capture();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InputFrameLimit(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Metrics(out float input,out float submitted,out float mean,out float peak);
        static string T(string zh,string en) { try { return (LocalizationManager.CurrentLanguageCode ?? "").StartsWith("zh",StringComparison.OrdinalIgnoreCase) ? zh : en; } catch { return en; } }
        public static bool Load(UnityModManager.ModEntry entry) {
            modEntry=entry;
            directory = Path.GetFullPath(entry.Path); config=Path.Combine(directory,"config.ini");
            try { settings=Settings.Read(config); }
            catch(Exception e) { settings=new Settings(); error=e.Message; entry.Logger.Error(e.Message); }
            runtimeSettings=new Settings {Enabled=settings.Enabled,DllPath=settings.DllPath,Multiplier=settings.Multiplier,Performance=settings.Performance,FlowScale=settings.FlowScale};
            AutoStartup.Initialize(entry);
            entry.OnToggle=Toggle; entry.OnGUI=Draw; entry.OnSaveGUI=Save;
            entry.OnUpdate=UpdateFrameLimit;
            Bind();
            new Harmony("DVLSFG.GraphicsSettings").PatchAll(typeof(Main).Assembly);
            entry.Logger.Log(T("DVLSFG 已加载；插帧状态请查看设置页。","DVLSFG loaded; see settings for actual frame generation status."));
            return true;
        }
        static void Bind() {
            if(loaded) return;
            IntPtr module=GetModuleHandle("DVLSFG.Native.dll"); if(module==IntPtr.Zero) return;
            IntPtr a=GetProcAddress(module,"DVLSFG_GetStatus"),b=GetProcAddress(module,"DVLSFG_SetEnabled");
            if(a==IntPtr.Zero || b==IntPtr.Zero) return;
            status=(Status)Marshal.GetDelegateForFunctionPointer(a,typeof(Status));
            enable=(Enable)Marshal.GetDelegateForFunctionPointer(b,typeof(Enable)); loaded=true;
            IntPtr c=GetProcAddress(module,"DVLSFG_RequestCapture");
            if(c!=IntPtr.Zero) capture=(Capture)Marshal.GetDelegateForFunctionPointer(c,typeof(Capture));
            IntPtr m=GetProcAddress(module,"DVLSFG_GetMetrics");
            if(m!=IntPtr.Zero) metrics=(Metrics)Marshal.GetDelegateForFunctionPointer(m,typeof(Metrics));
            IntPtr f=GetProcAddress(module,"DVLSFG_SetInputFrameLimit");
            if(f!=IntPtr.Zero) inputFrameLimit=(InputFrameLimit)Marshal.GetDelegateForFunctionPointer(f,typeof(InputFrameLimit));
            IntPtr sm=GetProcAddress(module,"DVLSFG_SetMultiplier"),gm=GetProcAddress(module,"DVLSFG_GetMultiplier");
            if(sm!=IntPtr.Zero && gm!=IntPtr.Zero) {
                setMultiplier=(SetMultiplier)Marshal.GetDelegateForFunctionPointer(sm,typeof(SetMultiplier));
                getMultiplier=(GetMultiplier)Marshal.GetDelegateForFunctionPointer(gm,typeof(GetMultiplier));
            }
            IntPtr sg=GetProcAddress(module,"DVLSFG_SetGenerationSettings"),gg=GetProcAddress(module,"DVLSFG_GetGenerationSettings");
            if(sg!=IntPtr.Zero && gg!=IntPtr.Zero) {
                setGenerationSettings=(SetGenerationSettings)Marshal.GetDelegateForFunctionPointer(sg,typeof(SetGenerationSettings));
                getGenerationSettings=(GetGenerationSettings)Marshal.GetDelegateForFunctionPointer(gg,typeof(GetGenerationSettings));
            }
        }
        static int RefreshGenerationSettings() {
            int multiplier,performance; float flow;
            int state=getGenerationSettings(out multiplier,out performance,out flow);
            runtimeSettings.Multiplier=multiplier; runtimeSettings.Performance=performance!=0; runtimeSettings.FlowScale=flow;
            return state;
        }
        static void UpdateFrameLimit(UnityModManager.ModEntry entry,float dt) {
            Bind();
            if(inputFrameLimit!=null) inputFrameLimit(Application.targetFrameRate);
            if(getGenerationSettings!=null) RefreshGenerationSettings();
            else if(getMultiplier!=null) { int actual; getMultiplier(out actual); runtimeSettings.Multiplier=actual; }
            FsrRuntime.Tick();
        }
        static bool Toggle(UnityModManager.ModEntry entry,bool value) {
            AutoStartup.Request(entry,value);
            if(!value) foreach(var panel in Resources.FindObjectsOfTypeAll<GraphicsSettingsPanel>()) panel.Discard();
            Bind(); if(loaded) enable(value && settings.Enabled ? 1 : 0); return true;
        }
        static void Save(UnityModManager.ModEntry entry) { SaveSettings(entry); }
        internal static bool SaveSettings(UnityModManager.ModEntry entry) {
            try { settings.Save(config); error=null; ApplyEnabled(); return true; }
            catch(Exception e) { error=e.Message; entry.Logger.Error(e.Message); return false; }
        }
        internal static bool NativeLoaded { get { Bind(); return loaded; } }
        internal static string GraphicsStatus {
            get {
                if(!NativeLoaded) return AutoStartup.Error!=null ? T("启动接入失败，请查看 UMM","Startup setup failed; see UMM") : !settings.Enabled ? T("已关闭","Off") : T("需重启以启用","Restart to enable");
                if(settings.DllPath!=runtimeSettings.DllPath)
                    return T("需重启以应用","Restart to apply");
                if(getGenerationSettings!=null) {
                    int change=RefreshGenerationSettings();
                    if(change==1) return T("正在切换帧生成设置","Switching generation settings");
                    if(change<0) return T("切换失败，当前 ","Change failed; active ")+runtimeSettings.Multiplier+"x / "+
                        (runtimeSettings.Performance ? T("性能","Performance") : T("标准","Standard"))+" / "+Mathf.RoundToInt(runtimeSettings.FlowScale*100)+"%";
                } else if(getMultiplier!=null) {
                    int actual; int change=getMultiplier(out actual); runtimeSettings.Multiplier=actual;
                    if(change==1) return T("正在切换倍率","Switching multiplier");
                    if(change<0) return T("倍率切换失败，当前 ","Multiplier change failed; active ")+actual+"x";
                }
                if(settings.Performance!=runtimeSettings.Performance || settings.FlowScale!=runtimeSettings.FlowScale)
                    return getGenerationSettings==null ? T("需重启以应用","Restart to apply") : T("帧生成设置尚未应用","Generation settings not applied");
                if(settings.Multiplier!=runtimeSettings.Multiplier) return T("倍率尚未应用","Multiplier not applied");
                var message=new StringBuilder(4096); ulong real,generated; int active=status(message,4096,out real,out generated);
                return active==0 ? T("已关闭 / 不可用","Off / unavailable") : generated>0 ? T("正在插帧","Generating frames") : T("尚未生成帧","No frames generated yet");
            }
        }
        internal static void ApplyEnabled() {
            Bind(); if(!loaded) return;
            enable(settings.Enabled && modEntry.Active ? 1 : 0);
            if(setGenerationSettings!=null && getGenerationSettings!=null) {
                int change=RefreshGenerationSettings();
                if(settings.Multiplier!=runtimeSettings.Multiplier || settings.Performance!=runtimeSettings.Performance || settings.FlowScale!=runtimeSettings.FlowScale || change!=0)
                    setGenerationSettings(settings.Multiplier,settings.Performance ? 1 : 0,settings.FlowScale);
            } else if(setMultiplier!=null && getMultiplier!=null) {
                int actual; int change=getMultiplier(out actual);
                if(settings.Multiplier!=actual || change==1) setMultiplier(settings.Multiplier);
            }
        }
        static void Draw(UnityModManager.ModEntry entry) {
            Bind(); GUILayout.Label("DV LSFG 0.4.12 — " + T("实验版本","Experimental"));
            GUILayout.Label("FSR 1.0: "+FsrRuntime.Status);
            GUILayout.Label(T("前置：已启用的 DVVulkan；仅支持非 VR、SDR。","Requires active DVVulkan; non-VR SDR only."));
            if(loaded) {
                var message=new StringBuilder(4096); ulong real,generated; int active=status(message,4096,out real,out generated);
                GUILayout.Label(T("原生层已加载：","Native layer loaded: ") + message);
                GUILayout.Label(T("已处理真实帧 / 生成帧：","Processed real / generated frames: ") + real + " / " + generated);
                if(metrics!=null) {
                    float input,submitted,mean,peak; metrics(out input,out submitted,out mean,out peak);
                    GUILayout.Label(T("输入 / 提交帧率：","Input / submitted frame rate: ")+input.ToString("F1")+" / "+submitted.ToString("F1")+" FPS");
                    GUILayout.Label(T("呈现处理时间（含同步等待）：","Presentation time (includes synchronization waits): ")+mean.ToString("F1")+" ms; "+T("峰值 ","peak ")+peak.ToString("F1")+" ms");
                }
                GUILayout.Label(active==1 && generated>0 ? T("已记录插帧；画质及延迟需实测。","Generated frames recorded; verify visual quality and latency.") : generated>0 ? T("插帧已暂停。","Frame generation paused.") : T("本次尚未记录生成帧。","No generated frames recorded in this session."));
            } else GUILayout.Label(T("原生层未加载。开启插帧并保存后重启游戏；下次可照常启动。","Native layer absent. Enable frame generation, save, and restart normally."));
            GUILayout.Label(AutoStartup.Installed ? T("普通启动接入已安装。","Automatic ordinary-launch integration installed.") : T("普通启动接入等待部署或重启。","Automatic launch integration awaiting deployment or restart."));
            if(AutoStartup.Error!=null) GUILayout.Label(AutoStartup.Error);
            GUILayout.Label("Lossless.dll " + T("完整路径","full path")); settings.DllPath=GUILayout.TextField(settings.DllPath);
            GUILayout.BeginHorizontal(); GUILayout.Label(T("倍率：","Multiplier: ") + settings.Multiplier + "x");
            if(GUILayout.Button("2x")) settings.Multiplier=2;
            if(GUILayout.Button("3x")) settings.Multiplier=3;
            if(GUILayout.Button("4x")) settings.Multiplier=4; GUILayout.EndHorizontal();
            settings.Performance=GUILayout.Toggle(settings.Performance,T("性能模型（需要对应着色器）","Performance model (requires matching shaders)"));
            GUILayout.Label(T("光流分辨率比例：","Flow scale: ") + settings.FlowScale.ToString("0.00"));
            settings.FlowScale=GUILayout.HorizontalSlider(settings.FlowScale,.25f,1f);
            GUILayout.Label(T("倍率、模型和光流质量保存后生效。DLL 路径修改仍需重启。","Multiplier, model and flow quality apply on save. DLL path changes still require restart."));
            if(GUILayout.Button(T("保存设置","Save settings"))) SaveSettings(entry);
            if(loaded && capture!=null && GUILayout.Button(T("保存输入 / 生成帧对照图","Save input / generated frame comparison"))) capture();
            if(GUILayout.Button(T("检查 DLL 兼容性","Check DLL compatibility")) && SaveSettings(entry)) {
                try {
                    var start=new ProcessStartInfo(Path.Combine(directory,"DVLSFG.Launcher.exe"),"--probe");
                    start.UseShellExecute=false; start.CreateNoWindow=true; start.WindowStyle=ProcessWindowStyle.Hidden;
                    using(var process=Process.Start(start)) { if(process==null) throw new Exception("Probe did not start"); }
                } catch(Exception e) { error=e.Message; }
            }
            string probe=Path.Combine(directory,"probe.log");
            if(File.Exists(probe)) { try { GUILayout.Label(File.ReadAllText(probe)); } catch(IOException) {} }
            if(!string.IsNullOrEmpty(error)) GUILayout.Label(error);
            GUILayout.Label(T("启动：Steam 或原有游戏快捷方式即可。","Launch normally through Steam or your existing game shortcut."));
            GUILayout.Label(T("卸载：先在 UMM 停用 DVLSFG，退出游戏等待自动移除启动接入，再删除模组。","Uninstall: disable in UMM, exit for automatic bootstrap removal, then remove the mod."));
        }
    }
}

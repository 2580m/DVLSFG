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
        public static bool Load(UnityModManager.ModEntry entry) {
            modEntry=entry;
            directory = Path.GetFullPath(entry.Path); config=Path.Combine(directory,"config.ini");
            try { L10n.Initialize(directory); }
            catch(Exception e) { entry.Logger.Error("Localization: "+e); return false; }
            try { settings=Settings.Read(config); }
            catch(Exception e) { settings=new Settings(); error=e.Message; entry.Logger.Error(e.Message); }
            runtimeSettings=new Settings {Enabled=settings.Enabled,FrameGenerationTechnology=settings.FrameGenerationTechnology,DllPath=settings.DllPath,Multiplier=settings.Multiplier,Performance=settings.Performance,FlowScale=settings.FlowScale};
            AutoStartup.Initialize(entry);
            entry.OnToggle=Toggle; entry.OnGUI=Draw; entry.OnSaveGUI=Save;
            entry.OnUpdate=UpdateFrameLimit;
            Bind();
            new Harmony("DVLSFG.GraphicsSettings").PatchAll(typeof(Main).Assembly);
            entry.Logger.Log(L10n.Get("dv_scaling/dvlsfg_loaded_see_settings_for_actual_frame_generation_statu"));
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
            SceneUiRuntime.Tick();
            FsrRuntime.Tick();
        }
        static bool Toggle(UnityModManager.ModEntry entry,bool value) {
            AutoStartup.Request(entry,value);
            if(!value) foreach(var panel in Resources.FindObjectsOfTypeAll<GraphicsSettingsPanel>()) panel.Discard();
            Bind(); if(loaded) enable(value && settings.Enabled && settings.FrameGenerationTechnology==runtimeSettings.FrameGenerationTechnology ? 1 : 0); return true;
        }
        static void Save(UnityModManager.ModEntry entry) { SaveSettings(entry); }
        internal static bool SaveSettings(UnityModManager.ModEntry entry) {
            try { settings.Save(config); error=null; ApplyEnabled(); return true; }
            catch(Exception e) { error=e.Message; entry.Logger.Error(e.Message); return false; }
        }
        internal static bool NativeLoaded { get { Bind(); return loaded; } }
        internal static string GraphicsStatus {
            get {
                if(!NativeLoaded) return AutoStartup.Error!=null ? L10n.Get("dv_scaling/startup_setup_failed_see_umm") : !settings.Enabled ? L10n.Get("dv_scaling/off") : L10n.Get("dv_scaling/restart_to_enable");
                if(settings.FrameGenerationTechnology!=runtimeSettings.FrameGenerationTechnology) return L10n.Get("dv_scaling/restart_to_apply");
                if(runtimeSettings.FrameGenerationTechnology==2) {
                    var fsrMessage=new StringBuilder(4096); ulong fsrReal,fsrGenerated;
                    int fsrActive=status(fsrMessage,4096,out fsrReal,out fsrGenerated);
                    return fsrActive==0 ? L10n.Get("dv_scaling/off_unavailable") : fsrGenerated>0 ? L10n.Get("dv_scaling/generating_frames") : L10n.Get("dv_scaling/no_frames_generated_yet");
                }
                if(settings.DllPath!=runtimeSettings.DllPath)
                    return L10n.Get("dv_scaling/restart_to_apply");
                if(getGenerationSettings!=null) {
                    int change=RefreshGenerationSettings();
                    if(change==1) return L10n.Get("dv_scaling/switching_generation_settings");
                    if(change<0) return L10n.Get("dv_scaling/change_failed_active")+runtimeSettings.Multiplier+"x / "+
                        (runtimeSettings.Performance ? L10n.Get("dv_scaling/performance") : L10n.Get("dv_scaling/standard"))+" / "+Mathf.RoundToInt(runtimeSettings.FlowScale*100)+"%";
                } else if(getMultiplier!=null) {
                    int actual; int change=getMultiplier(out actual); runtimeSettings.Multiplier=actual;
                    if(change==1) return L10n.Get("dv_scaling/switching_multiplier");
                    if(change<0) return L10n.Get("dv_scaling/multiplier_change_failed_active")+actual+"x";
                }
                if(settings.Performance!=runtimeSettings.Performance || settings.FlowScale!=runtimeSettings.FlowScale)
                    return getGenerationSettings==null ? L10n.Get("dv_scaling/restart_to_apply") : L10n.Get("dv_scaling/generation_settings_not_applied");
                if(settings.Multiplier!=runtimeSettings.Multiplier) return L10n.Get("dv_scaling/multiplier_not_applied");
                var message=new StringBuilder(4096); ulong real,generated; int active=status(message,4096,out real,out generated);
                return active==0 ? L10n.Get("dv_scaling/off_unavailable") : generated>0 ? L10n.Get("dv_scaling/generating_frames") : L10n.Get("dv_scaling/no_frames_generated_yet");
            }
        }
        internal static void ApplyEnabled() {
            Bind(); if(!loaded) return;
            enable(settings.Enabled && modEntry.Active && settings.FrameGenerationTechnology==runtimeSettings.FrameGenerationTechnology ? 1 : 0);
            if(runtimeSettings.FrameGenerationTechnology==2 || settings.FrameGenerationTechnology!=runtimeSettings.FrameGenerationTechnology) return;
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
            Bind(); GUILayout.Label("DV Scaling 0.6.3 — " + L10n.Get("dv_scaling/experimental"));
            GUILayout.Label((settings.FsrTechnology==2 ? "FSR 3.1: " : "FSR 1.0: ")+FsrRuntime.Status);
            GUILayout.Label(L10n.Get("dv_scaling/requires_active_dvvulkan_non_vr_sdr_only"));
            if(loaded) {
                var message=new StringBuilder(4096); ulong real,generated; int active=status(message,4096,out real,out generated);
                GUILayout.Label(L10n.Get("dv_scaling/native_layer_loaded") + message);
                GUILayout.Label(L10n.Get("dv_scaling/processed_real_generated_frames") + real + " / " + generated);
                if(metrics!=null) {
                    float input,submitted,mean,peak; metrics(out input,out submitted,out mean,out peak);
                    GUILayout.Label(L10n.Get("dv_scaling/input_submitted_frame_rate")+input.ToString("F1")+" / "+submitted.ToString("F1")+" FPS");
                    GUILayout.Label(L10n.Get("dv_scaling/presentation_time_includes_synchronization_waits")+mean.ToString("F1")+" ms; "+L10n.Get("dv_scaling/peak")+peak.ToString("F1")+" ms");
                }
                GUILayout.Label(active==1 && generated>0 ? L10n.Get("dv_scaling/generated_frames_recorded_verify_visual_quality_and_latency") : generated>0 ? L10n.Get("dv_scaling/frame_generation_paused") : L10n.Get("dv_scaling/no_generated_frames_recorded_in_this_session"));
            } else GUILayout.Label(L10n.Get("dv_scaling/native_layer_absent_enable_frame_generation_save_and_restart"));
            GUILayout.Label(AutoStartup.Installed ? L10n.Get("dv_scaling/automatic_ordinary_launch_integration_installed") : L10n.Get("dv_scaling/automatic_launch_integration_awaiting_deployment_or_restart"));
            if(AutoStartup.Error!=null) GUILayout.Label(AutoStartup.Error);
            GUILayout.Label("Lossless.dll " + L10n.Get("dv_scaling/full_path")); settings.DllPath=GUILayout.TextField(settings.DllPath);
            if(string.IsNullOrEmpty(settings.DllPath) && Event.current.type==EventType.Repaint) {
                var placeholder=new GUIStyle(GUI.skin.label);
                placeholder.padding=GUI.skin.textField.padding;
                placeholder.alignment=GUI.skin.textField.alignment;
                placeholder.fontSize=GUI.skin.textField.fontSize;
                placeholder.richText=false; placeholder.wordWrap=false; placeholder.clipping=TextClipping.Clip;
                placeholder.normal.textColor=new Color(.75f,.75f,.75f,.65f);
                GUI.Label(GUILayoutUtility.GetLastRect(),"C:/<YourLosslessScaling>/Lossless.dll",placeholder);
            }
            bool previousGuiEnabled=GUI.enabled;
            GUI.enabled=previousGuiEnabled && settings.FrameGenerationTechnology==1;
            GUILayout.BeginHorizontal(); GUILayout.Label(L10n.Get("dv_scaling/multiplier") + (settings.FrameGenerationTechnology==2 ? 2 : settings.Multiplier) + "x");
            if(GUILayout.Button("2x")) settings.Multiplier=2;
            if(GUILayout.Button("3x")) settings.Multiplier=3;
            if(GUILayout.Button("4x")) settings.Multiplier=4; GUILayout.EndHorizontal();
            settings.Performance=GUILayout.Toggle(settings.Performance,L10n.Get("dv_scaling/performance_model_requires_matching_shaders"));
            GUILayout.Label(L10n.Get("dv_scaling/flow_scale") + settings.FlowScale.ToString("0.00"));
            settings.FlowScale=GUILayout.HorizontalSlider(settings.FlowScale,.25f,1f);
            GUILayout.Label(L10n.Get("dv_scaling/multiplier_model_and_flow_quality_apply_on_save_dll_path_cha"));
            GUI.enabled=previousGuiEnabled;
            if(GUILayout.Button(L10n.Get("dv_scaling/save_settings"))) SaveSettings(entry);
            if(loaded && capture!=null && GUILayout.Button(L10n.Get("dv_scaling/save_input_generated_frame_comparison"))) capture();
            if(GUILayout.Button(L10n.Get("dv_scaling/check_dll_compatibility")) && SaveSettings(entry)) {
                try {
                    var start=new ProcessStartInfo(Path.Combine(directory,"DVLSFG.Launcher.exe"),"--probe");
                    start.UseShellExecute=false; start.CreateNoWindow=true; start.WindowStyle=ProcessWindowStyle.Hidden;
                    using(var process=Process.Start(start)) { if(process==null) throw new Exception("Probe did not start"); }
                } catch(Exception e) { error=e.Message; }
            }
            string probe=Path.Combine(directory,"probe.log");
            if(File.Exists(probe)) { try { GUILayout.Label(File.ReadAllText(probe)); } catch(IOException) {} }
            if(!string.IsNullOrEmpty(error)) GUILayout.Label(error);
            GUILayout.Label(L10n.Get("dv_scaling/launch_normally_through_steam_or_your_existing_game_shortcut"));
            GUILayout.Label(L10n.Get("dv_scaling/uninstall_disable_in_umm_exit_for_automatic_bootstrap_remova"));
        }
    }
}

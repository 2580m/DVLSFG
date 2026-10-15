using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    // Test-only input substitution scoped to the game's Zoom action. CameraZoom
    // still executes the native press/release, SmoothDamp and camera logic.
    [DefaultExecutionOrder(-10000)]
    public sealed class ZoomWorldProbe : MonoBehaviour {
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long value);
        static ZoomWorldProbe live;
        UnityModManager.ModEntry entry;
        Settings settings;
        Harmony harmony;
        object player;
        int action;
        int originalFrameLimit,captureRequests;
        bool fovChanging;
        bool pressed,down,up,suppress;
        string mode="steady",command="";
        float began,poll;
        Camera camera;
        int srChanges,fgChanges,srResets,fgResets;
        readonly List<string> samples=new List<string>();
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static readonly FieldInfo SrFov=AccessTools.Field(typeof(Fsr3SceneCamera),"lastFov");
        static readonly FieldInfo FgFov=AccessTools.Field(typeof(TemporalSceneCamera),"lastFov");
        static readonly PropertyInfo SrOwns=AccessTools.Property(typeof(Fsr3SceneCamera),"OwnsSceneTarget");
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_RequestCapture();
        public static IEnumerator Run(UnityModManager.ModEntry entry) {
            var go=new GameObject("Temporary zoom performance probe");
            var probe=go.AddComponent<ZoomWorldProbe>();
            go.AddComponent<ZoomCutDriver>();
            probe.entry=entry;
            yield return probe.Measure();
            Destroy(go);
        }
        IEnumerator Measure() {
            live=this;
            settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            originalFrameLimit=Application.targetFrameRate;
            bool enabled=settings.Enabled; int sr=settings.FsrMode,technology=settings.FsrTechnology;
            var input=AccessTools.TypeByName("DV.Interaction.Inputs.InputManager");
            player=AccessTools.Property(input,"NewPlayer").GetValue(null,null);
            action=(int)AccessTools.Property(input.GetNestedType("Actions",BindingFlags.Public|BindingFlags.NonPublic),"Zoom").GetValue(null,null);
            harmony=new Harmony("DVScaling.Tests.ZoomProbe");
            foreach(string name in new[]{"GetButton","GetButtonDown","GetButtonUp"})
                harmony.Patch(AccessTools.Method(player.GetType(),name,new[]{typeof(int)}),prefix:new HarmonyMethod(typeof(ZoomWorldProbe),"InputPrefix"));
            harmony.Patch(AccessTools.Method(typeof(Fsr3SceneCamera),"OnPreCull"),prefix:new HarmonyMethod(typeof(ZoomWorldProbe),"SrPrefix"),postfix:new HarmonyMethod(typeof(ZoomWorldProbe),"SrPostfix"));
            harmony.Patch(AccessTools.Method(typeof(TemporalSceneCamera),"Capture"),prefix:new HarmonyMethod(typeof(ZoomWorldProbe),"FgPrefix"),postfix:new HarmonyMethod(typeof(ZoomWorldProbe),"FgPostfix"));
            var zoomType=AccessTools.TypeByName("CameraZoom");
            foreach(var obj in Resources.FindObjectsOfTypeAll(zoomType)) {
                var component=obj as Behaviour;
                if(component && component.isActiveAndEnabled) {camera=AccessTools.Field(zoomType,"cam").GetValue(obj) as Camera; if(camera) break;}
            }
            if(!camera) throw new Exception("Native active CameraZoom not found");
            entry.Logger.Log("Zoom probe camera="+camera.name+" action="+action+" FOV="+camera.fieldOfView+" target="+Application.targetFrameRate);
            began=Time.realtimeSinceStartup;
            File.WriteAllText(Path.Combine(entry.Path,"zoom-ready.txt"),DateTime.UtcNow.ToString("O"));
            try {
                var end=new WaitForEndOfFrame();
                while(File.Exists(Path.Combine(entry.Path,"zoom-live.txt"))) {
                    yield return end;
                    if(command.Length==0) continue;
                    long qpc,frequency; QueryPerformanceCounter(out qpc); QueryPerformanceFrequency(out frequency);
                    samples.Add(string.Join(",",new[]{(qpc/(double)frequency).ToString("F6",Inv),(Time.realtimeSinceStartup-began).ToString("F5",Inv),(Time.unscaledDeltaTime*1000).ToString("F5",Inv),camera.fieldOfView.ToString("F5",Inv),pressed ? "1" : "0",srChanges.ToString(),fgChanges.ToString(),srResets.ToString(),fgResets.ToString()}));
                }
                mode="steady";
                yield return new WaitForSecondsRealtime(1);
            } finally {
                SaveSamples();
                pressed=down=false; up=true;
                settings.Enabled=enabled; settings.FsrMode=sr; settings.FsrTechnology=technology;
                Application.targetFrameRate=originalFrameLimit;
                AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                harmony.UnpatchAll(harmony.Id); live=null;
            }
        }
        void Update() {
            if(entry==null || live!=this) return;
            poll+=Time.unscaledDeltaTime;
            if(poll>=.2f) {
                poll=0;
                string path=Path.Combine(entry.Path,"zoom-command.txt");
                string next=File.Exists(path) ? File.ReadAllText(path).Trim() : "";
                if(next.Length>0 && next!=command) {
                    SaveSamples(); samples.Clear();
                    var fields=next.Split(';');
                    mode=fields[1]; settings.Enabled=bool.Parse(fields[2]);
                    int sr=int.Parse(fields[3]); settings.FsrTechnology=sr; settings.FsrMode=sr==0 ? 0 : 3;
                    suppress=bool.Parse(fields[4]);
                    int limit=fields.Length>5 ? int.Parse(fields[5]) : 0;
                    Application.targetFrameRate=limit>0 ? limit : originalFrameLimit;
                    captureRequests=fields.Length>6 ? int.Parse(fields[6]) : 0;
                    AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                    command=next; began=Time.realtimeSinceStartup; srChanges=fgChanges=srResets=fgResets=0;
                    File.WriteAllText(Path.Combine(entry.Path,"zoom-ack.txt"),next);
                }
            }
            bool nextPressed=mode=="hold" || (mode=="cycle" && (Time.realtimeSinceStartup-began)%2.4f<1.2f);
            down=nextPressed && !pressed; up=!nextPressed && pressed; pressed=nextPressed;
        }
        void SaveSamples() {
            if(command.Length==0) return;
            string path=Path.Combine(entry.Path,"zoom-"+command.Split(';')[0]+".csv");
            File.WriteAllText(path,"qpc,elapsed,frame_ms,fov,pressed,sr_fov_changes,fg_fov_changes,sr_reset_frames,fg_reset_frames\n"+string.Join("\n",samples.ToArray())+"\n");
        }
        static bool InputPrefix(object __instance,int __0,MethodBase __originalMethod,ref bool __result) {
            if(live==null || !ReferenceEquals(__instance,live.player) || __0!=live.action) return true;
            __result=__originalMethod.Name=="GetButtonDown" ? live.down : __originalMethod.Name=="GetButtonUp" ? live.up : live.pressed;
            return false;
        }
        static void SrPrefix(Fsr3SceneCamera __instance) {
            if(live==null || !(bool)SrOwns.GetValue(__instance,null)) return;
            var camera=__instance.GetComponent<Camera>();
            if(Mathf.Abs(camera.fieldOfView-(float)SrFov.GetValue(__instance))>.1f) live.srChanges++;
            if(live.suppress) SrFov.SetValue(__instance,camera.fieldOfView);
        }
        static void SrPostfix(Fsr3SceneCamera __instance) {
            if(live!=null && (bool)SrOwns.GetValue(__instance,null) && (bool)AccessTools.Field(typeof(Fsr3SceneCamera),"reset").GetValue(__instance)) live.srResets++;
        }
        static void FgPrefix(TemporalSceneCamera __instance) {
            if(live==null) return;
            var camera=__instance.GetComponent<Camera>();
            live.fovChanging=Mathf.Abs(camera.fieldOfView-(float)FgFov.GetValue(__instance))>.1f;
            if(live.fovChanging) live.fgChanges++;
            if(live.suppress) FgFov.SetValue(__instance,camera.fieldOfView);
        }
        static void FgPostfix(TemporalSceneCamera __instance) {
            if(live!=null && (bool)AccessTools.Property(typeof(TemporalSceneCamera),"HistoryReset").GetValue(__instance,null)) live.fgResets++;
            if(live!=null && live.fovChanging && live.captureRequests>0 && Time.realtimeSinceStartup-live.began>.25f) {live.captureRequests--; DVLSFG_RequestCapture();}
        }
        internal static void DriveCut() {
            if(live!=null && live.camera && live.mode=="cut") live.camera.fieldOfView=(Time.realtimeSinceStartup-live.began)%2.4f<1.2f ? 20 : 50;
        }
    }
    [DefaultExecutionOrder(10000)]
    public sealed class ZoomCutDriver : MonoBehaviour {
        void LateUpdate() {ZoomWorldProbe.DriveCut();}
    }
}

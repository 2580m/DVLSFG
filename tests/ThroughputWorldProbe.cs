using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    // Opt-in fixed-view attribution. Frozen UI is a diagnostic ablation only:
    // scene, temporal inputs, interpolation and native compositing keep running.
    public static class ThroughputWorldProbe {
        static bool freeze,legacy;
        static double uiMs,captureMs;
        static int uiCalls,canvasCount,layerCount,redrawn;
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long value);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_SetLegacyPresentWait(int enabled);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_SetPacingHeadroom(float value);
        static double Qpc() {long stamp,frequency; QueryPerformanceCounter(out stamp); QueryPerformanceFrequency(out frequency); return stamp/(double)frequency;}
        static long Stamp() {return Stopwatch.GetTimestamp();}
        static double Elapsed(long start) {return (Stamp()-start)*1000.0/Stopwatch.Frequency;}
        static bool UiPrefix(object __instance,ref bool __result,out long __state) {
            __state=Stamp();
            if(legacy) {
                var cache=AccessTools.Field(typeof(SceneUiCapture),"uiCache").GetValue(__instance);
                AccessTools.Method(cache.GetType(),"Invalidate").Invoke(cache,null);
            }
            var camera=AccessTools.Field(typeof(SceneUiCapture),"uiCamera").GetValue(__instance) as Camera;
            if(camera) {camera.useOcclusionCulling=legacy; camera.renderingPath=legacy ? RenderingPath.UsePlayerSettings : RenderingPath.Forward;}
            canvasCount=(int)AccessTools.Field(typeof(SceneUiCapture),"CanvasCount").GetValue(__instance);
            if(!freeze) return true;
            __result=true; return false;
        }
        static void UiPostfix(object __instance,long __state) {uiMs+=Elapsed(__state); uiCalls++; layerCount=((ICollection)AccessTools.Field(typeof(SceneUiCapture),"layers").GetValue(__instance)).Count; redrawn=(bool)AccessTools.Field(typeof(SceneUiCapture),"UiRedrawn").GetValue(__instance) ? 1 : 0;}
        static void CapturePrefix(out long __state) {__state=Stamp();}
        static void CapturePostfix(long __state) {captureMs+=Elapsed(__state);}
        public static IEnumerator Run(UnityModManager.ModEntry entry) {
            var settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            bool enabled=settings.Enabled; int limit=Application.targetFrameRate,queued=QualitySettings.maxQueuedFrames;
            var preferences=AccessTools.TypeByName("GamePreferences");
            var key=Enum.Parse(AccessTools.TypeByName("Preferences"),"FrameLimit");
            var get=AccessTools.GetDeclaredMethods(preferences).Find(m=>m.Name=="Get" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(int));
            var set=AccessTools.GetDeclaredMethods(preferences).Find(m=>m.Name=="Set" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(int));
            int savedLimit=(int)get.Invoke(null,new[]{key});
            Action<int> changeLimit=value=> {
                set.Invoke(null,new object[]{key,value});
                var limiter=AccessTools.TypeByName("FrameLimiter");
                foreach(var obj in Resources.FindObjectsOfTypeAll(limiter)) {var c=obj as Component; if(c && c.gameObject.activeInHierarchy) AccessTools.Method(limiter,"FrameLimitUpdated").Invoke(c,null);}
            };
            var harmony=new Harmony("DVScaling.Tests.Throughput");
            harmony.Patch(AccessTools.Method(typeof(SceneUiCapture),"RenderUi"),new HarmonyMethod(typeof(ThroughputWorldProbe),"UiPrefix"),new HarmonyMethod(typeof(ThroughputWorldProbe),"UiPostfix"));
            harmony.Patch(AccessTools.Method(typeof(SceneUiCapture),"Capture"),new HarmonyMethod(typeof(ThroughputWorldProbe),"CapturePrefix"),new HarmonyMethod(typeof(ThroughputWorldProbe),"CapturePostfix"));
            string command=""; var samples=new List<string>();
            Action flush=()=> {if(command.Length>0) File.WriteAllText(Path.Combine(entry.Path,"throughput-"+command.Split(';')[0]+".csv"),"qpc,frame_ms,ui_ms,capture_ms,ui_calls,canvases,target,vsync,layers,ui_redrawn,queued\n"+string.Join("\n",samples.ToArray()));};
            File.WriteAllText(Path.Combine(entry.Path,"throughput-ready.txt"),DateTime.UtcNow.ToString("O"));
            try {
                while(File.Exists(Path.Combine(entry.Path,"throughput-live.txt"))) {
                    string path=Path.Combine(entry.Path,"throughput-command.txt");
                    string next="";
                    // The controller may hold a short exclusive write lock.
                    // Retry next frame rather than ending the whole probe.
                    try {next=File.Exists(path) ? File.ReadAllText(path).Trim() : "";} catch(IOException) {}
                    if(next.Length>0 && next!=command) {
                        flush(); samples.Clear(); var fields=next.Split(';');
                        freeze=fields[2]=="freeze"; legacy=fields[2]=="legacy"; settings.Enabled=bool.Parse(fields[1]);
                        DVLSFG_SetLegacyPresentWait(fields[2]=="serial" ? 1 : 0);
                        DVLSFG_SetPacingHeadroom(fields[2]=="exact" ? 1f : fields[2]=="fast" ? .9f : .98f);
                        QualitySettings.maxQueuedFrames=fields[2].StartsWith("queue") ? int.Parse(fields[2].Substring(5)) : queued;
                        changeLimit(int.Parse(fields[3]));
                        AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                        command=next; File.WriteAllText(Path.Combine(entry.Path,"throughput-ack.txt"),command);
                    }
                    if(command.Length>0) samples.Add(string.Format(Inv,"{0:F6},{1:F5},{2:F5},{3:F5},{4},{5},{6},{7},{8},{9},{10}",Qpc(),Time.unscaledDeltaTime*1000,uiMs,captureMs,uiCalls,canvasCount,Application.targetFrameRate,QualitySettings.vSyncCount,layerCount,redrawn,QualitySettings.maxQueuedFrames));
                    uiMs=captureMs=0; uiCalls=0;
                    yield return null;
                }
            } finally {
                flush(); freeze=legacy=false; harmony.UnpatchAll(harmony.Id);
                DVLSFG_SetLegacyPresentWait(0);
                DVLSFG_SetPacingHeadroom(.98f);
                settings.Enabled=enabled; Application.targetFrameRate=limit; QualitySettings.maxQueuedFrames=queued;
                changeLimit(savedLimit);
                AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
            }
        }
    }
}

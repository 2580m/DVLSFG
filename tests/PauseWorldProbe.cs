using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    public static class PauseWorldProbe {
        static readonly List<string> events=new List<string>();
        static string stage;
        static int calls,resets,creates,releases,fgResets;
        static string diagnosticPath;
        static void SrPostfix(Fsr3SceneCamera __instance) {
            calls++;
            if((bool)AccessTools.Field(typeof(Fsr3SceneCamera),"reset").GetValue(__instance)) resets++;
        }
        static void Created() {creates++;}
        static void Released(Fsr3SceneCamera __instance) {
            releases++;
            if(AccessTools.Field(typeof(Fsr3SceneCamera),"context").GetValue(__instance)==null) return;
            var c=__instance.GetComponent<Camera>();
            File.AppendAllText(diagnosticPath,"Release stage="+stage+" frame="+Time.frameCount+" enabled="+c.enabled+" active="+c.gameObject.activeInHierarchy+" mask="+c.cullingMask+" target="+(c.targetTexture ? c.targetTexture.name : "none")+"\n"+Environment.StackTrace+"\n");
        }
        static void FgPostfix(TemporalSceneCamera __instance) {if((bool)AccessTools.Property(typeof(TemporalSceneCamera),"HistoryReset").GetValue(__instance,null)) fgResets++;}
        public static IEnumerator Run(UnityModManager.ModEntry entry) {
            diagnosticPath=Path.Combine(entry.Path,"pause-release.log");
            var h=new Harmony("DVScaling.Tests.Pause");
            h.Patch(AccessTools.Method(typeof(Fsr3SceneCamera),"OnPreCull"),postfix:new HarmonyMethod(typeof(PauseWorldProbe),"SrPostfix"));
            h.Patch(AccessTools.Method(typeof(Fsr3SceneCamera),"Create"),postfix:new HarmonyMethod(typeof(PauseWorldProbe),"Created"));
            h.Patch(AccessTools.Method(typeof(Fsr3SceneCamera),"Release"),prefix:new HarmonyMethod(typeof(PauseWorldProbe),"Released"));
            h.Patch(AccessTools.Method(typeof(TemporalSceneCamera),"Capture"),postfix:new HarmonyMethod(typeof(PauseWorldProbe),"FgPostfix"));
            var app=AccessTools.TypeByName("DV.AppUtil");
            var appInstance=UnityEngine.Object.FindObjectOfType(app);
            var settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            bool enabled=settings.Enabled;
            events.Clear(); events.Add("stage,frame,dt_ms,scale,sr_calls,sr_resets,creates,releases,fg_resets,camera,enabled,mask,output,jitter");
            try {
                yield return new WaitForSecondsRealtime(8);
                var snapshotCamera=UnityEngine.Object.FindObjectOfType<Fsr3SceneCamera>().GetComponent<Camera>();
                var target=snapshotCamera.targetTexture;
                var originalOutput=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"OutputTarget").GetValue(snapshotCamera.GetComponent<Fsr3SceneCamera>());
                var snapshot=new RenderTexture(320,180,24);
                snapshot.Create();
                AccessTools.Method(AccessTools.TypeByName("ScreenshotTaker"),"TakeScreenshotWithoutUI").Invoke(null,new object[]{snapshot});
                File.WriteAllText(Path.Combine(entry.Path,"snapshot-target.txt"),"same="+(snapshotCamera.targetTexture==target)+" before="+(target ? target.GetInstanceID().ToString() : "none")+" after="+(snapshotCamera.targetTexture ? snapshotCamera.targetTexture.GetInstanceID().ToString() : "none"));
                UnityEngine.Object.Destroy(snapshot);
                if(snapshotCamera.targetTexture!=target) throw new Exception("Native snapshot lost the SR scene target");
                calls=resets=creates=releases=fgResets=0;
                int segment=0;
                foreach(bool fg in new[]{false,true}) {
                    settings.Enabled=fg; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                    foreach(bool paused in new[]{false,true,false,true,false}) {
                        stage=(fg ? "fg" : "sr")+(paused ? "-paused" : "-running");
                        ++segment; int captured=0;
                        AccessTools.Method(app,paused ? "PauseGame" : "UnpauseGame").Invoke(appInstance,null);
                        float end=Time.realtimeSinceStartup+4;
                        while(Time.realtimeSinceStartup<end) {
                            yield return new WaitForEndOfFrame();
                            foreach(var sr in UnityEngine.Object.FindObjectsOfType<Fsr3SceneCamera>()) {
                                var camera=sr.GetComponent<Camera>(); var output=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"OutputTarget").GetValue(sr);
                                if(camera!=snapshotCamera) continue;
                                if(camera.targetTexture!=target || output!=originalOutput || creates!=0 || releases!=0) throw new Exception("Pause menu rebuilt the SR context or changed its targets");
                                var jitter=AccessTools.Field(typeof(Fsr3SceneCamera),"jitter").GetValue(sr);
                                events.Add(string.Join(",",new[]{stage,Time.frameCount.ToString(),(Time.unscaledDeltaTime*1000).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),Time.timeScale.ToString(),calls.ToString(),resets.ToString(),creates.ToString(),releases.ToString(),fgResets.ToString(),camera.name,camera.enabled.ToString(),camera.cullingMask.ToString(),output ? output.GetInstanceID().ToString() : "none",jitter.ToString().Replace(',', ';')}));
                                if(output && Time.realtimeSinceStartup>end-2 && captured<12) {
                                    var old=RenderTexture.active;
                                    var pixels=new Texture2D(256,256,TextureFormat.RGB24,false);
                                    try {RenderTexture.active=output; pixels.ReadPixels(new Rect(output.width*3/4-128,output.height/2-128,256,256),0,0); pixels.Apply(); File.WriteAllBytes(Path.Combine(entry.Path,"pause-"+segment+"-"+captured+".png"),pixels.EncodeToPNG());}
                                    finally {RenderTexture.active=old; UnityEngine.Object.Destroy(pixels);}
                                    captured++;
                                }
                            }
                            calls=resets=creates=releases=fgResets=0;
                        }
                        File.WriteAllLines(Path.Combine(entry.Path,"pause.csv"),events.ToArray());
                        entry.Logger.Log("Pause probe completed "+stage);
                    }
                }
            } finally {
                AccessTools.Method(app,"UnpauseGame").Invoke(appInstance,null);
                settings.Enabled=enabled; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                h.UnpatchAll(h.Id);
            }
            File.WriteAllText(Path.Combine(entry.Path,"pause-passed.txt"),"Pause diagnostic completed");
        }
    }
}

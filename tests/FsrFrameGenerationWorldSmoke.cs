using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DV.UI;
using DVLSFG;
namespace DVLSFGTests {
    public static class FsrFrameGenerationWorldSmoke {
        public static bool Load(UnityModManager.ModEntry entry) {
            var go=new GameObject("FSR3 world validation"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FsrFrameGenerationWorldSmokeRunner>().Entry=entry; return true;
        }
    }
    [DefaultExecutionOrder(2000)]
    public sealed class FsrFrameGenerationWorldSmokeRunner : MonoBehaviour {
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern int DVLSFG_GetStatus(IntPtr text,uint capacity,out ulong real,out ulong generated);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_RequestCapture();
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_GetMetrics(out float input,out float submitted,out float mean,out float peak);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long value);
        Transform movingCamera; Quaternion originalRotation; int originalFrameLimit; float motionStarted;
        void LateUpdate() {if(movingCamera) movingCamera.localRotation=originalRotation*Quaternion.Euler(0,Mathf.Sin((Time.realtimeSinceStartup-motionStarted)*.7f)*12,0);}
        ulong lastReal,lastGenerated;
        public UnityModManager.ModEntry Entry;
        Settings settings;
        int oldMode,oldTechnology,oldWidth,oldHeight;
        FullScreenMode oldScreenMode;
        void Log(string text) {Entry.Logger.Log(text); File.AppendAllText(Path.Combine(Entry.Path,"world-framegen.log"),text+"\n");}
        void Fail(Exception error) {Log("FAIL "+error); Restore(); File.WriteAllText(Path.Combine(Entry.Path,"failed.txt"),error.ToString());}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(25);
            settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            Log("Startup enabled="+settings.Enabled+" technology="+settings.FrameGenerationTechnology+" targetFrameRate="+Application.targetFrameRate+" vSyncCount="+QualitySettings.vSyncCount+" refresh="+Screen.currentResolution.refreshRate);
            oldMode=settings.FsrMode; oldTechnology=settings.FsrTechnology; oldWidth=Screen.width; oldHeight=Screen.height; oldScreenMode=Screen.fullScreenMode;
            if(File.Exists(Path.Combine(Entry.Path,"ui-cache-test.txt"))) {
                yield return UiCaptureCacheSmoke.Run(Entry);
                if(!UiCaptureCacheSmoke.Passed) {Restore(); yield break;}
            }
            MainMenuController activeMenu=null;
            float menuDeadline=Time.realtimeSinceStartup+90;
            while(!activeMenu && Time.realtimeSinceStartup<menuDeadline) {
                foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(menu.gameObject.activeInHierarchy) {activeMenu=menu; break;}
                if(!activeMenu) yield return new WaitForSecondsRealtime(1);
            }
            if(!activeMenu) {Fail(new Exception("Main menu not loaded")); yield break;}
            Log("Menu targetFrameRate="+Application.targetFrameRate+" vSyncCount="+QualitySettings.vSyncCount);
            try {
                var sliderTemplate=Resources.FindObjectsOfTypeAll<SettingChangeSourceSlider>()[0];
                var staging=new GameObject("Destroyed slider callback validation"); staging.SetActive(false);
                var stale=Instantiate(sliderTemplate,staging.transform,false);
                DestroyImmediate(stale.gameObject);
                AccessTools.Method(typeof(SettingChangeSourceSlider),"OnResetOrApplied").Invoke(stale,null);
                DestroyImmediate(staging);
                Log("PASS destroyed settings-slider callback safely ignored");
                var uiType=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(uiType,"mInstance").GetValue(null),new object[]{false});
                ((DV.UIFramework.ButtonDV)AccessTools.Field(typeof(MainMenuController),"continueButton").GetValue(activeMenu)).Click();
            } catch(Exception error) {Fail(error); yield break;}
            yield return new WaitForSecondsRealtime(2);
            try {
                var popup=AccessTools.TypeByName("DV.UI.LauncherController");
                foreach(var obj in Resources.FindObjectsOfTypeAll(popup)) {var component=obj as Component; if(component && component.gameObject.activeInHierarchy) {((UnityEngine.UI.Button)AccessTools.Field(popup,"runButton").GetValue(component)).onClick.Invoke(); break;}}
                settings.FsrMode=oldMode>0 ? oldMode : 3; settings.FsrTechnology=2;
            } catch(Exception error) {Fail(error); yield break;}
            float deadline=Time.realtimeSinceStartup+180;
            while(Time.realtimeSinceStartup<deadline && !Find()) yield return new WaitForSecondsRealtime(1);
            if(!Find()) {Fail(new Exception("FSR3 world camera did not become ready")); yield break;}
            Log("World targetFrameRate="+Application.targetFrameRate+" vSyncCount="+QualitySettings.vSyncCount+" refresh="+Screen.currentResolution.refreshRate);
            if(File.Exists(Path.Combine(Entry.Path,"thumbnail-first.txt"))) yield return CheckSaveThumbnails();
            if(File.Exists(Path.Combine(Entry.Path,"pause-test.txt"))) {
                yield return PauseWorldProbe.Run(Entry);
                Restore(); yield break;
            }
            if(File.Exists(Path.Combine(Entry.Path,"throughput-live.txt"))) {
                yield return ThroughputWorldProbe.Run(Entry);
                Restore(); yield break;
            }
            if(File.Exists(Path.Combine(Entry.Path,"zoom-live.txt"))) {
                yield return ZoomWorldProbe.Run(Entry);
                Restore(); yield break;
            }
            if(File.Exists(Path.Combine(Entry.Path,"thumbnail-only.txt"))) {
                yield return CheckSaveThumbnails();
                Restore(); yield break;
            }
            if(File.Exists(Path.Combine(Entry.Path,"comparison.txt"))) {
                yield return CompareBackends();
                Restore(); yield break;
            }
            if(File.Exists(Path.Combine(Entry.Path,"benchmark.txt"))) {
                foreach(bool enabled in new[]{false,true,false,true}) {
                    settings.Enabled=enabled;
                    AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                    yield return new WaitForSecondsRealtime(3);
                    var samples=new System.Collections.Generic.List<float>();
                    float end=Time.realtimeSinceStartup+8;
                    while(Time.realtimeSinceStartup<end) {yield return null; samples.Add(Time.unscaledDeltaTime);}
                    double total=0; foreach(var sample in samples) total+=sample;
                    samples.Sort();
                    Log("BENCH FG="+enabled+" SR="+settings.FsrMode+" average="+(samples.Count/total).ToString("F2")+" median-ms="+(samples[samples.Count/2]*1000).ToString("F3")+" p99-ms="+(samples[(int)(samples.Count*.99)]*1000).ToString("F3"));
                }
            }
            originalFrameLimit=Application.targetFrameRate; Application.targetFrameRate=30;
            movingCamera=Find().transform; originalRotation=movingCamera.localRotation; motionStarted=Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(3);
            DVLSFG_RequestCapture();
            yield return new WaitForSecondsRealtime(3);
            Application.targetFrameRate=originalFrameLimit;
            yield return new WaitForSecondsRealtime(3);
            DVLSFG_RequestCapture();
            yield return new WaitForSecondsRealtime(3);
            movingCamera.localRotation=originalRotation; movingCamera=null;
            Application.targetFrameRate=originalFrameLimit;
            Log("World motion and overlay mask captured at 30 FPS source");
            if(File.Exists(Path.Combine(Entry.Path,"capture-only.txt"))) {
                Restore(); File.WriteAllText(Path.Combine(Entry.Path,"capture-passed.txt"),"Real world motion capture complete"); yield break;
            }
            for(int quality=1;quality<=5;quality++) {
                settings.FsrMode=quality;
                yield return new WaitForSecondsRealtime(4);
                try {Check("world quality "+quality);} catch(Exception error) {Fail(error); yield break;}
            }
            var renderingCamera=Find().GetComponent<Camera>();
            var originalPath=renderingCamera.renderingPath;
            foreach(var path in new[]{RenderingPath.Forward,RenderingPath.DeferredShading}) {
                renderingCamera.renderingPath=path;
                yield return new WaitForSecondsRealtime(3);
                try {Check("rendering path "+path);} catch(Exception error) {renderingCamera.renderingPath=originalPath; Fail(error); yield break;}
            }
            renderingCamera.renderingPath=originalPath;
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(5);
            try {Check("resolution change");} catch(Exception error) {Fail(error); yield break;}
            settings.FsrTechnology=1; settings.FsrMode=2;
            yield return new WaitForSecondsRealtime(3);
            try {foreach(var fsr in Resources.FindObjectsOfTypeAll<Fsr3SceneCamera>()) if(fsr.GetComponent<Camera>().targetTexture && (bool)AccessTools.Property(typeof(Fsr3SceneCamera),"OwnsSceneTarget").GetValue(fsr,null)) throw new Exception("FSR3 ownership was not released on FSR1 switch"); Log("PASS FSR3 -> FSR1 release");} catch(Exception error) {Fail(error); yield break;}
            settings.FsrTechnology=2;
            yield return new WaitForSecondsRealtime(4);
            try {Check("FSR1 -> FSR3");} catch(Exception error) {Fail(error); yield break;}
            Restore();
            File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"FSR3 game world, five quality modes, resize, picking and FSR1 transitions passed");
        }
        Fsr3SceneCamera Find() {
            foreach(var fsr in Resources.FindObjectsOfTypeAll<Fsr3SceneCamera>()) {
                var camera=fsr.GetComponent<Camera>();
                if(fsr.isActiveAndEnabled && camera && camera.enabled && camera.targetTexture && (bool)AccessTools.Property(typeof(Fsr3SceneCamera),"OwnsSceneTarget").GetValue(fsr,null) && (ulong)AccessTools.Field(typeof(Fsr3SceneCamera),"frames").GetValue(fsr)>30) return fsr;
            }
            return null;
        }
        IEnumerator CheckSaveThumbnails() {
            var managerType=AccessTools.TypeByName("SaveGameManager");
            var manager=UnityEngine.Object.FindObjectOfType(managerType);
            bool originalEnabled=settings.Enabled;
            bool passed=true;
            try {
                foreach(int upscaler in new[]{0,1,2}) {
                    settings.FsrTechnology=upscaler; settings.FsrMode=upscaler==0 ? 0 : oldMode>0 ? oldMode : 3;
                    foreach(bool enabled in new[]{false,true}) {
                        ulong beforeReal,beforeGenerated; DVLSFG_GetStatus(IntPtr.Zero,0,out beforeReal,out beforeGenerated);
                        settings.Enabled=enabled; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                        yield return new WaitForSecondsRealtime(3);
                        if(enabled) {
                            ulong real,generated; DVLSFG_GetStatus(IntPtr.Zero,0,out real,out generated);
                            if(generated<=beforeGenerated) {passed=false; Log("FAIL frame generation did not advance for upscaler "+upscaler);}
                        }
                        foreach(bool stashed in new[]{false,true}) {
                            var activeCamera=(Camera)AccessTools.Property(AccessTools.TypeByName("PlayerManager"),"ActiveCamera").GetValue(null,null);
                            var previousTarget=activeCamera.targetTexture;
                            AccessTools.Method(managerType,"ClearStashedScreenshot").Invoke(manager,null);
                            string active=RenderTexture.active ? RenderTexture.active.name : "screen";
                            if(stashed) AccessTools.Method(managerType,"StashScreenshot").Invoke(manager,null);
                            var image=(Texture2D)AccessTools.Method(managerType,"GetThumbnail").Invoke(manager,null);
                            if(activeCamera.targetTexture!=previousTarget) {passed=false; Log("FAIL snapshot changed scene camera target for upscaler "+upscaler);}
                            var pixels=image.GetPixels32(); double sum=0,squares=0;
                            foreach(var pixel in pixels) {double value=(pixel.r+pixel.g+pixel.b)/3.0; sum+=value; squares+=value*value;}
                            double mean=sum/pixels.Length,variance=squares/pixels.Length-mean*mean;
                            string name="thumbnail-sr"+upscaler+"-fg"+(enabled ? "on" : "off")+(stashed ? "-stashed" : "-automatic");
                            File.WriteAllBytes(Path.Combine(Entry.Path,name+".png"),image.EncodeToPNG());
                            Log(name+" active="+active+" mean="+mean.ToString("F2")+" deviation="+Math.Sqrt(Math.Max(0,variance)).ToString("F2"));
                            if(variance<4) {passed=false; Log("FAIL blank "+name);}
                        }
                    }
                }
            } finally {
                AccessTools.Method(managerType,"ClearStashedScreenshot").Invoke(manager,null);
                settings.Enabled=originalEnabled; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
            }
            File.WriteAllText(Path.Combine(Entry.Path,passed ? "thumbnail-passed.txt" : "thumbnail-failed.txt"),"Automatic and stashed thumbnail pixel checks complete");
        }
        IEnumerator CompareBackends() {
            var preferences=AccessTools.TypeByName("GamePreferences");
            var key=Enum.Parse(AccessTools.TypeByName("Preferences"),"FrameLimit");
            var get=AccessTools.GetDeclaredMethods(preferences).Find(m=>m.Name=="Get" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(int));
            var set=AccessTools.GetDeclaredMethods(preferences).Find(m=>m.Name=="Set" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(int));
            int previous=(int)get.Invoke(null,new[]{key}); bool enabled=settings.Enabled;
            originalFrameLimit=Application.targetFrameRate;
            movingCamera=Find().transform; originalRotation=movingCamera.localRotation; motionStarted=Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(15);
            var report=Path.Combine(Entry.Path,"comparison.csv");
            File.WriteAllText(report,"backend,limit,enabled,start_qpc_ms,end_qpc_ms,source_fps,native_real,native_generated,target,vsync\n");
            bool live=File.Exists(Path.Combine(Entry.Path,"comparison-live.txt"));
            if(live) {
                File.WriteAllText(Path.Combine(Entry.Path,"live-ready.txt"),settings.FrameGenerationTechnology.ToString());
                string last="";
                while(File.Exists(Path.Combine(Entry.Path,"comparison-live.txt"))) {
                    string commandFile=Path.Combine(Entry.Path,"live-command.txt");
                    string command=File.Exists(commandFile) ? File.ReadAllText(commandFile).Trim() : "";
                    if(command.Length>0 && command!=last) {
                        var fields=command.Split(';'); int limit=int.Parse(fields[1]);
                        if(limit!=30 && limit!=60 && limit!=90 && limit!=999) throw new Exception("Invalid comparison limit");
                        if(fields.Length>3 && fields[3]!="capture") {
                            int multiplier=int.Parse(fields[3]);
                            if(multiplier<2 || multiplier>4) throw new Exception("Invalid comparison multiplier");
                            settings.Multiplier=multiplier;
                        }
                        settings.Enabled=bool.Parse(fields[2]); AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                        set.Invoke(null,new object[]{key,limit});
                        var limiter=AccessTools.TypeByName("FrameLimiter");
                        foreach(var obj in Resources.FindObjectsOfTypeAll(limiter)) {var c=obj as Component; if(c && c.gameObject.activeInHierarchy) AccessTools.Method(limiter,"FrameLimitUpdated").Invoke(c,null);}
                        long qpc,frequency; QueryPerformanceCounter(out qpc); QueryPerformanceFrequency(out frequency);
                        File.WriteAllText(Path.Combine(Entry.Path,"live-ack.txt"),command+";"+settings.FrameGenerationTechnology+";"+Application.targetFrameRate+";"+QualitySettings.vSyncCount+";"+(qpc/(double)frequency).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+";"+DateTime.UtcNow.ToString("O"));
                        last=command;
                        if(fields.Length>3 && fields[3]=="capture") DVLSFG_RequestCapture();
                    }
                    yield return new WaitForSecondsRealtime(.5f);
                }
            }
            foreach(int limit in live ? new int[0] : new[]{30,60,90,999}) {
                foreach(bool generation in limit==30 || limit==60 ? new[]{false,true} : new[]{true}) {
                    settings.Enabled=generation;
                    AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                    set.Invoke(null,new object[]{key,limit});
                    var limiter=AccessTools.TypeByName("FrameLimiter");
                    foreach(var obj in Resources.FindObjectsOfTypeAll(limiter)) {var component=obj as Component; if(component && component.gameObject.activeInHierarchy) AccessTools.Method(limiter,"FrameLimitUpdated").Invoke(component,null);}
                    yield return new WaitForSecondsRealtime(4);
                    ulong real0,gen0,real1,gen1; DVLSFG_GetStatus(IntPtr.Zero,0,out real0,out gen0);
                    int frame=Time.frameCount;
                    double start=System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    Log("COMPARISON START backend="+settings.FrameGenerationTechnology+" limit="+limit+" enabled="+generation+" qpc_ms="+start.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    yield return new WaitForSecondsRealtime(12);
                    double end=System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    DVLSFG_GetStatus(IntPtr.Zero,0,out real1,out gen1);
                    var row=string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3:F3},{4:F3},{5:F3},{6},{7},{8},{9}",settings.FrameGenerationTechnology,limit,generation,start,end,(Time.frameCount-frame)*1000/(end-start),real1-real0,gen1-gen0,Application.targetFrameRate,QualitySettings.vSyncCount);
                    File.AppendAllText(report,row+"\n"); Log("COMPARISON END "+row);
                    if(generation && (limit==30 || limit==999)) {DVLSFG_RequestCapture(); yield return new WaitForSecondsRealtime(2);}
                }
            }
            settings.Enabled=enabled; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
            set.Invoke(null,new object[]{key,previous});
            File.WriteAllText(Path.Combine(Entry.Path,"comparison-passed.txt"),"Comparison samples collected; inspect independent PresentMon data.");
        }
        void Check(string stage) {
            ulong real,generated; int active=DVLSFG_GetStatus(IntPtr.Zero,0,out real,out generated);
            if(active!=1 || generated<=lastGenerated || real<=lastReal || Math.Abs((long)(real-lastReal)-(long)(generated-lastGenerated))>2) throw new Exception("FSR FG inactive or not 2x: "+stage+" real="+real+" generated="+generated);
            lastReal=real; lastGenerated=generated;
            float inputRate,submittedRate,mean,peak; DVLSFG_GetMetrics(out inputRate,out submittedRate,out mean,out peak);
            Log("Rates "+stage+": input="+inputRate+" submitted="+submittedRate+" target="+Application.targetFrameRate+" vsync="+QualitySettings.vSyncCount);
            var fsr=Find(); if(!fsr) throw new Exception("No FSR3 world output: "+stage);
            var camera=fsr.GetComponent<Camera>();
            if(!camera || !camera.targetTexture) throw new Exception("World scene target missing: "+stage);
            var temporal=camera.GetComponent<TemporalSceneCamera>();
            if(!temporal) throw new Exception("World temporal component missing: "+camera.name);
            var depth=temporal ? (RenderTexture)AccessTools.Field(typeof(TemporalSceneCamera),"depth").GetValue(temporal) : null;
            var motion=temporal ? (RenderTexture)AccessTools.Field(typeof(TemporalSceneCamera),"motion").GetValue(temporal) : null;
            if(!depth || !motion || depth.width!=camera.targetTexture.width || depth.height!=camera.targetTexture.height) throw new Exception("World temporal inputs do not match scene rendering");
            Log("PASS world FSR frame generation 2x: real="+real+" generated="+generated+" input="+depth.width+"x"+depth.height);
            var output=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"OutputTarget").GetValue(fsr);
            if(!output || output.width!=Screen.width || output.height!=Screen.height || camera.allowMSAA || camera.targetTexture.antiAliasing!=1) throw new Exception("Invalid FSR3 output or scene AA still active");
            var previous=RenderTexture.active; RenderTexture.active=output;
            var pixels=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            bool nonblack=false;
            try {
                foreach(var uv in new[]{new Vector2(.25f,.25f),new Vector2(.5f,.5f),new Vector2(.75f,.75f)}) {
                    pixels.ReadPixels(new Rect(output.width*uv.x,output.height*uv.y,1,1),0,0); pixels.Apply(); var color=pixels.GetPixel(0,0);
                    if(float.IsNaN(color.r) || float.IsInfinity(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b)) throw new Exception("Non-finite FSR3 output");
                    nonblack|=color.r+color.g+color.b>.001f;
                    var actual=camera.ScreenPointToRay(new Vector3(Screen.width*uv.x,Screen.height*uv.y,0)); var expected=camera.ViewportPointToRay(uv);
                    if(Vector3.Angle(actual.direction,expected.direction)>.001f || Vector3.Distance(actual.origin,expected.origin)>.001f) throw new Exception("FSR3 display mouse ray mismatch");
                }
            } finally {RenderTexture.active=previous; Destroy(pixels);}
            if(!nonblack) throw new Exception("FSR3 world output is black");
            var post=AccessTools.Field(typeof(Fsr3SceneCamera),"post").GetValue(fsr);
            if(post!=null && AccessTools.Field(post.GetType(),"antialiasingMode").GetValue(post).ToString()!="None") throw new Exception("Native postprocess AA remains enabled");
            if((camera.depthTextureMode & (DepthTextureMode.Depth|DepthTextureMode.MotionVectors))!=(DepthTextureMode.Depth|DepthTextureMode.MotionVectors)) throw new Exception("Temporal inputs not enabled");
            var capture=UnityEngine.Object.FindObjectOfType<SceneUiCapture>();
            if(capture) {
                var captured=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"scene").GetValue(capture);
                if(!captured || captured.width!=Screen.width || captured.height!=Screen.height || (int)AccessTools.Field(typeof(SceneUiCapture),"CanvasCount").GetValue(capture)<1) throw new Exception("Scene/UI world capture is inactive");
                var uiTarget=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"uiMsaa").GetValue(capture);
                if(!uiTarget || uiTarget.antiAliasing<4) throw new Exception("World UI AA inactive");
                foreach(var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>()) if(canvas.isRootCanvas && canvas.renderMode!=RenderMode.WorldSpace && canvas.gameObject.layer==31) throw new Exception("UI layer was not restored after capture");
                Log("PASS scene/UI world capture "+captured.width+"x"+captured.height+" UI MSAA="+uiTarget.antiAliasing+" canvases="+AccessTools.Field(typeof(SceneUiCapture),"CanvasCount").GetValue(capture));
            }
            Log("PASS "+stage+" camera="+camera.name+" target="+camera.targetTexture.width+"x"+camera.targetTexture.height+" output="+output.width+"x"+output.height+" rendering="+camera.actualRenderingPath+" post-AA="+(post==null ? "no PPV2 layer" : "None")+" scene MSAA disabled, UI/global MSAA="+QualitySettings.antiAliasing+", depth/motion enabled, finite pixels and picking rays");
        }
        void Restore() {if(movingCamera) {movingCamera.localRotation=originalRotation; movingCamera=null; Application.targetFrameRate=originalFrameLimit;} if(settings!=null) {settings.FsrMode=oldMode; settings.FsrTechnology=oldTechnology;} Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);}
    }
}

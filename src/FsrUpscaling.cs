using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace DVLSFG {
    internal static class FsrRuntime {
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("DVLSFG.FSR",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr DVFSR_GetRenderEvent();
        [DllImport("DVLSFG.FSR",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr DVFSR_Create(IntPtr input,IntPtr output,float sharpness);
        [DllImport("DVLSFG.FSR",CallingConvention=CallingConvention.Cdecl)] internal static extern int DVFSR_Status(IntPtr job,out ulong frames);
        [DllImport("DVLSFG.FSR",CallingConvention=CallingConvention.Cdecl)] internal static extern void DVFSR_Sharpness(IntPtr job,float value);
        internal static IntPtr callback;
        static bool attempted;
        static bool wasRequested;
        internal static readonly List<FsrSceneCamera> Cameras=new List<FsrSceneCamera>();
        static Type graphicsType;
        static FieldInfo gameCameraField;
        internal static string failure;
        internal static readonly float[] Ratios={1f,1f/1.3f,1f/1.5f,1f/1.7f,.5f,1f/3f};
        internal static bool Requested {get {return Main.modEntry!=null && Main.modEntry.Active && Main.settings.FsrMode>0;}}
        static void Metadata() {
            if(graphicsType!=null) return;
            graphicsType=AccessTools.TypeByName("DV.VFX.CameraGraphicsUpdater");
            if(graphicsType!=null) gameCameraField=AccessTools.Field(graphicsType,"isGameCam");
        }
        internal static void RegisterGraphics(Component component) {
            Metadata();
            if(!component || gameCameraField==null || !(bool)gameCameraField.GetValue(component)) return;
            var camera=component.GetComponent<Camera>();
            if(camera && !camera.GetComponent<FsrSceneCamera>()) camera.gameObject.AddComponent<FsrSceneCamera>();
            if(camera && !camera.GetComponent<Fsr3SceneCamera>()) camera.gameObject.AddComponent<Fsr3SceneCamera>().enabled=false;
            if(camera && !camera.GetComponent<SceneUiSceneCamera>()) camera.gameObject.AddComponent<SceneUiSceneCamera>().enabled=SceneUiRuntime.Active && !Requested;
            if(camera && !camera.GetComponent<TemporalSceneCamera>()) camera.gameObject.AddComponent<TemporalSceneCamera>().enabled=SceneUiRuntime.Active && Main.settings.FrameGenerationTechnology==2;
        }
        internal static void Tick() {
            Fsr3Runtime.Tick();
            if(!Requested) {wasRequested=false; return;}
            if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11) {failure="Requires D3D11 / DXVK"; return;}
            if(!attempted) {
                attempted=true;
                try {
                    var module=LoadLibrary(System.IO.Path.Combine(Main.modEntry.Path,"DVLSFG.FSR.dll"));
                    if(module==IntPtr.Zero) throw new InvalidOperationException("FSR plugin load failed: "+Marshal.GetLastWin32Error());
                    callback=DVFSR_GetRenderEvent();
                    Metadata();
                } catch(Exception e) {failure=e.Message; Main.modEntry.Logger.Error("FSR: "+e);}
            }
            if(callback==IntPtr.Zero || wasRequested) return;
            wasRequested=true;
            // Restrict automatic ownership to the game's explicitly marked scene cameras.
            // Reflection, minimap and UI preview targets must keep their original resolutions.
            // One discovery on enable covers already-loaded cameras. New cameras register
            // through CameraGraphicsUpdater.Awake; stable gameplay performs no global scan.
            if(graphicsType!=null) foreach(var obj in Resources.FindObjectsOfTypeAll(graphicsType)) RegisterGraphics(obj as Component);
        }
        internal static string Status {
            get {
                if(Main.settings.FsrTechnology==2 && Requested) return Fsr3Runtime.Status;
                if(!Requested) return L10n.Get("dv_scaling/off");
                if(failure!=null) return L10n.Get("dv_scaling/unavailable")+failure;
                foreach(var camera in Cameras) {
                    if(!camera.isActiveAndEnabled || !camera.Scene || !camera.Scene.enabled) continue;
                    string message=camera.Status;
                    if(message!=null) return message;
                }
                return L10n.Get("dv_scaling/waiting_for_scene_camera");
            }
        }
    }

    // The scene camera renders its complete postprocessing chain into a smaller target.
    // A following, empty camera runs EASU/RCAS and composites back to the full-sized screen.
    // Screen Space Overlay canvases are subsequently rendered by Unity at output resolution.
    public sealed class FsrSceneCamera : MonoBehaviour {
        internal Camera Scene;
        Camera outputCamera;
        FsrOutputCamera output;
        RenderTexture sceneTarget,inputTarget,outputTarget;
        RenderTexture previousTarget;
        IntPtr job;
        int width,height,mode,samples;
        int failedMode=-1,failedWidth,failedHeight;
        string error;
        bool owned;
        internal bool OwnsSceneTarget { get { return owned && Scene && sceneTarget && Scene.targetTexture==sceneTarget; } }
        void Awake() {Scene=GetComponent<Camera>(); FsrRuntime.Cameras.Add(this);}
        internal string Status {
            get {
                if(error!=null) return error;
                if(job==IntPtr.Zero) return null;
                ulong frames; int state=FsrRuntime.DVFSR_Status(job,out frames);
                if(state<0) return "FSR GPU error 0x"+state.ToString("X8");
                return state==1 ? inputTarget.width+"×"+inputTarget.height+" → "+width+"×"+height : L10n.Get("dv_scaling/preparing");
            }
        }
        void LateUpdate() {
            try {
                if(!Scene || !Scene.enabled || Scene.cullingMask==0 || !FsrRuntime.Requested || Main.settings.FsrTechnology==2 || FsrRuntime.callback==IntPtr.Zero || Scene.stereoEnabled) {Release(); failedMode=-1; error=null; return;}
                if(failedMode==Main.settings.FsrMode && failedWidth==Screen.width && failedHeight==Screen.height) return;
                if(Scene.targetDisplay!=0) {Release(); error="FSR: primary display required"; return;}
                if(Scene.rect!=new Rect(0,0,1,1)) {Release(); error="FSR: full-screen scene camera required"; return;}
                if((!owned && Scene.targetTexture) || (owned && Scene.targetTexture!=sceneTarget)) {Release(); error="FSR: scene target owned by another renderer"; return;}
                int aa=Scene.actualRenderingPath==RenderingPath.Forward ? Mathf.Max(1,QualitySettings.antiAliasing) : 1;
                if(job!=IntPtr.Zero) {ulong frames; int state=FsrRuntime.DVFSR_Status(job,out frames); if(state<0) {error="FSR GPU error 0x"+state.ToString("X8"); Block(); return;}}
                if(job==IntPtr.Zero || width!=Screen.width || height!=Screen.height || mode!=Main.settings.FsrMode || samples!=aa) {
                    Release(); Create(Screen.width,Screen.height,Main.settings.FsrMode,aa);
                }
                if(job==IntPtr.Zero) return;
                FsrRuntime.DVFSR_Sharpness(job,Main.settings.FsrSharpness);
                outputCamera.depth=Scene.depth+.001f;
                outputCamera.enabled=Scene.enabled;
            } catch(Exception e) {error="FSR: "+e.Message; Block(); Main.modEntry.Logger.Error(error);}
        }
        void Block() {failedMode=Main.settings.FsrMode; failedWidth=Screen.width; failedHeight=Screen.height; Release();}
        static RenderTexture Target(int w,int h,int depth,int aa,bool randomWrite,string name) {
            var descriptor=new RenderTextureDescriptor(w,h);
            descriptor.graphicsFormat=QualitySettings.activeColorSpace==ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            descriptor.depthBufferBits=depth; descriptor.msaaSamples=aa; descriptor.enableRandomWrite=randomWrite;
            descriptor.useMipMap=false; descriptor.autoGenerateMips=false;
            var target=new RenderTexture(descriptor); target.name=name; target.filterMode=FilterMode.Bilinear;
            if(!target.Create()) {Destroy(target); throw new InvalidOperationException("RenderTexture allocation failed");}
            return target;
        }
        void Create(int w,int h,int selected,int aa) {
            if(w<16 || h<16) return;
            width=w; height=h; mode=selected; samples=aa;
            int rw=Mathf.Max(8,Mathf.RoundToInt(w*FsrRuntime.Ratios[selected]));
            int rh=Mathf.Max(8,Mathf.RoundToInt(h*FsrRuntime.Ratios[selected]));
            sceneTarget=Target(rw,rh,24,aa,false,"DVLSFG FSR scene");
            inputTarget=Target(rw,rh,0,1,false,"DVLSFG FSR display RGB input");
            outputTarget=Target(w,h,0,1,true,"DVLSFG FSR output");
            job=FsrRuntime.DVFSR_Create(inputTarget.GetNativeTexturePtr(),outputTarget.GetNativeTexturePtr(),Main.settings.FsrSharpness);
            if(job==IntPtr.Zero) throw new InvalidOperationException("Native FSR job allocation failed");
            var go=new GameObject("DVLSFG FSR compositor"); go.transform.SetParent(transform,false);
            outputCamera=go.AddComponent<Camera>(); outputCamera.cullingMask=0; outputCamera.clearFlags=CameraClearFlags.SolidColor;
            outputCamera.backgroundColor=Color.black;
            outputCamera.forceIntoRenderTexture=true;
            outputCamera.allowHDR=false; outputCamera.allowMSAA=false; outputCamera.depth=Scene.depth+.001f;
            output=go.AddComponent<FsrOutputCamera>(); output.Configure(sceneTarget,inputTarget,outputTarget,job);
            previousTarget=Scene.targetTexture; Scene.targetTexture=sceneTarget; owned=true; error=null;
            Main.modEntry.Logger.Log("FSR camera "+Scene.name+": "+rw+"x"+rh+" -> "+w+"x"+h+", AA="+aa);
        }
        void OnDisable() {Release();}
        void OnDestroy() {Release(); FsrRuntime.Cameras.Remove(this);}
        void Release() {
            if(outputCamera) {outputCamera.enabled=false; if(output) output.Detach(); Destroy(outputCamera.gameObject);}
            output=null; outputCamera=null;
            if(owned && Scene && Scene.targetTexture==sceneTarget) Scene.targetTexture=previousTarget;
            owned=false; previousTarget=null;
            if(job!=IntPtr.Zero) {
                using(var command=new CommandBuffer()) {
                    command.IssuePluginEventAndData(FsrRuntime.callback,2,job);
                    Graphics.ExecuteCommandBuffer(command);
                }
                job=IntPtr.Zero;
            }
            // Destroy queues Unity resource disposal after previously issued render commands.
            if(sceneTarget) Destroy(sceneTarget); if(inputTarget) Destroy(inputTarget); if(outputTarget) Destroy(outputTarget);
            sceneTarget=inputTarget=outputTarget=null;
        }
    }
    public sealed class FsrOutputCamera : MonoBehaviour {
        RenderTexture scene,input,output;
        IntPtr job;
        CommandBuffer commands;
        int preCullCount,imageCount;
        void OnPreCull() {if(preCullCount++==0) Main.modEntry.Logger.Log("FSR compositor OnPreCull");}
        internal void Configure(RenderTexture sceneTarget,RenderTexture inputTarget,RenderTexture outputTarget,IntPtr handle) {
            scene=sceneTarget; input=inputTarget; output=outputTarget; job=handle;
            commands=new CommandBuffer(); commands.name="DVLSFG FSR1 EASU + RCAS";
        }
        internal void Detach() {job=IntPtr.Zero; if(commands!=null) {commands.Dispose(); commands=null;}}
        void OnDestroy() {Detach();}
        void OnRenderImage(RenderTexture source,RenderTexture destination) {
            if(imageCount++==0) Main.modEntry.Logger.Log("FSR compositor OnRenderImage: "+source.width+"x"+source.height+", destination="+(destination ? destination.name : "screen"));
            if(job==IntPtr.Zero || commands==null) {Graphics.Blit(source,destination); return;}
            ulong frames; int state=FsrRuntime.DVFSR_Status(job,out frames);
            commands.Clear();
            commands.Blit(scene,input); // Resolve MSAA and preserve display encoding.
            if(state!=1) commands.Blit(input,output); // Defined first-frame/error fallback.
            commands.IssuePluginEventAndData(FsrRuntime.callback,1,job);
            Graphics.ExecuteCommandBuffer(commands);
            // OnRenderImage's null destination means the final display. CameraTarget in
            // a command buffer can instead name this camera's temporary image-effect RT.
            // Graphics.Blit also falls back to Camera.main.targetTexture: temporarily
            // detach that fallback so a low-resolution main camera cannot capture output.
            var main=Camera.main;
            var mainTarget=main ? main.targetTexture : null;
            if(destination==null && mainTarget) main.targetTexture=null;
            try {SceneUiRuntime.Capture(output); Graphics.Blit(output,destination);}
            finally {if(destination==null && mainTarget && main) main.targetTexture=mainTarget;}
        }
    }
    [HarmonyPatch]
    static class FsrScreenPointRay {
        // Patch the managed Vector2 wrapper shared by the public Vector3 overloads.
        // Input.mousePosition remains in display pixels; only our offscreen scene
        // cameras need conversion to the viewport shown by the FSR compositor.
        static MethodBase TargetMethod() {
            return AccessTools.Method(typeof(Camera),"ScreenPointToRay",new[]{typeof(Vector2),typeof(Camera.MonoOrStereoscopicEye)});
        }
        static bool Prefix(Camera __instance,Vector2 pos,Camera.MonoOrStereoscopicEye eye,ref Ray __result) {
            if(eye!=Camera.MonoOrStereoscopicEye.Mono || !__instance || Screen.width<=0 || Screen.height<=0) return true;
            foreach(var fsr in FsrRuntime.Cameras) {
                if(!fsr || fsr.Scene!=__instance || !fsr.OwnsSceneTarget) continue;
                __result=__instance.ViewportPointToRay(new Vector3(pos.x/Screen.width,pos.y/Screen.height,0),eye);
                return false;
            }
            foreach(var fsr in Fsr3Runtime.Cameras) {
                if(!fsr || fsr.Scene!=__instance || !fsr.OwnsSceneTarget) continue;
                __result=__instance.ViewportPointToRay(new Vector3(pos.x/Screen.width,pos.y/Screen.height,0),eye);
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch]
    static class PreserveFsrScreenshotTarget {
        internal struct State {internal Camera Camera; internal RenderTexture Target;}
        static MethodBase TargetMethod() {return AccessTools.Method(AccessTools.TypeByName("ScreenshotTaker"),"TakeScreenshotWithoutUI");}
        static void Prefix(out State __state) {
            __state=default(State);
            // Native snapshots temporarily render the active scene camera into
            // their own texture, then assign null rather than the previous target.
            // Preserve our ownership so a menu snapshot cannot destroy SR history.
            var player=AccessTools.TypeByName("PlayerManager");
            var camera=AccessTools.Property(player,"ActiveCamera").GetValue(null,null) as Camera;
            if(!camera || !camera.targetTexture) return;
            var fsr=camera.GetComponent<FsrSceneCamera>();
            var fsr3=camera.GetComponent<Fsr3SceneCamera>();
            if((fsr && fsr.OwnsSceneTarget) || (fsr3 && fsr3.OwnsSceneTarget)) __state=new State {Camera=camera,Target=camera.targetTexture};
        }
        static void Restore(State state) {if(state.Camera && state.Target) state.Camera.targetTexture=state.Target;}
        static void Postfix(State __state) {Restore(__state);}
        static Exception Finalizer(Exception __exception,State __state) {if(__exception!=null) Restore(__state); return __exception;}
    }
    [HarmonyPatch]
    static class RegisterFsrSceneCamera {
        static MethodBase TargetMethod() {return AccessTools.Method(AccessTools.TypeByName("DV.VFX.CameraGraphicsUpdater"),"Awake");}
        static void Postfix(Component __instance) {FsrRuntime.RegisterGraphics(__instance);}
    }
}

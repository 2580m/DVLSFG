using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityModManagerNet;

namespace DVLSFG {
    internal static class SceneUiRuntime {
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_SetSceneUiEnabled(int enabled);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr DVLSFG_GetSceneUiEvent();
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr DVLSFG_CreateSceneUi(IntPtr scene,IntPtr black,IntPtr white,IntPtr complete);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] internal static extern int DVLSFG_SceneUiStatus(IntPtr job,out ulong frames);
        static SceneUiCapture capture;
        internal static readonly List<SceneUiSceneCamera> Cameras=new List<SceneUiSceneCamera>();
        static bool discovered,requested;
        static Func<bool> ummOpened;
        static bool inspectedUmm;
        internal static bool Active {get {return requested && capture;}}
        internal static void Tick() {
            if(!Main.NativeLoaded) return;
            bool value=Main.modEntry.Active && Main.settings.Enabled;
            if(value!=requested) {requested=value; DVLSFG_SetSceneUiEnabled(value ? 1 : 0);}
            foreach(var camera in Cameras) if(camera) camera.enabled=value && !FsrRuntime.Requested;
            foreach(var camera in TemporalSceneCamera.Cameras) if(camera) camera.enabled=value && Main.settings.FrameGenerationTechnology==2 && Main.runtimeSettings.FrameGenerationTechnology==2;
            if(!value) {if(capture) capture.ResetCapture(); discovered=false; return;}
            if(!capture) {var go=new GameObject("DV Scaling scene/UI separation"); UnityEngine.Object.DontDestroyOnLoad(go); capture=go.AddComponent<SceneUiCapture>();}
            if(!discovered) {
                discovered=true;
                var type=AccessTools.TypeByName("DV.VFX.CameraGraphicsUpdater");
                if(type!=null) foreach(var obj in Resources.FindObjectsOfTypeAll(type)) FsrRuntime.RegisterGraphics(obj as Component);
            }
        }
        internal static void Capture(RenderTexture scene) {
            if(!Active) return;
            if(!inspectedUmm) {
                inspectedUmm=true;
                var type=typeof(UnityModManager).GetNestedType("UI",BindingFlags.Public|BindingFlags.NonPublic);
                var property=type==null ? null : AccessTools.Property(type,"Opened");
                var getter=property==null ? null : property.GetGetMethod(true);
                if(getter!=null) {
                    var owner=getter.IsStatic ? null : AccessTools.Field(type,"mInstance").GetValue(null);
                    if(getter.IsStatic || owner!=null) ummOpened=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),owner,getter);
                    else inspectedUmm=false;
                }
            }
            // UMM uses IMGUI, which is not a Canvas and cannot be re-rendered by
            // the UI camera. Keep ordinary presentation while that window is open.
            if(ummOpened!=null && ummOpened()) return;
            capture.Capture(scene);
        }
    }
    // Appended after the game's image effects. FSR compositors submit their
    // full-resolution output explicitly instead of capturing the low-res camera.
    public sealed class SceneUiSceneCamera : MonoBehaviour {
        Camera scene;
        void Awake() {scene=GetComponent<Camera>(); SceneUiRuntime.Cameras.Add(this);}
        void OnDestroy() {SceneUiRuntime.Cameras.Remove(this);}
        void OnRenderImage(RenderTexture source,RenderTexture destination) {
            if(scene && scene.enabled && !scene.targetTexture && !scene.stereoEnabled) SceneUiRuntime.Capture(source);
            Graphics.Blit(source,destination);
        }
    }
    public sealed class SceneUiCapture : MonoBehaviour {
        RenderTexture scene,black,white,complete,uiMsaa;
        int uiSamples,requestedUiSamples;
        Camera uiCamera;
        IntPtr job,callback;
        CommandBuffer publish;
        int capturedFrame=-1;
        bool failed;
        bool cameraUiWarning;
        internal int CanvasCount;
        static readonly FieldInfo Registry=AccessTools.Field(typeof(GraphicRegistry),"m_Graphics");
        readonly List<CanvasState> canvases=new List<CanvasState>();
        readonly List<LayerState> layers=new List<LayerState>();
        readonly HashSet<Canvas> roots=new HashSet<Canvas>();
        readonly List<Transform> children=new List<Transform>();
        readonly List<RendererState> renderers=new List<RendererState>();
        struct RendererState {internal CanvasRenderer Renderer; internal bool Culled;}
        struct LayerState {internal GameObject Object; internal int Layer;}
        struct CanvasState {
            internal Canvas Canvas; internal Camera Camera; internal float Distance; internal RenderMode Mode;
            internal Vector3 Position,Scale; internal Quaternion Rotation; internal Vector2 Size;
        }
        static RenderTexture Target(string name) {
            var target=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear) {name=name,antiAliasing=1};
            if(!target.Create()) {Destroy(target); throw new InvalidOperationException("Scene/UI capture allocation failed");}
            return target;
        }
        static int UiSampleCount(int requested) {
            var descriptor=new RenderTextureDescriptor(Screen.width,Screen.height,RenderTextureFormat.ARGBHalf,24);
            descriptor.msaaSamples=requested;
            return SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
        }
        internal void Capture(RenderTexture source) {
            if(failed || !source || Screen.width<128 || Screen.height<128) return;
            try {
                if(!scene || scene.width!=Screen.width || scene.height!=Screen.height || requestedUiSamples!=Mathf.Max(4,QualitySettings.antiAliasing)) {Clear(); Create();}
                Graphics.Blit(source,scene); capturedFrame=Time.frameCount;
            } catch(Exception e) {failed=true; Clear(); Main.modEntry.Logger.Error("Scene/UI capture: "+e);}
        }
        void Create() {
            scene=Target("DV Scaling scene without screen UI"); black=Target("DV Scaling UI over black"); white=Target("DV Scaling UI over white");
            complete=Target("DV Scaling complete game frame before external overlays");
            requestedUiSamples=Mathf.Max(4,QualitySettings.antiAliasing); uiSamples=UiSampleCount(requestedUiSamples);
            uiMsaa=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear) {name="DV Scaling antialiased UI",antiAliasing=uiSamples};
            if(!uiMsaa.Create()) throw new InvalidOperationException("UI MSAA allocation failed");
            var go=new GameObject("DV Scaling UI capture camera"); go.transform.SetParent(transform,false);
            uiCamera=go.AddComponent<Camera>(); uiCamera.enabled=false; uiCamera.orthographic=true; uiCamera.orthographicSize=Screen.height/2f;
            uiCamera.nearClipPlane=.1f; uiCamera.farClipPlane=100; uiCamera.cullingMask=1<<31; uiCamera.clearFlags=CameraClearFlags.SolidColor; uiCamera.allowHDR=true; uiCamera.allowMSAA=true;
            callback=SceneUiRuntime.DVLSFG_GetSceneUiEvent();
            publish=new CommandBuffer {name="DV Scaling publish scene/UI"};
            job=SceneUiRuntime.DVLSFG_CreateSceneUi(scene.GetNativeTexturePtr(),black.GetNativeTexturePtr(),white.GetNativeTexturePtr(),complete.GetNativeTexturePtr());
            if(job==IntPtr.Zero || callback==IntPtr.Zero) throw new InvalidOperationException("Scene/UI native bridge allocation failed");
        }
        IEnumerator Start() {
            var end=new WaitForEndOfFrame();
            while(true) {
                yield return end;
                if(!SceneUiRuntime.Active || failed || job==IntPtr.Zero) continue;
                try {
                    if(capturedFrame!=Time.frameCount) {Publish(3); continue;}
                    // Capture before temporary Canvas changes and external present hooks.
                    // This reference separates RTSS/other late overlays from game content.
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(complete);
                    if(!RenderUi()) {Publish(3); continue;}
                    Publish(1);
                    ulong frames;
                    if(SceneUiRuntime.DVLSFG_SceneUiStatus(job,out frames)<0) throw new InvalidOperationException("DXVK scene/UI interop failed; see native status");
                } catch(Exception e) {failed=true; Clear(); Main.modEntry.Logger.Error("Scene/UI capture: "+e);}
            }
        }
        void Publish(int kind) {publish.Clear(); publish.IssuePluginEventAndData(callback,kind,job); Graphics.ExecuteCommandBuffer(publish);}
        bool RenderUi() {
            canvases.Clear(); layers.Clear(); renderers.Clear(); roots.Clear();
            // Enumerate the UI's own live registry; no per-frame global Unity scan.
            var registry=(IDictionary)Registry.GetValue(GraphicRegistry.instance);
            foreach(Canvas registered in registry.Keys) {
                if(!registered || !registered.isActiveAndEnabled) continue;
                var canvas=registered.rootCanvas;
                if(!canvas || canvas.renderMode==RenderMode.WorldSpace || canvas.targetDisplay!=0 || !roots.Add(canvas)) continue;
                // Camera-space UI can already be baked into an OnRenderImage
                // source. Until its camera path can be split, preserve ordinary
                // presentation rather than generating/compositing that UI twice.
                if(canvas.renderMode==RenderMode.ScreenSpaceCamera) {
                    if(!cameraUiWarning) Main.modEntry.Logger.Log("Scene-only generation paused: camera-space UI canvas "+canvas.name);
                    cameraUiWarning=true; return false;
                }
                var rect=(RectTransform)canvas.transform;
                canvases.Add(new CanvasState {Canvas=canvas,Camera=canvas.worldCamera,Distance=canvas.planeDistance,Mode=canvas.renderMode,Position=rect.localPosition,Rotation=rect.localRotation,Scale=rect.localScale,Size=rect.sizeDelta});
            }
            CanvasCount=canvases.Count;
            cameraUiWarning=false;
            try {
                foreach(var saved in canvases) {
                    children.Clear(); saved.Canvas.GetComponentsInChildren<Transform>(true,children);
                    foreach(var child in children) {
                        layers.Add(new LayerState {Object=child.gameObject,Layer=child.gameObject.layer}); child.gameObject.layer=31;
                        var renderer=child.GetComponent<CanvasRenderer>();
                        if(renderer) renderers.Add(new RendererState {Renderer=renderer,Culled=renderer.cull});
                    }
                    saved.Canvas.renderMode=RenderMode.ScreenSpaceCamera; saved.Canvas.worldCamera=uiCamera; saved.Canvas.planeDistance=1;
                }
                Canvas.ForceUpdateCanvases();
                // Resolve after rasterizing UI geometry with MSAA. The native
                // bridge still receives single-sample float textures.
                uiCamera.targetTexture=uiMsaa;
                uiCamera.backgroundColor=Color.black; uiCamera.Render(); Graphics.Blit(uiMsaa,black);
                uiCamera.backgroundColor=Color.white; uiCamera.Render(); Graphics.Blit(uiMsaa,white);
            } finally {
                uiCamera.targetTexture=null;
                foreach(var saved in canvases) if(saved.Canvas) {
                    saved.Canvas.renderMode=saved.Mode; saved.Canvas.worldCamera=saved.Camera; saved.Canvas.planeDistance=saved.Distance;
                    var rect=(RectTransform)saved.Canvas.transform;
                    rect.localPosition=saved.Position; rect.localRotation=saved.Rotation; rect.localScale=saved.Scale; rect.sizeDelta=saved.Size;
                }
                foreach(var saved in layers) if(saved.Object) saved.Object.layer=saved.Layer;
                Canvas.ForceUpdateCanvases();
                // Temporary camera-space clipping must not leak into the native UI.
                foreach(var saved in renderers) if(saved.Renderer) saved.Renderer.cull=saved.Culled;
            }
            return true;
        }
        internal void Clear() {
            capturedFrame=-1;
            if(job!=IntPtr.Zero && callback!=IntPtr.Zero) {using(var disposal=new CommandBuffer()) {disposal.IssuePluginEventAndData(callback,2,job); Graphics.ExecuteCommandBuffer(disposal);} job=IntPtr.Zero;}
            if(publish!=null) {publish.Dispose(); publish=null;}
            if(uiCamera) {Destroy(uiCamera.gameObject); uiCamera=null;}
            if(scene) Destroy(scene); if(black) Destroy(black); if(white) Destroy(white); scene=black=white=null;
            if(uiMsaa) Destroy(uiMsaa); uiMsaa=null;
            if(complete) Destroy(complete); complete=null;
        }
        internal void ResetCapture() {Clear(); failed=false;}
        void OnDestroy() {Clear();}
    }
}

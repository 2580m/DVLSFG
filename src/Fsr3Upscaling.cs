using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using FidelityFX;
using FidelityFX.FSR3;

namespace DVLSFG {
    internal static class Fsr3Runtime {
        internal static AssetBundle Bundle {get {if(!bundle) bundle=AssetBundle.LoadFromFile(Path.Combine(Main.modEntry.Path,"dv-scaling-fsr3")); return bundle;}}
        internal static readonly List<Fsr3SceneCamera> Cameras=new List<Fsr3SceneCamera>();
        internal static Fsr3UpscalerShaders Shaders;
        static AssetBundle bundle;
        static string failure;
        internal static bool Requested {get {return FsrRuntime.Requested && Main.settings.FsrTechnology==2;}}
        static ComputeShader Load(string name) {
            var shader=bundle.LoadAsset<ComputeShader>("ffx_fsr3upscaler_"+name+"_pass");
            if(!shader || !shader.HasKernel("CS")) throw new InvalidDataException("FSR3 shader missing: "+name);
            return shader;
        }
        internal static void Tick() {
            if(!Requested) {
                foreach(var camera in Cameras) if(camera) {camera.enabled=false; camera.Failed=false;}
                return;
            }
            if(Shaders==null && failure==null) try {
                if(!SystemInfo.supportsComputeShaders || !SystemInfo.usesReversedZBuffer) throw new NotSupportedException("FSR3 requires D3D11 compute and reversed depth");
                bundle=Bundle;
                if(!bundle) throw new InvalidDataException("FSR3 shader bundle could not be loaded");
                Shaders=new Fsr3UpscalerShaders {
                    prepareInputsPass=Load("prepare_inputs"),lumaPyramidPass=Load("luma_pyramid"),
                    shadingChangePyramidPass=Load("shading_change_pyramid"),shadingChangePass=Load("shading_change"),
                    prepareReactivityPass=Load("prepare_reactivity"),lumaInstabilityPass=Load("luma_instability"),
                    accumulatePass=Load("accumulate"),sharpenPass=Load("rcas"),autoGenReactivePass=Load("autogen_reactive"),
                    tcrAutoGenPass=Load("tcr_autogen"),debugViewPass=Load("debug_view")
                };
            } catch(Exception e) {failure=e.Message; Main.modEntry.Logger.Error("FSR3: "+e);}
            if(Shaders==null) return;
            foreach(var camera in Cameras) if(camera && !camera.Failed) camera.enabled=true;
            // Disable native AA on owned scene cameras/targets only. The global
            // quality setting is also used by native screen UI and other cameras.
        }
        internal static string Status {get {
            if(failure!=null) return L10n.Get("dv_scaling/unavailable")+failure;
            foreach(var camera in Cameras) if(camera && (camera.isActiveAndEnabled || camera.Failed) && camera.Scene && camera.Scene.enabled) return camera.Status;
            return L10n.Get("dv_scaling/waiting_for_scene_camera");
        }}
    }

    [DefaultExecutionOrder(500)]
    public sealed class Fsr3SceneCamera : MonoBehaviour {
        internal Camera Scene;
        internal bool Failed;
        internal RenderTexture SceneTarget,OutputTarget;
        Fsr3UpscalerContext context;
        Fsr3UpscalerShaders shaders;
        CommandBuffer commands;
        Camera compositor;
        PostProcessLayer post;
        PostProcessLayer.Antialiasing previousAa;
        DepthTextureMode previousDepth;
        bool previousMsaa,reset=true,owned,stateCaptured;
        int mode,width,height;
        Vector2 jitter;
        internal Vector2 Jitter {get {return jitter;}}
        Vector3 lastPosition;
        Quaternion lastRotation;
        float lastFov;
        RenderingPath lastRenderingPath;
        ulong frames;
        string error;
        internal bool OwnsSceneTarget {get {return owned && Scene && Scene.targetTexture==SceneTarget;}}
        internal string Status {get {return error!=null ? L10n.Get("dv_scaling/unavailable")+error : frames>0 ? SceneTarget.width+"×"+SceneTarget.height+" → "+width+"×"+height : L10n.Get("dv_scaling/preparing");}}
        void Awake() {Scene=GetComponent<Camera>(); Fsr3Runtime.Cameras.Add(this);}
        void LateUpdate() {
            if(!Fsr3Runtime.Requested || Fsr3Runtime.Shaders==null || !Scene || !Scene.enabled || Scene.cullingMask==0 || Scene.stereoEnabled) {Release(); return;}
            try {
                if(Scene.targetDisplay!=0 || Scene.rect!=new Rect(0,0,1,1)) throw new NotSupportedException("FSR3 requires a full-screen primary scene camera");
                if((!owned && Scene.targetTexture) || (owned && Scene.targetTexture!=SceneTarget)) {Release(); return;}
                if(context==null || width!=Screen.width || height!=Screen.height || mode!=Main.settings.FsrMode) {Release(); Create();}
                if(post && post.antialiasingMode!=PostProcessLayer.Antialiasing.None) {previousAa=post.antialiasingMode; post.antialiasingMode=PostProcessLayer.Antialiasing.None;}
                Scene.allowMSAA=false;
                compositor.depth=Scene.depth+.001f;
            } catch(Exception e) {error=e.Message; Failed=true; Release(); Main.modEntry.Logger.Error("FSR3 camera: "+e); enabled=false;}
        }
        static RenderTexture Target(int w,int h,int depth,bool randomWrite,string name) {
            var target=new RenderTexture(w,h,depth,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear) {name=name,enableRandomWrite=randomWrite,antiAliasing=1};
            if(!target.Create()) {Destroy(target); throw new InvalidOperationException("FSR3 target allocation failed");}
            return target;
        }
        void Create() {
            width=Screen.width; height=Screen.height; mode=Main.settings.FsrMode;
            int rw=Mathf.Max(8,Mathf.RoundToInt(width*FsrRuntime.Ratios[mode])),rh=Mathf.Max(8,Mathf.RoundToInt(height*FsrRuntime.Ratios[mode]));
            previousDepth=Scene.depthTextureMode; previousMsaa=Scene.allowMSAA;
            stateCaptured=true;
            post=Scene.GetComponent<PostProcessLayer>(); if(post) {previousAa=post.antialiasingMode; post.antialiasingMode=PostProcessLayer.Antialiasing.None;}
            SceneTarget=Target(rw,rh,24,false,"DVLSFG FSR3 scene"); OutputTarget=Target(width,height,0,true,"DVLSFG FSR3 output");
            shaders=Fsr3Runtime.Shaders.DeepCopy();
            context=Fsr3Upscaler.CreateContext(new Vector2Int(width,height),new Vector2Int(rw,rh),shaders,Fsr3Upscaler.InitializationFlags.EnableAutoExposure);
            commands=new CommandBuffer {name="DV Scaling FSR 3.1 temporal reconstruction"};
            Scene.depthTextureMode=previousDepth|DepthTextureMode.Depth|DepthTextureMode.MotionVectors;
            Scene.targetTexture=SceneTarget; Scene.allowMSAA=false; owned=true; reset=true; frames=0; error=null;
            var go=new GameObject("DVLSFG FSR3 compositor"); go.transform.SetParent(transform,false);
            compositor=go.AddComponent<Camera>(); compositor.cullingMask=0; compositor.clearFlags=CameraClearFlags.Nothing; compositor.allowMSAA=false; compositor.allowHDR=false;
            go.AddComponent<Fsr3OutputCamera>().Owner=this;
            Main.modEntry.Logger.Log("FSR3 camera "+Scene.name+": "+rw+"x"+rh+" -> "+width+"x"+height);
        }
        void OnPreCull() {
            if(!OwnsSceneTarget || context==null) return;
            Scene.ResetProjectionMatrix();
            reset|=(Scene.transform.position-lastPosition).sqrMagnitude>25 || Quaternion.Angle(Scene.transform.rotation,lastRotation)>45 || TemporalHistory.ProjectionDiscontinuity(lastFov,Scene.fieldOfView,Time.unscaledDeltaTime) || Time.unscaledDeltaTime>.25f || Scene.actualRenderingPath!=lastRenderingPath;
            lastRenderingPath=Scene.actualRenderingPath;
            lastPosition=Scene.transform.position; lastRotation=Scene.transform.rotation; lastFov=Scene.fieldOfView;
            int phases=Fsr3Upscaler.GetJitterPhaseCount(SceneTarget.width,width);
            float x,y; Fsr3Upscaler.GetJitterOffset(out x,out y,Time.frameCount,phases); jitter=new Vector2(x,y);
            Scene.nonJitteredProjectionMatrix=Scene.projectionMatrix;
            Scene.projectionMatrix=Matrix4x4.Translate(new Vector3(2*x/SceneTarget.width,2*y/SceneTarget.height,0))*Scene.nonJitteredProjectionMatrix;
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination) {
            if(context==null || !OwnsSceneTarget) {Graphics.Blit(source,destination); return;}
            try {
                commands.Clear();
                var depth=Scene.actualRenderingPath==RenderingPath.Forward ? BuiltinRenderTextureType.Depth : BuiltinRenderTextureType.CameraTarget;
                var dispatch=new Fsr3Upscaler.DispatchDescription {
                    Color=new ResourceView(source),Depth=new ResourceView(depth,RenderTextureSubElement.Depth),MotionVectors=new ResourceView(BuiltinRenderTextureType.MotionVectors),Output=new ResourceView(OutputTarget),
                    Exposure=ResourceView.Unassigned,Reactive=ResourceView.Unassigned,TransparencyAndComposition=ResourceView.Unassigned,
                    RenderSize=new Vector2Int(SceneTarget.width,SceneTarget.height),UpscaleSize=new Vector2Int(width,height),
                    JitterOffset=jitter,MotionVectorScale=new Vector2(-SceneTarget.width,-SceneTarget.height),
                    PreExposure=1,EnableSharpening=Main.settings.FsrSharpness>0,Sharpness=Main.settings.FsrSharpness,
                    FrameTimeDelta=Time.unscaledDeltaTime,CameraNear=Scene.farClipPlane,CameraFar=Scene.nearClipPlane,
                    CameraFovAngleVertical=Scene.fieldOfView*Mathf.Deg2Rad,ViewSpaceToMetersFactor=1,Reset=reset
                };
                context.Dispatch(dispatch,commands); Graphics.ExecuteCommandBuffer(commands); frames++; reset=false;
                Graphics.Blit(source,destination);
            } catch(Exception e) {error=e.Message; Failed=true; Main.modEntry.Logger.Error("FSR3 dispatch: "+e); Graphics.Blit(source,destination); enabled=false;}
            finally {Scene.ResetProjectionMatrix();}
        }
        void OnDisable() {Release();}
        void OnDestroy() {Release(); Fsr3Runtime.Cameras.Remove(this);}
        void Release() {
            if(compositor) {compositor.enabled=false; Destroy(compositor.gameObject); compositor=null;}
            if(stateCaptured && Scene) {
                if(owned && Scene.targetTexture==SceneTarget) Scene.targetTexture=null;
                Scene.ResetProjectionMatrix(); Scene.depthTextureMode=previousDepth; Scene.allowMSAA=previousMsaa;
                if(post && post.antialiasingMode==PostProcessLayer.Antialiasing.None) post.antialiasingMode=previousAa;
            }
            owned=false;
            stateCaptured=false;
            if(context!=null) {context.Destroy(); context=null;}
            if(shaders!=null) {shaders.Dispose(); shaders=null;}
            if(commands!=null) {commands.Release(); commands=null;}
            if(SceneTarget) {Destroy(SceneTarget); SceneTarget=null;} if(OutputTarget) {Destroy(OutputTarget); OutputTarget=null;}
        }
    }
    public sealed class Fsr3OutputCamera : MonoBehaviour {
        internal Fsr3SceneCamera Owner;
        void OnRenderImage(RenderTexture source,RenderTexture destination) {var image=Owner && Owner.OutputTarget ? Owner.OutputTarget : source; SceneUiRuntime.Capture(image); Graphics.Blit(image,destination);}
    }
}

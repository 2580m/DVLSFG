using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
namespace DVLSFG {
    public sealed class TemporalSceneCamera : MonoBehaviour {
        internal static readonly List<TemporalSceneCamera> Cameras=new List<TemporalSceneCamera>();
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr DVLSFG_CreateTemporal(IntPtr depth,IntPtr motion);
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_SetTemporalMetadata(IntPtr job,float x,float y,float dt,float nearPlane,float farPlane,float fov,int reset);
        static AssetBundle bundle;
        static Shader shader;
        Camera scene;
        Material material;
        RenderTexture depth,motion;
        IntPtr job;
        CommandBuffer commands;
        DepthTextureMode previousDepth;
        bool owned,failed;
        Vector3 lastPosition;
        Quaternion lastRotation;
        float lastFov;
        RenderingPath lastPath;
        int lastFrame=-1;
        void Awake() {scene=GetComponent<Camera>(); Cameras.Add(this);}
        void OnPreCull() {
            bool active=Main.NativeLoaded && Main.modEntry.Active && Main.settings.Enabled && Main.settings.FrameGenerationTechnology==2;
            if(!active) {Release(); return;}
            if(!owned) {previousDepth=scene.depthTextureMode; owned=true;}
            scene.depthTextureMode|=DepthTextureMode.Depth|DepthTextureMode.MotionVectors;
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination) {
            try {
                if(owned && !failed && scene.enabled && !scene.stereoEnabled && scene.cullingMask!=0) Capture(source);
            } catch(Exception e) {failed=true; Release(); Main.modEntry.Logger.Error("FSR frame generation input capture: "+e);}
            Graphics.Blit(source,destination);
        }
        void Capture(RenderTexture source) {
            if(!shader) {
                // The upscaler and frame generator share the same asset bundle.
                bundle=Fsr3Runtime.Bundle;
                shader=bundle ? bundle.LoadAsset<Shader>("TemporalCapture") : null;
                if(!shader || !shader.isSupported) throw new InvalidDataException("Temporal capture shader unavailable");
            }
            if(!material) material=new Material(shader);
            if(!depth || depth.width!=source.width || depth.height!=source.height) {
                ClearTargets();
                depth=Target(source.width,source.height,RenderTextureFormat.RFloat,"FSR FG depth");
                motion=Target(source.width,source.height,RenderTextureFormat.RGFloat,"FSR FG motion");
                job=DVLSFG_CreateTemporal(depth.GetNativeTexturePtr(),motion.GetNativeTexturePtr());
                commands=new CommandBuffer {name="FSR 3.1 frame generation inputs"};
                lastFrame=-1;
            }
            var fsr3=GetComponent<Fsr3SceneCamera>();
            var jitter=fsr3 && fsr3.OwnsSceneTarget ? fsr3.Jitter : Vector2.zero;
            bool reset=lastFrame!=Time.frameCount-1 || (scene.transform.position-lastPosition).sqrMagnitude>25 || Quaternion.Angle(scene.transform.rotation,lastRotation)>45 || Mathf.Abs(scene.fieldOfView-lastFov)>.1f || lastPath!=scene.actualRenderingPath || Time.unscaledDeltaTime>.25f;
            DVLSFG_SetTemporalMetadata(job,jitter.x,-jitter.y,Time.unscaledDeltaTime*1000,scene.nearClipPlane,scene.farClipPlane,scene.fieldOfView*Mathf.Deg2Rad,reset ? 1 : 0);
            commands.Clear(); commands.Blit(null,depth,material,0); commands.Blit(null,motion,material,1);
            commands.IssuePluginEventAndData(SceneUiRuntime.DVLSFG_GetSceneUiEvent(),4,job); Graphics.ExecuteCommandBuffer(commands);
            lastFrame=Time.frameCount; lastPosition=scene.transform.position; lastRotation=scene.transform.rotation; lastFov=scene.fieldOfView; lastPath=scene.actualRenderingPath;
        }
        static RenderTexture Target(int width,int height,RenderTextureFormat format,string name) {
            var result=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear) {name=name,antiAliasing=1};
            if(!result.Create()) throw new InvalidOperationException("Temporal capture allocation failed"); return result;
        }
        void ClearTargets() {
            if(job!=IntPtr.Zero) {using(var dispose=new CommandBuffer()) {dispose.IssuePluginEventAndData(SceneUiRuntime.DVLSFG_GetSceneUiEvent(),5,job); Graphics.ExecuteCommandBuffer(dispose);} job=IntPtr.Zero;}
            if(commands!=null) {commands.Dispose(); commands=null;}
            if(depth) Destroy(depth); if(motion) Destroy(motion); depth=motion=null;
        }
        void Release() {ClearTargets(); if(owned && scene) scene.depthTextureMode=previousDepth; owned=false; lastFrame=-1;}
        void OnDisable() {Release();}
        void OnDestroy() {Cameras.Remove(this); Release(); if(material) Destroy(material);}
    }
}

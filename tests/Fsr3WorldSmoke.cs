using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DV.UI;
using DVLSFG;
namespace DVLSFGTests {
    public static class Fsr3WorldSmoke {
        public static bool Load(UnityModManager.ModEntry entry) {
            var go=new GameObject("FSR3 world validation"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Fsr3WorldSmokeRunner>().Entry=entry; return true;
        }
    }
    public sealed class Fsr3WorldSmokeRunner : MonoBehaviour {
        public UnityModManager.ModEntry Entry;
        Settings settings;
        int oldMode,oldTechnology,oldWidth,oldHeight;
        FullScreenMode oldScreenMode;
        void Log(string text) {Entry.Logger.Log(text); File.AppendAllText(Path.Combine(Entry.Path,"world-fsr3.log"),text+"\n");}
        void Fail(Exception error) {Log("FAIL "+error); Restore(); File.WriteAllText(Path.Combine(Entry.Path,"failed.txt"),error.ToString());}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(15);
            settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            oldMode=settings.FsrMode; oldTechnology=settings.FsrTechnology; oldWidth=Screen.width; oldHeight=Screen.height; oldScreenMode=Screen.fullScreenMode;
            MainMenuController activeMenu=null;
            float menuDeadline=Time.realtimeSinceStartup+90;
            while(!activeMenu && Time.realtimeSinceStartup<menuDeadline) {
                foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(menu.gameObject.activeInHierarchy) {activeMenu=menu; break;}
                if(!activeMenu) yield return new WaitForSecondsRealtime(1);
            }
            if(!activeMenu) {Fail(new Exception("Main menu not loaded")); yield break;}
            try {
                var uiType=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(uiType,"mInstance").GetValue(null),new object[]{false});
                ((DV.UIFramework.ButtonDV)AccessTools.Field(typeof(MainMenuController),"continueButton").GetValue(activeMenu)).Click();
            } catch(Exception error) {Fail(error); yield break;}
            yield return new WaitForSecondsRealtime(2);
            try {
                var popup=AccessTools.TypeByName("DV.UI.LauncherController");
                foreach(var obj in Resources.FindObjectsOfTypeAll(popup)) {var component=obj as Component; if(component && component.gameObject.activeInHierarchy) {((UnityEngine.UI.Button)AccessTools.Field(popup,"runButton").GetValue(component)).onClick.Invoke(); break;}}
                settings.FsrMode=2; settings.FsrTechnology=2;
            } catch(Exception error) {Fail(error); yield break;}
            float deadline=Time.realtimeSinceStartup+180;
            while(Time.realtimeSinceStartup<deadline && !Find()) yield return new WaitForSecondsRealtime(1);
            if(!Find()) {Fail(new Exception("FSR3 world camera did not become ready")); yield break;}
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
                if(fsr.isActiveAndEnabled && camera && camera.enabled && (ulong)AccessTools.Field(typeof(Fsr3SceneCamera),"frames").GetValue(fsr)>30) return fsr;
            }
            return null;
        }
        void Check(string stage) {
            var fsr=Find(); if(!fsr) throw new Exception("No FSR3 world output: "+stage);
            var camera=fsr.GetComponent<Camera>();
            var output=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"OutputTarget").GetValue(fsr);
            if(!output || output.width!=Screen.width || output.height!=Screen.height || QualitySettings.antiAliasing!=0 || camera.allowMSAA) throw new Exception("Invalid FSR3 output or native AA still active");
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
            Log("PASS "+stage+" camera="+camera.name+" target="+camera.targetTexture.width+"x"+camera.targetTexture.height+" output="+output.width+"x"+output.height+" rendering="+camera.actualRenderingPath+" post-AA="+(post==null ? "no PPV2 layer" : "None")+" native MSAA disabled, depth/motion enabled, finite pixels and picking rays");
        }
        void Restore() {if(settings!=null) {settings.FsrMode=oldMode; settings.FsrTechnology=oldTechnology;} Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);}
    }
}

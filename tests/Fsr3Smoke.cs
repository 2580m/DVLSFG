using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    public static class Fsr3Smoke {
        public static bool Load(UnityModManager.ModEntry entry) {
            var go=new GameObject("FSR3 GPU validation"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Fsr3SmokeRunner>().Entry=entry; return true;
        }
    }
    public sealed class Fsr3SmokeRunner : MonoBehaviour {
        public UnityModManager.ModEntry Entry;
        Settings settings;
        Camera camera;
        Fsr3SceneCamera fsr;
        int oldMode,oldTech,oldAa;
        float oldSharp;
        void Log(string text) {Entry.Logger.Log(text); File.AppendAllText(Path.Combine(Entry.Path,"fsr3-smoke.log"),text+"\n");}
        void Fail(Exception error) {Log("FAIL "+error); Restore(); File.WriteAllText(Path.Combine(Entry.Path,"failed.txt"),error.ToString());}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(15);
            settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
            oldMode=settings.FsrMode; oldTech=settings.FsrTechnology; oldSharp=settings.FsrSharpness;
            // Capture the native baseline after releasing any FSR3 active at startup.
            settings.FsrMode=0;
            yield return new WaitForSecondsRealtime(2);
            oldAa=QualitySettings.antiAliasing;
            var go=new GameObject("FSR3 synthetic scene"); go.transform.SetParent(transform,false);
            camera=go.AddComponent<Camera>(); camera.cullingMask=1<<31; camera.depth=1000; camera.allowHDR=false;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.5f,.25f,.75f,1);
            fsr=go.AddComponent<Fsr3SceneCamera>();
            settings.FsrTechnology=2; settings.FsrSharpness=0;
            for(int mode=1;mode<=5;mode++) {
                settings.FsrMode=mode;
                yield return new WaitForSecondsRealtime(4);
                try {Check(mode);} catch(Exception error) {Fail(error); yield break;}
            }
            settings.FsrSharpness=1;
            yield return new WaitForSecondsRealtime(3);
            try {Check(5); Log("PASS sharpening enabled");} catch(Exception error) {Fail(error); yield break;}
            settings.FsrMode=0;
            yield return new WaitForSecondsRealtime(2);
            try {if(camera.targetTexture || QualitySettings.antiAliasing!=oldAa) throw new Exception("Off failed to restore target or antialiasing: target="+camera.targetTexture+", MSAA="+QualitySettings.antiAliasing+", expected="+oldAa); Log("PASS Off restores camera and MSAA");} catch(Exception error) {Fail(error); yield break;}
            Restore();
            File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"FSR3 GPU output, five modes, sharpening and Off restoration passed");
        }
        static Color32 Pixel(RenderTexture target) {
            var previous=RenderTexture.active; RenderTexture.active=target;
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            try {texture.ReadPixels(new Rect(target.width/2,target.height/2,1,1),0,0); texture.Apply(); return texture.GetPixels32()[0];}
            finally {RenderTexture.active=previous; Destroy(texture);}
        }
        void Check(int mode) {
            var input=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"SceneTarget").GetValue(fsr);
            var output=(RenderTexture)AccessTools.Field(typeof(Fsr3SceneCamera),"OutputTarget").GetValue(fsr);
            var frames=(ulong)AccessTools.Field(typeof(Fsr3SceneCamera),"frames").GetValue(fsr);
            if(!fsr.enabled || !input || !output || frames<10) throw new Exception("FSR3 reconstruction inactive");
            if(output.width!=Screen.width || output.height!=Screen.height || input.width>=output.width || QualitySettings.antiAliasing!=0) throw new Exception("FSR3 dimensions or AA override wrong");
            var before=Pixel(input); var after=Pixel(output);
            if(before.r<30 || before.b<30 || Math.Abs(before.r-after.r)>8 || Math.Abs(before.g-after.g)>8 || Math.Abs(before.b-after.b)>8) throw new Exception("FSR3 GPU output differs: "+before+" -> "+after);
            Log("PASS FSR3 mode="+mode+" "+input.width+"x"+input.height+" -> "+output.width+"x"+output.height+" frames="+frames+" pixel="+before+" -> "+after);
        }
        void Restore() {if(settings!=null) {settings.FsrMode=oldMode; settings.FsrTechnology=oldTech; settings.FsrSharpness=oldSharp;} if(camera) camera.enabled=false;}
    }
}

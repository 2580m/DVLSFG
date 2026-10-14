using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    public static class SceneUiSmoke {
        public static bool Load(UnityModManager.ModEntry entry) {var go=new GameObject("Scene/UI validation"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<SceneUiRunner>().Entry=entry; return true;}
    }
    public sealed class SceneUiRunner : MonoBehaviour {
        public UnityModManager.ModEntry Entry;
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_RequestCapture();
        [DllImport("DVLSFG.Native",CallingConvention=CallingConvention.Cdecl)] static extern int DVLSFG_GetStatus(IntPtr text,uint capacity,out ulong real,out ulong generated);
        Settings settings; int oldMode,oldTechnology; Camera camera; Canvas canvas; Image opaque,translucent;
        int oldWidth,oldHeight; FullScreenMode oldScreenMode;
        int pausedCaptureFrame;
        int oldMultiplier; bool oldPerformance;
        int originalUiAa;
        readonly System.Collections.Generic.List<Canvas> hidden=new System.Collections.Generic.List<Canvas>();
        void Log(string text) {Entry.Logger.Log(text); File.AppendAllText(Path.Combine(Entry.Path,"scene-ui.log"),text+"\n");}
        void Fail(Exception e) {Log("FAIL "+e); Restore(); File.WriteAllText(Path.Combine(Entry.Path,"failed.txt"),e.ToString());}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(20);
            settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null); oldMode=settings.FsrMode; oldTechnology=settings.FsrTechnology;
            oldWidth=Screen.width; oldHeight=Screen.height; oldScreenMode=Screen.fullScreenMode;
            oldMultiplier=settings.Multiplier; oldPerformance=settings.Performance;
            originalUiAa=QualitySettings.antiAliasing;
            try {
                var uiType=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                foreach(var member in uiType.GetMembers(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static)) if(member.Name.ToLowerInvariant().Contains("open")) Log("UMM member "+member.MemberType+" "+member.Name);
                AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(uiType,"mInstance").GetValue(null),new object[]{false});
                foreach(var existingCanvas in UnityEngine.Object.FindObjectsOfType<Canvas>()) if(existingCanvas.isRootCanvas && existingCanvas.enabled && existingCanvas.renderMode!=RenderMode.WorldSpace) {hidden.Add(existingCanvas); existingCanvas.enabled=false;}
                var scene=new GameObject("UI separation synthetic scene"); scene.transform.SetParent(transform,false);
                camera=scene.AddComponent<Camera>(); camera.depth=1000; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.2f,.3f,.6f,1); camera.cullingMask=1<<30;
                scene.AddComponent<FsrSceneCamera>(); scene.AddComponent<Fsr3SceneCamera>().enabled=false; scene.AddComponent<SceneUiSceneCamera>();
                var root=new GameObject("UI capture test canvas",typeof(RectTransform),typeof(Canvas)); root.transform.SetParent(transform,false);
                canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=32760;
                opaque=Panel("opaque",new Vector2(-200,0),new Color(.1f,.9f,.2f,1));
                translucent=Panel("translucent",new Vector2(200,0),new Color(.9f,.1f,.2f,.5f));
                opaque.rectTransform.anchoredPosition+=new Vector2(.35f,7.25f);
                opaque.rectTransform.localRotation=Quaternion.Euler(0,0,13);
                translucent.rectTransform.anchoredPosition+=new Vector2(.4f,-7.2f);
                translucent.rectTransform.localRotation=Quaternion.Euler(0,0,-7);
            } catch(Exception e) {Fail(e); yield break;}
            for(int technology=0;technology<=2;++technology) {
                settings.FsrTechnology=technology; settings.FsrMode=technology==0 ? 0 : 3;
                settings.Multiplier=technology+2; settings.Performance=technology==2;
                AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
                yield return new WaitForSecondsRealtime(5);
                ulong beforeReal,beforeGenerated; DVLSFG_GetStatus(IntPtr.Zero,0,out beforeReal,out beforeGenerated);
                for(int change=0;change<2;++change) {
                    opaque.color=change==0 ? Color.green : Color.magenta;
                    translucent.color=change==0 ? new Color(1,0,0,.5f) : new Color(0,0,1,.5f);
                    yield return null;
                    string captureDirectory=Path.Combine(Entry.Path,"..","dv-scaling","captures");
                    var existing=Directory.Exists(captureDirectory) ? Directory.GetFiles(captureDirectory,"*-generated.bmp") : new string[0];
                    DVLSFG_RequestCapture();
                    yield return new WaitForSecondsRealtime(2);
                    try {
                        var generated=Directory.GetFiles(captureDirectory,"*-generated.bmp").Except(existing).LastOrDefault();
                        if(generated==null) throw new Exception("No generated capture; bridge/presentation inactive");
                        var input=generated.Replace("-generated.bmp","-input.bmp");
                        var capture=UnityEngine.Object.FindObjectOfType<SceneUiCapture>();
                        CheckUiAa(capture,technology);
                        foreach(var field in new[]{"scene","black","white"}) {
                            var surface=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),field).GetValue(capture);
                            Log(field+" left="+Pixel(surface,Screen.width/2-200,Screen.height/2)+" right="+Pixel(surface,Screen.width/2+200,Screen.height/2));
                        }
                        foreach(int offset in new[]{-200,200}) Compare(input,generated,Screen.width/2+offset,Screen.height/2);
                        CheckPresentedEdges(input,generated);
                        var target=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"scene").GetValue(capture);
                        var pixel=Pixel(target,Screen.width/2-200,Screen.height/2);
                        if(pixel.g>.7f || pixel.r>.7f) throw new Exception("UI contaminated scene input: "+pixel);
                        if(canvas.renderMode!=RenderMode.ScreenSpaceOverlay || canvas.gameObject.layer!=0 || opaque.gameObject.layer!=0) throw new Exception("UI capture did not restore canvas/layers");
                        Log("PASS technology="+technology+" color="+change+" opaque/translucent generated UI matches original; scene excludes UI; canvas state restored");
                    } catch(Exception e) {Fail(e); yield break;}
                }
                ulong afterReal,afterGenerated; DVLSFG_GetStatus(IntPtr.Zero,0,out afterReal,out afterGenerated);
                long expected=(long)(afterReal-beforeReal)*(settings.Multiplier-1);
                if(Math.Abs((long)(afterGenerated-beforeGenerated)-expected)>settings.Multiplier) {Fail(new Exception("Generation counter ratio mismatch")); yield break;}
                Log("PASS multiplier="+settings.Multiplier+" performance="+settings.Performance+" real="+(afterReal-beforeReal)+" generated="+(afterGenerated-beforeGenerated));
            }
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(5);
            try {
                var capture=UnityEngine.Object.FindObjectOfType<SceneUiCapture>();
                var target=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"scene").GetValue(capture);
                if(!target || target.width!=Screen.width || target.height!=Screen.height || target.width!=1280) throw new Exception("Scene/UI resize failed");
                Log("PASS resolution change: capture targets rebuilt at 1280x720");
                var type=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                pausedCaptureFrame=(int)AccessTools.Field(typeof(SceneUiCapture),"capturedFrame").GetValue(capture);
                AccessTools.Method(type,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(type,"mInstance").GetValue(null),new object[]{true});
            } catch(Exception e) {Fail(e); yield break;}
            yield return new WaitForSecondsRealtime(2);
            try {
                var capture=UnityEngine.Object.FindObjectOfType<SceneUiCapture>();
                if((int)AccessTools.Field(typeof(SceneUiCapture),"capturedFrame").GetValue(capture)!=pausedCaptureFrame) throw new Exception("UMM IMGUI did not pause scene capture");
                var type=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                AccessTools.Method(type,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(type,"mInstance").GetValue(null),new object[]{false});
                Log("PASS UMM IMGUI pauses capture instead of interpolating/flickering its window");
            } catch(Exception e) {Fail(e); yield break;}
            canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            settings.Multiplier=2; settings.Performance=false;
            AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
            yield return new WaitForSecondsRealtime(1);
            ulong pausedReal,pausedGenerated; DVLSFG_GetStatus(IntPtr.Zero,0,out pausedReal,out pausedGenerated);
            yield return new WaitForSecondsRealtime(1);
            ulong currentReal,currentGenerated; DVLSFG_GetStatus(IntPtr.Zero,0,out currentReal,out currentGenerated);
            if(pausedGenerated!=currentGenerated) {Fail(new Exception("Camera-space UI was interpolated instead of bypassed")); yield break;}
            canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null;
            Log("PASS camera-space UI bypass prevents baked-in UI interpolation");
            Restore(); File.WriteAllText(Path.Combine(Entry.Path,"scene-passed.txt"),"Scene-only LSFG with opaque/translucent UI, FSR off/1/3, resize and UMM passed");
            if(!File.Exists(Path.Combine(Entry.Path,"skip-world.txt"))) gameObject.AddComponent<Fsr3WorldSmokeRunner>().Entry=Entry;
        }
        Image Panel(string name,Vector2 position,Color color) {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image)); go.transform.SetParent(canvas.transform,false);
            var rect=go.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.sizeDelta=new Vector2(180,180); rect.anchoredPosition=position;
            var image=go.GetComponent<Image>(); image.color=color; return image;
        }
        void CheckUiAa(SceneUiCapture capture,int technology) {
            if(QualitySettings.antiAliasing!=originalUiAa) throw new Exception("FSR changed global/UI AA");
            var multisample=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"uiMsaa").GetValue(capture);
            if(!multisample || multisample.antiAliasing<4 || multisample.depth<24) throw new Exception("UI MSAA/stencil target inactive");
            var target=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"black").GetValue(capture);
            var previous=RenderTexture.active; RenderTexture.active=target;
            var pixels=new Texture2D(260,260,TextureFormat.RGBAFloat,false,true);
            int partial=0;
            try {
                pixels.ReadPixels(new Rect(Screen.width/2-330,Screen.height/2-130,260,260),0,0); pixels.Apply();
                foreach(var color in pixels.GetPixels()) {float coverage=Mathf.Max(color.r,Mathf.Max(color.g,color.b)); if(coverage>.01f && coverage<.99f) partial++;}
            } finally {RenderTexture.active=previous; Destroy(pixels);}
            if(partial<100) throw new Exception("Rotated subpixel UI edge remains aliased: partial pixels="+partial);
            if(technology==2 && (camera.allowMSAA || camera.targetTexture.antiAliasing!=1)) throw new Exception("FSR3 scene AA not disabled");
            Log("PASS UI AA: global="+originalUiAa+" UI="+multisample.antiAliasing+"x resolved partial edge pixels="+partial+" technology="+technology);
        }
        void CheckPresentedEdges(string input,string generated) {
            var original=File.ReadAllBytes(input); var interpolated=File.ReadAllBytes(generated);
            int width=BitConverter.ToInt32(original,18),height=BitConverter.ToInt32(original,22),start=BitConverter.ToInt32(original,10);
            Func<int,int,int> offset=(x,y)=>start+((height<0 ? -height-1-y : y)*width+x)*4;
            int center=offset(Screen.width/2-200,Screen.height/2),background=offset(Screen.width/2-200,Screen.height/2-130);
            int originalPartial=0,generatedPartial=0,maxError=0;
            for(int y=Screen.height/2-130;y<Screen.height/2+130;y++) for(int x=Screen.width/2-330;x<Screen.width/2-70;x++) {
                int at=offset(x,y),aBg=0,aFg=0,bBg=0,bFg=0;
                for(int c=0;c<3;c++) {
                    aBg+=Math.Abs(original[at+c]-original[background+c]); aFg+=Math.Abs(original[at+c]-original[center+c]);
                    bBg+=Math.Abs(interpolated[at+c]-interpolated[background+c]); bFg+=Math.Abs(interpolated[at+c]-interpolated[center+c]);
                    maxError=Math.Max(maxError,Math.Abs(original[at+c]-interpolated[at+c]));
                }
                if(aBg>10 && aFg>10) originalPartial++; if(bBg>10 && bFg>10) generatedPartial++;
            }
            if(originalPartial<100 || generatedPartial<100 || maxError>5) throw new Exception("Presented UI AA mismatch: real="+originalPartial+" generated="+generatedPartial+" max error="+maxError);
            Log("PASS presented UI AA: real partial="+originalPartial+" generated partial="+generatedPartial+" max RGB error="+maxError);
        }
        static Color Pixel(RenderTexture target,int x,int y) {
            var prior=RenderTexture.active; RenderTexture.active=target; var texture=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try {texture.ReadPixels(new Rect(x,y,1,1),0,0); texture.Apply(); return texture.GetPixel(0,0);} finally {RenderTexture.active=prior; UnityEngine.Object.Destroy(texture);}
        }
        void Compare(string input,string generated,int x,int y) {
            var a=File.ReadAllBytes(input); var b=File.ReadAllBytes(generated);
            int width=BitConverter.ToInt32(a,18),height=BitConverter.ToInt32(a,22),row=height<0 ? -height-1-y : y;
            int offset=BitConverter.ToInt32(a,10)+(row*width+x)*4;
            Log("PIXEL "+x+","+y+" input="+a[offset]+","+a[offset+1]+","+a[offset+2]+" generated="+b[offset]+","+b[offset+1]+","+b[offset+2]);
            for(int c=0;c<3;++c) if(Math.Abs(a[offset+c]-b[offset+c])>5) throw new Exception("Generated UI pixel differs at "+x+","+y);
        }
        void Restore() {if(settings!=null) {settings.FsrMode=oldMode; settings.FsrTechnology=oldTechnology; settings.Multiplier=oldMultiplier; settings.Performance=oldPerformance; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);} if(camera) camera.enabled=false; if(canvas) canvas.enabled=false; foreach(var original in hidden) if(original) original.enabled=true; hidden.Clear(); Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);}
    }
}

using System;
using System.IO;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
 public static class FsrSmoke {
  public static bool Load(UnityModManager.ModEntry mod) {
   var go=new GameObject("FSR smoke test"); UnityEngine.Object.DontDestroyOnLoad(go);
   go.AddComponent<FsrSmokeRunner>().Entry=mod; return true;
  }
 }
 public sealed class FsrSmokeRunner : MonoBehaviour {
  public UnityModManager.ModEntry Entry;
  Settings settings;
  int oldMode,oldWidth,oldHeight;
  float oldSharpness;
  FullScreenMode oldScreenMode;
  Camera camera;
  Canvas canvas;
  int renderEvents;
  string path;
  IEnumerator Start() {
   yield return new WaitUntil(delegate {return Application.isFocused;});
   yield return new WaitForSecondsRealtime(12);
   path=Path.Combine(Entry.Path,"fsr-smoke.log");
   settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
   oldMode=settings.FsrMode; oldSharpness=settings.FsrSharpness; oldWidth=Screen.width; oldHeight=Screen.height; oldScreenMode=Screen.fullScreenMode;
   try {Setup();} catch(Exception e) {Fail(e); yield break;}
   for(int mode=1;mode<=4;mode++) {
    settings.FsrMode=mode;
    yield return new WaitForSecondsRealtime(3);
    try {Check(mode);} catch(Exception e) {Fail(e); yield break;}
   }
   settings.FsrMode=0;
   yield return new WaitForSecondsRealtime(2);
   try {if(camera.targetTexture!=null) throw new Exception("Off failed to restore camera target"); Log("PASS disabled camera target restored");} catch(Exception e) {Fail(e); yield break;}
   settings.FsrMode=2;
   Screen.SetResolution(1280,720,FullScreenMode.Windowed);
   yield return new WaitForSecondsRealtime(4);
   try {Check(2);} catch(Exception e) {Fail(e); yield break;}
   camera.enabled=false;
   yield return new WaitForSecondsRealtime(1);
   try {if(camera.targetTexture) throw new Exception("Inactive camera retained target"); Log("PASS camera disable/re-enable");} catch(Exception e) {Fail(e); yield break;}
   camera.enabled=true;
   Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);
   yield return new WaitForSecondsRealtime(4);
   try {Check(2); ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"fsr-overlay.png"));} catch(Exception e) {Fail(e); yield break;}
   yield return new WaitForSecondsRealtime(3);
   try {
    var screenshot=new Texture2D(2,2,TextureFormat.RGBA32,false);
    try {
     if(!screenshot.LoadImage(File.ReadAllBytes(Path.Combine(Entry.Path,"fsr-overlay.png")))) throw new Exception("Screenshot decode failed");
     var center=(Color32)screenshot.GetPixel(screenshot.width/2,screenshot.height/2);
     var marker=(Color32)screenshot.GetPixel(20,20);
     var expected=Pixel((RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"outputTarget").GetValue(camera.GetComponent<FsrSceneCamera>()));
     if(Math.Abs(center.r-expected.r)>3 || Math.Abs(center.g-expected.g)>3 || Math.Abs(center.b-expected.b)>3 || marker.r<240 || marker.g<240 || marker.b<240) throw new Exception("Final screen or overlay pixels wrong: "+center+" / "+marker+" expected="+expected);
     Log("PASS actual screen center="+center+" overlay="+marker);
    } finally {Destroy(screenshot);}
   } catch(Exception e) {Fail(e); yield break;}
   yield return new WaitForSecondsRealtime(2);
   Restore();
   Log("PASS all modes, real size change, native GPU dispatch, color encoding, overlay canvas, restoration");
   File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"FSR smoke passed");
  }
  void Setup() {
   var uiType=typeof(UnityModManager).GetNestedType("UI",BindingFlags.Public|BindingFlags.NonPublic);
   var ui=AccessTools.Field(uiType,"mInstance").GetValue(null);
   AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(ui,new object[]{false});
   var go=new GameObject("FSR synthetic scene camera"); go.transform.SetParent(transform,false);
   camera=go.AddComponent<Camera>(); camera.depth=1000; camera.cullingMask=1<<31;
   camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.5f,.25f,.75f,1); camera.allowHDR=false;
   Camera.onPreRender+=Observe;
   go.AddComponent<FsrSceneCamera>();
   var overlay=new GameObject("FSR full-resolution overlay",typeof(RectTransform),typeof(Canvas)); overlay.transform.SetParent(transform,false);
   canvas=overlay.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=32700;
   var block=new GameObject("One-pixel UI stripes",typeof(RectTransform),typeof(Image)); block.transform.SetParent(overlay.transform,false);
   var rect=block.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=Vector2.zero; rect.pivot=Vector2.zero; rect.anchoredPosition=new Vector2(10,10); rect.sizeDelta=new Vector2(100,30);
   block.GetComponent<Image>().color=Color.white;
   Log("START graphics="+SystemInfo.graphicsDeviceType+" color-space="+QualitySettings.activeColorSpace+" LSFG="+settings.Enabled+" multiplier="+settings.Multiplier);
  }
  void Check(int mode) {
   Log("Render events="+renderEvents+" focus="+Application.isFocused+" targetDisplay="+camera.targetDisplay);
   var fsr=camera.GetComponent<FsrSceneCamera>();
   var input=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"inputTarget").GetValue(fsr);
   var output=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"outputTarget").GetValue(fsr);
   var handle=(IntPtr)AccessTools.Field(typeof(FsrSceneCamera),"job").GetValue(fsr);
   var runtime=typeof(Main).Assembly.GetType("DVLSFG.FsrRuntime");
   object[] args={handle,(ulong)0}; int state=(int)AccessTools.Method(runtime,"DVFSR_Status").Invoke(null,args);
   if(state!=1 || (ulong)args[1]<5) {
    camera.Render();
    var compositor=(Camera)AccessTools.Field(typeof(FsrSceneCamera),"outputCamera").GetValue(fsr);
    compositor.Render();
    Log("Manual Render issued");
    foreach(var cam in Camera.allCameras) Log("CAM "+cam.name+" enabled="+cam.enabled+" active="+cam.gameObject.activeInHierarchy+" depth="+cam.depth+" target="+(cam.targetTexture ? cam.targetTexture.name : "screen"));
    throw new Exception("GPU dispatch inactive state="+state+" frames="+args[1]+" status="+AccessTools.Property(typeof(FsrSceneCamera),"Status").GetValue(fsr,null));
   }
   if(output.width!=Screen.width || output.height!=Screen.height || input.width>=output.width || input.height>=output.height) throw new Exception("Render/output size separation failed");
   Color32 before=Pixel(input),after=Pixel(output);
   if(Math.Abs(before.r-after.r)>2 || Math.Abs(before.g-after.g)>2 || Math.Abs(before.b-after.b)>2 || after.a!=255) throw new Exception("Gamma/color mismatch: "+before+" -> "+after);
   if(before.r<30 || before.b<30) throw new Exception("Scene not rendered into low-resolution target");
   if(canvas.renderMode!=RenderMode.ScreenSpaceOverlay || canvas.worldCamera!=null) throw new Exception("UI no longer uses full-resolution overlay");
   Log("PASS mode="+mode+" "+input.width+"x"+input.height+" -> "+output.width+"x"+output.height+" frames="+args[1]+" color="+before+" -> "+after+" aa="+camera.targetTexture.antiAliasing);
  }
  Color32 Pixel(RenderTexture target) {
   var previous=RenderTexture.active; var texture=new Texture2D(1,1,TextureFormat.RGBA32,false);
   try {RenderTexture.active=target; texture.ReadPixels(new Rect(target.width/2,target.height/2,1,1),0,0); texture.Apply(); return texture.GetPixels32()[0];}
   finally {RenderTexture.active=previous; Destroy(texture);}
  }
  void Restore() {
   Camera.onPreRender-=Observe;
   if(settings!=null) {settings.FsrMode=oldMode; settings.FsrSharpness=oldSharpness;}
   if(camera) {camera.enabled=false; Destroy(camera.gameObject);}
   if(canvas) Destroy(canvas.gameObject);
   Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);
  }
  void Fail(Exception e) {Log("FAIL "+e); Restore();}
  void Observe(Camera cam) {if(renderEvents++<8) Log("RENDER "+cam.name);}
  void Log(string message) {Entry.Logger.Log(message); File.AppendAllText(path,message+"\n");}
 }
}

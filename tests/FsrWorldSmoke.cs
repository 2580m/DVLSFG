using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
using DVLSFG;
namespace DVLSFGTests {
 public static class FsrWorldSmoke {
  public static bool Load(UnityModManager.ModEntry mod) {var go=new GameObject("FSR world test"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<FsrWorldRunner>().Entry=mod; return true;}
 }
 public sealed class FsrWorldRunner : MonoBehaviour {
  public UnityModManager.ModEntry Entry;
  Settings settings;
  int oldMode,oldWidth,oldHeight,oldMultiplier;
  bool oldPerformance; float oldFlow;
  [DllImport("DVLSFG.Native.dll",CallingConvention=CallingConvention.Cdecl)] static extern int DVLSFG_GetGenerationSettings(out int multiplier,out int performance,out float flowScale);
  [DllImport("DVLSFG.Native.dll",CallingConvention=CallingConvention.Cdecl)] static extern int DVLSFG_GetMultiplier(out int value);
  [DllImport("DVLSFG.Native.dll",CallingConvention=CallingConvention.Cdecl)] static extern int DVLSFG_GetStatus(StringBuilder text,uint capacity,out ulong real,out ulong generated);
  FullScreenMode oldScreenMode;
  string log;
  IEnumerator Start() {
   yield return new WaitUntil(delegate {return Application.isFocused;});
   yield return new WaitForSecondsRealtime(15);
   log=Path.Combine(Entry.Path,"world.log");
   settings=(Settings)AccessTools.Field(typeof(Main),"settings").GetValue(null);
   oldMode=settings.FsrMode; oldMultiplier=settings.Multiplier; oldWidth=Screen.width; oldHeight=Screen.height; oldScreenMode=Screen.fullScreenMode;
   oldPerformance=settings.Performance; oldFlow=settings.FlowScale;
   try {
    var uiType=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
    var ui=AccessTools.Field(uiType,"mInstance").GetValue(null); AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(ui,new object[]{false});
   } catch(Exception e) {Fail(e); yield break;}
   float menuDeadline=Time.realtimeSinceStartup+90;
   MainMenuController activeMenu=null;
   while(Time.realtimeSinceStartup<menuDeadline && !activeMenu) {
    foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(menu.gameObject.activeInHierarchy) {activeMenu=menu; break;}
    if(!activeMenu) yield return new WaitForSecondsRealtime(1);
   }
   if(!activeMenu) {Fail(new Exception("Main menu not loaded")); yield break;}
   try {
    var menu=activeMenu;
     ((ButtonDV)AccessTools.Field(typeof(MainMenuController),"continueButton").GetValue(menu)).Click(); Write("Continue requested");
   } catch(Exception e) {Fail(e); yield break;}
   yield return new WaitForSecondsRealtime(2);
   try {
    var continueType=AccessTools.TypeByName("DV.UI.LauncherController");
    foreach(var obj in Resources.FindObjectsOfTypeAll(continueType)) {var c=obj as Component; if(c && c.gameObject.activeInHierarchy) {((UnityEngine.UI.Button)AccessTools.Field(continueType,"runButton").GetValue(c)).onClick.Invoke(); Write("Start existing save requested"); break;}}
    settings.FsrMode=2;
   } catch(Exception e) {Fail(e); yield break;}
   float deadline=Time.realtimeSinceStartup+150;
   while(Time.realtimeSinceStartup<deadline && !Find()) yield return new WaitForSecondsRealtime(1);
   if(!Find()) {Fail(new Exception("No active game scene camera after Continue")); yield break;}
   float readyDeadline=Time.realtimeSinceStartup+60;
   while(Time.realtimeSinceStartup<readyDeadline && !Ready()) yield return new WaitForSecondsRealtime(.5f);
   try {Check("first-person"); ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"world-quality.png"));} catch(Exception e) {Fail(e); yield break;}
   int[] multipliers={2,3,4,2,3,4,2,4};
   for(int test=0;test<multipliers.Length;test++) {
    int multiplier=multipliers[test];
    settings.Multiplier=multiplier; settings.Performance=test%2==1; settings.FlowScale=new float[]{.25f,.5f,.75f,1f}[test%4];
    AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);
    yield return new WaitForSecondsRealtime(3);
    ulong r0,g0,r1,g1; var text=new StringBuilder(4096);
    DVLSFG_GetStatus(text,4096,out r0,out g0);
    yield return new WaitForSecondsRealtime(3);
    DVLSFG_GetStatus(text,4096,out r1,out g1);
    try {
     int actual,performance; float flow;
     if(DVLSFG_GetGenerationSettings(out actual,out performance,out flow)!=0 || actual!=multiplier || performance!=(settings.Performance?1:0) || flow!=settings.FlowScale) throw new Exception("Live generation settings not applied");
     if(r1-r0<10 || Math.Abs((long)(g1-g0)-(long)(r1-r0)*(multiplier-1))>3) throw new Exception("Generated/input ratio incorrect");
     Write("LIVE PASS "+actual+"x performance="+performance+" flow="+flow+" input="+(r1-r0)+" generated="+(g1-g0)); Check("live generation settings");
    } catch(Exception e) {Fail(e); yield break;}
   }
   var switcherType=AccessTools.TypeByName("PlayerCameraSwitcher");
   UnityEngine.Object switcher=null;
   if(switcherType!=null) foreach(var obj in Resources.FindObjectsOfTypeAll(switcherType)) {var c=obj as Component; if(c && c.gameObject.activeInHierarchy) {switcher=obj; break;}}
   if(switcher!=null) {
    try {Request(switcherType,switcher,"External");} catch(Exception e) {Fail(e); yield break;}
    yield return new WaitForSecondsRealtime(6);
    try {Check("external"); if(Find().GetComponent<Camera>().name!="ExternalCam") throw new Exception("External view not selected"); ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"external-quality.png"));} catch(Exception e) {Fail(e); yield break;}
    try {Request(switcherType,switcher,"FirstPerson");} catch(Exception e) {Fail(e); yield break;}
   }
   for(int mode=1;mode<=4;mode++) {
    settings.FsrMode=mode; yield return new WaitForSecondsRealtime(4);
    try {Check("mode "+mode);} catch(Exception e) {Fail(e); yield break;}
   }
   Screen.SetResolution(1280,720,FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(6);
   try {Check("1280x720 resize with LSFG "+settings.Multiplier+"x");} catch(Exception e) {Fail(e); yield break;}
   Restore(); yield return new WaitForSecondsRealtime(5);
   try {if(oldMode==0) {foreach(var c in Resources.FindObjectsOfTypeAll<FsrSceneCamera>()) if(c.GetComponent<Camera>() && c.GetComponent<Camera>().enabled && c.GetComponent<Camera>().targetTexture) throw new Exception("Off did not restore game camera");} else Check("restored original FSR");} catch(Exception e) {Fail(e); yield break;}
   Write("PASS restored original FSR mode and resolution; game scene, all modes, external camera and resize");
   File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"World FSR test passed");
  }
  static void Request(Type type,UnityEngine.Object switcher,string view) {var method=AccessTools.Method(type,"RequestView"); var args=method.GetParameters(); method.Invoke(switcher,new object[]{Enum.Parse(args[0].ParameterType,view),Enum.Parse(args[1].ParameterType,"PlayerInput")});}
  FsrSceneCamera Find() {foreach(var c in Resources.FindObjectsOfTypeAll<FsrSceneCamera>()) if(c.isActiveAndEnabled && c.GetComponent<Camera>() && c.GetComponent<Camera>().enabled && c.GetComponent<Camera>().cullingMask!=0) return c; return null;}
  bool Ready() {
   var camera=Find(); if(!camera) return false;
   var input=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"inputTarget").GetValue(camera);
   var output=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"outputTarget").GetValue(camera);
   var handle=(IntPtr)AccessTools.Field(typeof(FsrSceneCamera),"job").GetValue(camera);
   if(handle==IntPtr.Zero || !input || !output || input.width>=output.width || output.width!=Screen.width) return false;
   object[] args={handle,(ulong)0}; int state=(int)AccessTools.Method(typeof(Main).Assembly.GetType("DVLSFG.FsrRuntime"),"DVFSR_Status").Invoke(null,args);
   return state==1 && (ulong)args[1]>=10;
  }
  void Check(string stage) {
   var camera=Find(); if(!camera) throw new Exception("No FSR camera at "+stage);
   var input=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"inputTarget").GetValue(camera);
   var output=(RenderTexture)AccessTools.Field(typeof(FsrSceneCamera),"outputTarget").GetValue(camera);
   var handle=(IntPtr)AccessTools.Field(typeof(FsrSceneCamera),"job").GetValue(camera);
   object[] args={handle,(ulong)0}; int state=(int)AccessTools.Method(typeof(Main).Assembly.GetType("DVLSFG.FsrRuntime"),"DVFSR_Status").Invoke(null,args);
   if(state!=1 || (ulong)args[1]<10 || !input || !output || input.width>=output.width || output.width!=Screen.width) throw new Exception("Inactive/invalid FSR at "+stage+" state="+state+" frames="+args[1]+" input="+(input ? input.width.ToString() : "none")+" output="+(output ? output.width.ToString() : "none")+" screen="+Screen.width);
   Write("PASS "+stage+" camera="+camera.GetComponent<Camera>().name+" render="+input.width+"x"+input.height+" output="+output.width+"x"+output.height+" frames="+args[1]+" path="+camera.GetComponent<Camera>().actualRenderingPath);
   foreach(var canvas in Resources.FindObjectsOfTypeAll<Canvas>()) if(canvas.isActiveAndEnabled && canvas.renderMode!=RenderMode.WorldSpace) Write("CANVAS "+canvas.name+" mode="+canvas.renderMode+" camera="+(canvas.worldCamera ? canvas.worldCamera.name : "none"));
  }
  void Restore() {if(settings!=null) {settings.FsrMode=oldMode; settings.Multiplier=oldMultiplier; settings.Performance=oldPerformance; settings.FlowScale=oldFlow; AccessTools.Method(typeof(Main),"ApplyEnabled").Invoke(null,null);} Screen.SetResolution(oldWidth,oldHeight,oldScreenMode);}
  void Fail(Exception e) {Write("FAIL "+e); Restore();}
  void Write(string message) {Entry.Logger.Log(message); File.AppendAllText(log,message+"\n");}
 }
}

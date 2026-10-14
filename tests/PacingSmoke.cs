using System;
using System.IO;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using DVLSFG;
using DV.UI;
using DV.UIFramework;
namespace DVLSFGTests {
 public static class PacingSmoke {
  internal static double tickMs,uiMs;
  static long tickStart,uiStart;
  public static bool Load(UnityModManager.ModEntry entry) {
   var harmony=new Harmony("DVLSFG.PacingTest");
   harmony.Patch(AccessTools.Method(typeof(Main).Assembly.GetType("DVLSFG.FsrRuntime"),"Tick"),new HarmonyMethod(typeof(PacingSmoke),"TickBefore"),new HarmonyMethod(typeof(PacingSmoke),"TickAfter"));
   harmony.Patch(AccessTools.Method(typeof(Main).Assembly.GetType("DVLSFG.GraphicsSettingsPanel"),"Refresh"),new HarmonyMethod(typeof(PacingSmoke),"UiBefore"),new HarmonyMethod(typeof(PacingSmoke),"UiAfter"));
   var go=new GameObject("Pacing test"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<PacingRunner>().Entry=entry; return true;
  }
  static void TickBefore() {tickStart=Stopwatch.GetTimestamp();}
  static void TickAfter() {tickMs=(Stopwatch.GetTimestamp()-tickStart)*1000.0/Stopwatch.Frequency;}
  static void UiBefore() {uiStart=Stopwatch.GetTimestamp();}
  static void UiAfter() {uiMs+=(Stopwatch.GetTimestamp()-uiStart)*1000.0/Stopwatch.Frequency;}
 }
 public sealed class PacingRunner : MonoBehaviour {
  public UnityModManager.ModEntry Entry;
  struct Sample {public int frame,gc;public float time,dt;public double tick,ui;}
  Sample[] samples=new Sample[30000]; int count; bool recording;
  IEnumerator Start() {
   yield return new WaitForSecondsRealtime(15);
   var uiType=typeof(UnityModManager).GetNestedType("UI",BindingFlags.Public|BindingFlags.NonPublic);
   var ui=AccessTools.Field(uiType,"mInstance").GetValue(null); AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(ui,new object[]{false});
   MainMenuController active=null; float deadline=Time.realtimeSinceStartup+90;
   while(!active && Time.realtimeSinceStartup<deadline) {foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(menu.gameObject.activeInHierarchy) {active=menu;break;} if(!active) yield return new WaitForSecondsRealtime(1);}
   if(active) {((ButtonDV)AccessTools.Field(typeof(MainMenuController),"continueButton").GetValue(active)).Click(); yield return new WaitForSecondsRealtime(2); var type=AccessTools.TypeByName("DV.UI.LauncherController"); foreach(var obj in Resources.FindObjectsOfTypeAll(type)) {var c=obj as Component; if(c && c.gameObject.activeInHierarchy) {((UnityEngine.UI.Button)AccessTools.Field(type,"runButton").GetValue(c)).onClick.Invoke();break;}}}
   yield return new WaitForSecondsRealtime(40);
   Entry.Logger.Log("PACING recording 40 seconds"); recording=true;
   yield return new WaitForSecondsRealtime(40); recording=false;
   using(var writer=new StreamWriter(Path.Combine(Entry.Path,"frames.csv"))) {
    writer.WriteLine("frame,time,dt_ms,fsr_tick_ms,ui_refresh_ms,gc0");
    for(int i=0;i<count;i++) {var s=samples[i]; writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:F6},{2:F4},{3:F4},{4:F4},{5}",s.frame,s.time,s.dt*1000,s.tick,s.ui,s.gc));}
   }
   File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"Recorded "+count+" frames"); Entry.Logger.Log("PACING recorded "+count+" frames");
  }
  void LateUpdate() {
   if(recording && count<samples.Length) samples[count++]=new Sample {frame=Time.frameCount,time=Time.realtimeSinceStartup,dt=Time.unscaledDeltaTime,tick=PacingSmoke.tickMs,ui=PacingSmoke.uiMs,gc=GC.CollectionCount(0)};
   PacingSmoke.uiMs=0;
  }
 }
}

using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;
namespace DVLSFGTests {
 public static class ResolutionSmoke {
  static DateTime deadline;
  static int stage, width, height;
  static FullScreenMode mode;
  static readonly int[,] sizes={{1280,720},{2560,1440},{1920,1080},{1024,768},{2560,1440}};
  static UnityModManager.ModEntry entry;
  public static bool Load(UnityModManager.ModEntry mod) {
   entry=mod; width=Screen.width; height=Screen.height; mode=Screen.fullScreenMode;
   var restore=Path.Combine(entry.Path,"restore-only.txt");
   if(File.Exists(restore)) { var values=File.ReadAllText(restore).Trim().Split(','); width=int.Parse(values[0]); height=int.Parse(values[1]); mode=(FullScreenMode)int.Parse(values[2]); stage=sizes.GetLength(0); }
   deadline=DateTime.UtcNow.AddSeconds(15); mod.OnUpdate=Update; return true;
  }
  static void Update(UnityModManager.ModEntry mod,float dt) {
   if(stage<0 || DateTime.UtcNow<deadline) return;
   File.AppendAllText(Path.Combine(entry.Path,"resolutions.log"),"stage="+stage+" actual="+Screen.width+"x"+Screen.height+"\n");
   if(stage<sizes.GetLength(0)) { Screen.SetResolution(sizes[stage,0],sizes[stage,1],FullScreenMode.Windowed); ++stage; deadline=DateTime.UtcNow.AddSeconds(8); }
   else if(stage==sizes.GetLength(0)) { Screen.SetResolution(width,height,mode); ++stage; deadline=DateTime.UtcNow.AddSeconds(8); }
   else { File.AppendAllText(Path.Combine(entry.Path,"resolutions.log"),"PASS restored="+Screen.width+"x"+Screen.height+" mode="+Screen.fullScreenMode+"\n"); stage=-1; }
  }
 }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityModManagerNet;
namespace DVLSFGTests {
    public static class FrameRateSmoke {
        static UnityModManager.ModEntry entry;
        static DateTime deadline;
        static int stage=-1, originalLimit;
        static bool originalBackground;
        static readonly int[] limits={30,60,120,-1};
        [DllImport("DVLSFG.Native.dll",CallingConvention=CallingConvention.Cdecl)] static extern void DVLSFG_GetMetrics(out float input,out float submitted,out float mean,out float peak);
        public static bool Load(UnityModManager.ModEntry mod) {
            entry=mod; originalLimit=Application.targetFrameRate; originalBackground=Application.runInBackground;
            Application.runInBackground=true; deadline=DateTime.UtcNow.AddSeconds(12); mod.OnUpdate=Update; return true;
        }
        static void Update(UnityModManager.ModEntry mod,float delta) {
            if(stage==limits.Length || DateTime.UtcNow<deadline) return;
            if(stage>=0) {
                float input,submitted,mean,peak; DVLSFG_GetMetrics(out input,out submitted,out mean,out peak);
                File.AppendAllText(Path.Combine(entry.Path,"rates.csv"),limits[stage]+","+input.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+submitted.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+mean.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n");
                entry.Logger.Log("RATE TEST: target="+limits[stage]+" input="+input+" submitted="+submitted);
            }
            ++stage;
            if(stage<limits.Length) { Application.targetFrameRate=limits[stage]; deadline=DateTime.UtcNow.AddSeconds(12); }
            else { Application.targetFrameRate=originalLimit; Application.runInBackground=originalBackground; entry.Logger.Log("RATE TEST COMPLETE: restored original transient Unity settings"); }
        }
    }
}

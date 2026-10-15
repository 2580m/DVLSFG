using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
using DVLSFG;
namespace DVLSFGTests {
    public static class GraphicsArrowSmoke {
        public static bool Load(UnityModManager.ModEntry entry) {
            var go=new GameObject("Graphics arrow validation");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GraphicsArrowRunner>().Entry=entry; return true;
        }
    }
    public sealed class GraphicsArrowRunner : MonoBehaviour {
        [System.Runtime.InteropServices.DllImport("DVLSFG.Native",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)] static extern void DVLSFG_RequestCapture();
        [System.Runtime.InteropServices.DllImport("DVLSFG.Native",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)] static extern int DVLSFG_GetStatus(IntPtr text,uint capacity,out ulong real,out ulong generated);
        public UnityModManager.ModEntry Entry;
        void Log(string text) {File.AppendAllText(Path.Combine(Entry.Path,"arrows.log"),text+"\n");}
        IEnumerator Start() {
            MainMenuController menu=null;
            while(!menu) {foreach(var candidate in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(candidate.gameObject.activeInHierarchy) {menu=candidate; break;} yield return new WaitForSecondsRealtime(1);}
            var type=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            AccessTools.Method(type,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(type,"mInstance").GetValue(null),new object[]{false});
            ((ButtonDV)AccessTools.Field(typeof(MainMenuController),"settingsButton").GetValue(menu)).Click();
            yield return new WaitForSecondsRealtime(2);
            SettingsController controller=null;
            foreach(var candidate in Resources.FindObjectsOfTypeAll<SettingsController>()) if(candidate.gameObject.activeInHierarchy) {controller=candidate; break;}
            SettingsCategoryMarker marker=null;
            foreach(var candidate in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(candidate.categoryName=="Graphics") marker=candidate;
            var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(controller);
            AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
            var root=controller.GetComponentInParent<Canvas>().rootCanvas;
            var originalMode=root.renderMode; var originalCamera=root.worldCamera;
            root.renderMode=RenderMode.ScreenSpaceOverlay; root.worldCamera=null;
            marker.GetComponent<ScrollRect>().verticalNormalizedPosition=1;
            yield return new WaitForSecondsRealtime(2);
            Dump(marker,"native");
            yield return new WaitForEndOfFrame(); yield return null;
            var go=new GameObject("Arrow capture scene");
            var camera=go.AddComponent<Camera>(); camera.depth=1000; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.2f,.3f,.6f); camera.cullingMask=1<<30;
            go.AddComponent<FsrSceneCamera>(); go.AddComponent<Fsr3SceneCamera>().enabled=false;
            go.AddComponent<SceneUiSceneCamera>(); go.AddComponent<TemporalSceneCamera>();
            ulong realBefore,generatedBefore; DVLSFG_GetStatus(IntPtr.Zero,0,out realBefore,out generatedBefore);
            yield return new WaitForSecondsRealtime(4);
            Dump(marker,"generation");
            DVLSFG_RequestCapture();
            yield return new WaitForSecondsRealtime(2);
            yield return new WaitForEndOfFrame(); yield return null;
            ulong realAfter,generatedAfter; DVLSFG_GetStatus(IntPtr.Zero,0,out realAfter,out generatedAfter);
            if(generatedAfter<=generatedBefore) throw new Exception("Arrow test did not generate any frames");
            Log("Generated frames="+(generatedAfter-generatedBefore));
            var capture=UnityEngine.Object.FindObjectOfType<SceneUiCapture>();
            if(!capture) throw new Exception("UI capture unavailable");
            var target=(RenderTexture)AccessTools.Field(typeof(SceneUiCapture),"black").GetValue(capture);
            var prior=RenderTexture.active; RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Entry.Path,"arrows-ui.png"),image.EncodeToPNG());
            int checkedArrows=0;
            foreach(var selector in marker.GetComponentsInChildren<Selector>(true)) {
                if(selector.name!="DVLSFG Graphics 0" && selector.name!="DVLSFG Graphics 6" && selector.name!="DVLSFG Graphics 8") continue;
                foreach(var name in new[]{"[arrow left]","[arrow right]"}) {
                    var arrow=selector.transform.Find(name);
                    foreach(var icon in arrow.GetComponentsInChildren<Image>(true)) {
                        if(icon.name!="icon") continue;
                        var point=RectTransformUtility.WorldToScreenPoint(null,icon.transform.position);
                        int whitePixels=0;
                        for(int y=(int)point.y-20;y<(int)point.y+20;y++) for(int x=(int)point.x-20;x<(int)point.x+20;x++) {
                            var color=image.GetPixel(x,y); if(color.r>.6f && color.g>.6f && color.b>.6f) whitePixels++;
                        }
                        if(whitePixels<10 || icon.canvasRenderer.cull) throw new Exception("Missing arrow: "+selector.name+" "+name+" white="+whitePixels+" culled="+icon.canvasRenderer.cull);
                        checkedArrows++; Log("PASS "+selector.name+" "+name+" visible pixels="+whitePixels);
                    }
                }
            }
            if(checkedArrows!=6) throw new Exception("Expected six visible native selector arrows, got "+checkedArrows);
            RenderTexture.active=prior; Destroy(image);
            camera.enabled=false; Destroy(go);
            root.renderMode=originalMode; root.worldCamera=originalCamera;
            File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"Native and frame-generation arrow captures recorded");
        }
        void Dump(SettingsCategoryMarker marker,string stage) {
            foreach(var selector in marker.GetComponentsInChildren<Selector>(true)) {
                foreach(var name in new[]{"[arrow left]","[arrow right]"}) {
                    var arrow=selector.transform.Find(name); if(!arrow) continue;
                    foreach(var image in arrow.GetComponentsInChildren<Image>(true)) Log(stage+" "+selector.name+" "+name+" "+image.name+" enabled="+image.enabled+" active="+image.gameObject.activeInHierarchy+" cull="+image.canvasRenderer.cull+" alpha="+image.canvasRenderer.GetAlpha()+" sprite="+(image.sprite ? image.sprite.name : "null")+" rect="+image.rectTransform.rect+" position="+image.transform.position);
                }
            }
        }
    }
}

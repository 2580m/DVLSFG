using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
namespace DVLSFGTests {
    public static class UpscalingTextSmoke {
        public static bool Load(UnityModManager.ModEntry entry) {var go=new GameObject("Upscaling text validation"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<UpscalingTextRunner>().Entry=entry; return true;}
    }
    public sealed class UpscalingTextRunner : MonoBehaviour {
        public UnityModManager.ModEntry Entry;
        void Log(string text) {File.AppendAllText(Path.Combine(Entry.Path,"text.log"),text+"\n"); Entry.Logger.Log(text);}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(15);
            MainMenuController menu=null;
            while(!menu) {foreach(var candidate in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(candidate.gameObject.activeInHierarchy) {menu=candidate; break;} if(!menu) yield return new WaitForSecondsRealtime(1);}
            var type=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            AccessTools.Method(type,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(type,"mInstance").GetValue(null),new object[]{false});
            ((ButtonDV)AccessTools.Field(typeof(MainMenuController),"settingsButton").GetValue(menu)).Click();
            yield return new WaitForSecondsRealtime(2);
            SettingsController controller=null;
            foreach(var candidate in Resources.FindObjectsOfTypeAll<SettingsController>()) if(candidate.gameObject.activeInHierarchy) {controller=candidate; break;}
            var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(controller);
            SettingsCategoryMarker marker=null;
            foreach(var candidate in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(candidate.categoryName=="Graphics") marker=candidate;
            AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
            yield return new WaitForSecondsRealtime(2);
            Selector selector=null;
            foreach(var candidate in marker.GetComponentsInChildren<Selector>(true)) if(candidate.name=="DVLSFG Graphics 6") selector=candidate;
            selector.SetSelectedIndex(1,true);
            var scroll=marker.GetComponent<ScrollRect>(); scroll.verticalNormalizedPosition=1;
            yield return new WaitForSecondsRealtime(1);
            var value=(TMPro.TextMeshProUGUI)AccessTools.Field(typeof(Selector),"valueTMPro").GetValue(selector);
            value.ForceMeshUpdate();
            Log("Native value: auto="+value.enableAutoSizing+" font="+value.fontSize+" min="+value.fontSizeMin+" max="+value.fontSizeMax+" overflow="+value.overflowMode+" wrap="+value.enableWordWrapping+" preferred="+value.preferredWidth+" rect="+value.rectTransform.rect+" alignment="+value.alignment);
            foreach(var rt in selector.GetComponentsInChildren<RectTransform>(true)) Log(rt.name+" rect="+rt.rect+" position="+rt.localPosition);
            ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"name-initial.png"));
            yield return new WaitForSecondsRealtime(3);
            ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"name-later.png"));
            if(selector.GetValues()[1]!="FidelityFX™ Super Resolution" || selector.GetValues()[2]!="AMD FSR 3.1") throw new Exception("Incorrect technology names");
            var marquee=selector.GetComponent<DVLSFG.GraphicsValueMarquee>();
            if(!marquee || !marquee.IsScrolling || !marquee.Viewport.GetComponent<RectMask2D>()) throw new Exception("Long value does not use clipped scrolling");
            var label=selector.GetLabelTMPro(); label.ForceMeshUpdate();
            if(label.preferredWidth>label.rectTransform.rect.width+.5f) throw new Exception("Title is still truncated");
            var viewport=marquee.Viewport;
            var left=selector.transform.InverseTransformPoint(viewport.TransformPoint(new Vector3(viewport.rect.xMin,0,0))).x;
            var labelRight=selector.transform.InverseTransformPoint(label.rectTransform.TransformPoint(label.textBounds.max)).x;
            var right=selector.transform.InverseTransformPoint(viewport.TransformPoint(new Vector3(viewport.rect.xMax,0,0))).x;
            var arrow=(RectTransform)selector.transform.Find("[arrow right]");
            var arrowLeft=selector.transform.InverseTransformPoint(arrow.TransformPoint(new Vector3(arrow.rect.xMin,0,0))).x;
            if(left-labelRight<8 || arrowLeft-right<8) throw new Exception("Value area lacks spacing");
            float originalOffset=value.rectTransform.anchoredPosition.x;
            yield return new WaitForSecondsRealtime(2);
            if(Mathf.Abs(originalOffset-value.rectTransform.anchoredPosition.x)<20) throw new Exception("Long value does not move");
            Log("PASS clipped scrolling: title intact; label gap="+(left-labelRight)+" arrow gap="+(arrowLeft-right)+" viewport="+viewport.rect.width);
            selector.SetSelectedIndex(2,true); yield return null; yield return null;
            if(marquee.IsScrolling || value.rectTransform.anchoredPosition.x!=0) throw new Exception("Short value keeps scrolling");
            ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Path,"name-short.png"));
            Log("PASS names selected; AMD FSR 3.1 restores static right alignment");
            File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"Clipped marquee, title/arrow spacing and short-name reset passed");
        }
    }
}

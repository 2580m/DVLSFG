using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using I2.Loc;
using DV.UI;
using DV.UIFramework;
using UnityEngine;
using UnityEngine.UI;

namespace DVLSFG {
    // Uses the game's own selectors and existing Apply/Discard actions.
    public sealed class GraphicsSettingsPanel : MonoBehaviour {
        SettingsController controller;
        readonly List<Selector> rows=new List<Selector>();
        Settings pending;
        bool syncing;
        string language;
        string failure;
        float statusTimer;
        ScrollRect graphicsScroll;
        bool graphicsVisible;
        internal static string Text(string zh,string en) {
            return (LocalizationManager.CurrentLanguageCode ?? "").StartsWith("zh",StringComparison.OrdinalIgnoreCase) ? zh : en;
        }
        static Settings Copy(Settings s) { return new Settings { Enabled=s.Enabled,DllPath=s.DllPath,Multiplier=s.Multiplier,Performance=s.Performance,FlowScale=s.FlowScale }; }
        internal bool Dirty { get { return pending!=null && (pending.Enabled!=Main.settings.Enabled || pending.Multiplier!=Main.settings.Multiplier || pending.Performance!=Main.settings.Performance || pending.FlowScale!=Main.settings.FlowScale); } }
        void Start() {
            controller=GetComponent<SettingsController>();
            try {
                SettingsCategoryMarker graphics=null;
                foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) {
                    if(string.Equals(marker.categoryName,"Graphics",StringComparison.OrdinalIgnoreCase)) graphics=marker;
                }
                if(graphics==null) throw new InvalidOperationException("Graphics settings category not found");
                Selector template=null;
                foreach(var selector in graphics.GetComponentsInChildren<Selector>(true)) {
                    if(selector.GetType()==typeof(Selector) && selector.GetComponent<SettingChangeSourceSelector>()!=null) { template=selector; break; }
                }
                if(template==null) throw new InvalidOperationException("Native graphics selector not found");
                // An inactive clone lets us remove the original preference binding
                // before Awake subscribes it to the game's settings provider.
                var staging=new GameObject("DVLSFG inactive UI staging"); staging.SetActive(false);
                try {
                    for(int i=0;i<6;i++) {
                        var row=Instantiate(template.gameObject,staging.transform,false);
                        row.name="DVLSFG Graphics "+i;
                        foreach(var binding in row.GetComponentsInChildren<SettingChangeSource>(true)) DestroyImmediate(binding);
                        foreach(var component in row.GetComponentsInChildren<MonoBehaviour>(true)) {
                            var name=component.GetType().FullName;
                            if(name=="I2.Loc.Localize" || name=="DV.Localization.Localize" || name.StartsWith("DV.UI.UIElementTooltip")) DestroyImmediate(component);
                        }
                        var selector=row.GetComponent<Selector>();
                        row.SetActive(false); row.transform.SetParent(template.transform.parent,false);
                        row.transform.SetSiblingIndex(i);
                        row.SetActive(true);
                        selector.LocalizedLabel=false; selector.LocalizedValues=false;
                        int field=i;
                        selector.SelectionChanged+=delegate(IClickable source,int index) { Change(field,index); };
                        rows.Add(selector);
                    }
                } finally { Destroy(staging); }
                graphicsScroll=MakeScrollable(template.transform.parent,FindDividerStyle(controller));
                pending=Copy(Main.settings); Refresh();
                Main.modEntry.Logger.Log("LSFG controls installed in native Graphics settings (6 rows)");
            } catch(Exception e) { failure=e.Message; Main.modEntry.Logger.Error("Graphics settings integration: "+e); }
        }
        static Image FindDividerStyle(SettingsController controller) {
            foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) {
                if(marker.categoryName!="Advanced") continue;
                foreach(var image in marker.GetComponentsInChildren<Image>(true)) {
                    if(image.name=="image" && image.transform.parent.name.StartsWith("Divider",StringComparison.OrdinalIgnoreCase)) return image;
                }
            }
            return null;
        }
        static ScrollRect MakeScrollable(Transform parent,Image dividerStyle) {
            var grid=parent.GetComponent<GridLayoutGroup>();
            if(!grid) return null;
            var children=new List<Transform>(); foreach(Transform child in parent) children.Add(child);
            var viewport=new GameObject("DVLSFG Graphics viewport",typeof(RectTransform),typeof(RectMask2D));
            viewport.transform.SetParent(parent,false);
            var viewportRect=(RectTransform)viewport.transform;
            viewportRect.anchorMin=Vector2.zero; viewportRect.anchorMax=Vector2.one; viewportRect.offsetMin=new Vector2(0,75); viewportRect.offsetMax=Vector2.zero;
            var content=new GameObject("DVLSFG Graphics content",typeof(RectTransform),typeof(GridLayoutGroup),typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform,false);
            var contentRect=(RectTransform)content.transform;
            contentRect.anchorMin=new Vector2(0,1); contentRect.anchorMax=Vector2.one; contentRect.pivot=new Vector2(0,1); contentRect.sizeDelta=Vector2.zero;
            var layout=content.GetComponent<GridLayoutGroup>();
            layout.cellSize=grid.cellSize; layout.spacing=grid.spacing; layout.padding=grid.padding;
            layout.startCorner=grid.startCorner; layout.startAxis=grid.startAxis; layout.childAlignment=grid.childAlignment;
            layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount=2;
            content.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            DestroyImmediate(grid);
            foreach(var child in children) child.SetParent(content.transform,false);
            var scroll=parent.gameObject.AddComponent<ScrollRect>();
            scroll.viewport=viewportRect; scroll.content=contentRect; scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=35;
            var bar=new GameObject("DVLSFG Graphics scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar));
            bar.transform.SetParent(parent,false);
            var barRect=(RectTransform)bar.transform;
            barRect.anchorMin=new Vector2(1,0); barRect.anchorMax=Vector2.one; barRect.pivot=new Vector2(0,1);
            barRect.offsetMin=new Vector2(4,75); barRect.offsetMax=new Vector2(12,0);
            bar.GetComponent<Image>().color=new Color(.025f,.04f,.07f,.9f);
            var handle=new GameObject("Handle",typeof(RectTransform),typeof(Image)); handle.transform.SetParent(bar.transform,false);
            var handleRect=(RectTransform)handle.transform; handleRect.anchorMin=Vector2.zero; handleRect.anchorMax=Vector2.one; handleRect.offsetMin=handleRect.offsetMax=Vector2.zero;
            var image=handle.GetComponent<Image>(); image.color=new Color(1f,.57f,.12f,.8f);
            var scrollbar=bar.GetComponent<Scrollbar>(); scrollbar.handleRect=handleRect; scrollbar.targetGraphic=image; scrollbar.direction=Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar=scrollbar;
            // Clone the actual native graphic, including sliced-sprite settings
            // and pixel density. Recreating selected Image properties changes
            // its visual style; additional border rules are not part of it.
            var divider=dividerStyle ? Instantiate(dividerStyle.gameObject,parent,false) : new GameObject("DVLSFG preset divider",typeof(RectTransform),typeof(Image));
            divider.name="DVLSFG preset divider";
            if(!dividerStyle) divider.transform.SetParent(parent,false);
            var dividerRect=(RectTransform)divider.transform;
            dividerRect.anchorMin=Vector2.zero; dividerRect.anchorMax=new Vector2(1,0); dividerRect.pivot=new Vector2(.5f,.5f);
            dividerRect.anchoredPosition=new Vector2(0,60); dividerRect.sizeDelta=new Vector2(dividerStyle ? dividerStyle.rectTransform.sizeDelta.x : -40,dividerStyle ? dividerStyle.rectTransform.sizeDelta.y : 2);
            var dividerImage=divider.GetComponent<Image>(); dividerImage.raycastTarget=false;
            if(dividerStyle) {
                Main.modEntry.Logger.Log("Preset divider styled from Advanced / "+dividerStyle.name);
            } else dividerImage.color=new Color(1,1,1,.25f);
            scroll.verticalNormalizedPosition=1;
            return scroll;
        }
        void Update() {
            if(rows.Count!=6) return;
            if(graphicsScroll) {
                bool visible=graphicsScroll.gameObject.activeInHierarchy;
                if(visible && !graphicsVisible) { Canvas.ForceUpdateCanvases(); graphicsScroll.verticalNormalizedPosition=1; }
                graphicsVisible=visible;
            }
            string current=LocalizationManager.CurrentLanguageCode;
            statusTimer+=Time.unscaledDeltaTime;
            if(language!=current || statusTimer>=1f) { statusTimer=0; Refresh(); }
            foreach(var row in rows) if(row) row.gameObject.SetActive(Main.modEntry.Active);
        }
        void Change(int field,int index) {
            if(syncing || pending==null || field>=4) return;
            switch(field) {
                case 0: pending.Enabled=index==1; break;
                case 1: pending.Multiplier=index+2; break;
                case 2: pending.Performance=index==1; break;
                case 3: pending.FlowScale=new float[]{.25f,.5f,.75f,1f}[index]; break;
            }
            Refresh();
        }
        void Set(int row,string label,List<string> values,int index,bool enabled) {
            rows[row].SetLabel(label); rows[row].SetValues(values); rows[row].SetSelectedIndex(index,false); rows[row].ToggleInteractable(enabled);
        }
        void Refresh() {
            if(rows.Count!=6 || pending==null) return;
            syncing=true;
            try {
                language=LocalizationManager.CurrentLanguageCode;
                Set(0,Text("LSFG 帧生成","LSFG frame generation"),new List<string>{Text("关闭","Off"),Text("开启","On")},pending.Enabled?1:0,true);
                Set(1,Text("插帧倍率","Frame generation multiplier"),new List<string>{"2x","3x","4x"},pending.Multiplier-2,true);
                Set(2,Text("插帧模型","Frame generation model"),new List<string>{Text("标准","Standard"),Text("性能","Performance")},pending.Performance?1:0,true);
                int scale=Mathf.Clamp(Mathf.RoundToInt(pending.FlowScale*4)-1,0,3);
                Set(3,Text("光流质量","Optical flow quality"),new List<string>{"25%","50%","75%","100%"},scale,true);
                string status=failure ?? (Dirty ? Text("待应用","Pending Apply") : Main.GraphicsStatus);
                Set(4,Text("LSFG 状态","LSFG status"),new List<string>{status},0,false);
                Set(5,Text("倍率 / 模型 / 质量","Multiplier / model / quality"),new List<string>{Text("应用后重启生效","Apply, then restart")},0,false);
            } finally { syncing=false; }
        }
        internal void Apply() {
            if(!Dirty) return;
            var previous=Main.settings;
            pending.DllPath=previous.DllPath;
            Main.settings=Copy(pending);
            if(!Main.SaveSettings(Main.modEntry)) { Main.settings=previous; failure=Text("保存失败，请查看 UMM","Save failed; see UMM"); }
            else { Main.ApplyEnabled(); failure=null; }
            Refresh();
        }
        internal void Discard() { pending=Copy(Main.settings); failure=null; Refresh(); }
        internal void SetButtons() {
            if(!Dirty || !Main.modEntry.Active) return;
            foreach(var name in new[]{"applyChanges","revertChanges"}) {
                var button=AccessTools.Field(typeof(SettingsController),name).GetValue(controller) as Button;
                if(button) button.interactable=true;
            }
        }
    }
    [HarmonyPatch(typeof(SettingsController),"Awake")]
    static class InstallGraphicsSettings {
        static void Postfix(SettingsController __instance) { if(!__instance.GetComponent<GraphicsSettingsPanel>()) __instance.gameObject.AddComponent<GraphicsSettingsPanel>(); }
    }
    [HarmonyPatch(typeof(SettingsController),"get_HasChanges")]
    static class IncludeLSFGChanges {
        static void Postfix(SettingsController __instance,ref bool __result) { var panel=__instance.GetComponent<GraphicsSettingsPanel>(); if(panel && Main.modEntry.Active) __result|=panel.Dirty; }
    }
    [HarmonyPatch(typeof(SettingsController),"Update")]
    static class EnableLSFGApplyButtons {
        static void Postfix(SettingsController __instance) { var panel=__instance.GetComponent<GraphicsSettingsPanel>(); if(panel) panel.SetButtons(); }
    }
    [HarmonyPatch]
    static class ApplyLSFGGraphicsSettings {
        static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("DV.UI.SettingsProvider"),"ApplyChanges"); }
        static void Postfix() { foreach(var panel in Resources.FindObjectsOfTypeAll<GraphicsSettingsPanel>()) if(Main.modEntry.Active) panel.Apply(); }
    }
    [HarmonyPatch(typeof(ASettingsProvider),"RevertChanges")]
    static class DiscardLSFGGraphicsSettings {
        static void Postfix() { foreach(var panel in Resources.FindObjectsOfTypeAll<GraphicsSettingsPanel>()) panel.Discard(); }
    }
}

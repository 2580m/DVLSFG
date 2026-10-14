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
        readonly List<GameObject> controls=new List<GameObject>();
        readonly List<GraphicsSettingsTooltip> tooltips=new List<GraphicsSettingsTooltip>();
        SliderDV sharpnessSlider;
        TMPro.TextMeshProUGUI sharpnessLabel,sharpnessValue;
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
        static Settings Copy(Settings s) { return new Settings { Enabled=s.Enabled,DllPath=s.DllPath,Multiplier=s.Multiplier,Performance=s.Performance,FlowScale=s.FlowScale,FsrMode=s.FsrMode,FsrSharpness=s.FsrSharpness }; }
        internal bool Dirty { get { return pending!=null && (pending.Enabled!=Main.settings.Enabled || pending.Multiplier!=Main.settings.Multiplier || pending.Performance!=Main.settings.Performance || pending.FlowScale!=Main.settings.FlowScale || pending.FsrMode!=Main.settings.FsrMode || pending.FsrSharpness!=Main.settings.FsrSharpness); } }
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
                    for(int i=0;i<10;i++) {
                        if(i==4 || i==5 || i==8 || i==9) { rows.Add(null); continue; }
                        if(i==7) {
                            var sliderRow=CreateSharpnessSlider(staging.transform);
                            AddTooltip(sliderRow,i);
                            sliderRow.SetActive(false); sliderRow.transform.SetParent(template.transform.parent,false);
                            sliderRow.transform.SetSiblingIndex(controls.Count); sliderRow.SetActive(true);
                            sharpnessSlider.onValueChanged.AddListener(ChangeSharpness);
                            rows.Add(null); controls.Add(sliderRow); continue;
                        }
                        var row=Instantiate(template.gameObject,staging.transform,false);
                        row.name="DVLSFG Graphics "+i;
                        foreach(var binding in row.GetComponentsInChildren<SettingChangeSource>(true)) DestroyImmediate(binding);
                        foreach(var component in row.GetComponentsInChildren<MonoBehaviour>(true)) {
                            var name=component.GetType().FullName;
                            if(name=="I2.Loc.Localize" || name=="DV.Localization.Localize" || name.StartsWith("DV.UI.UIElementTooltip")) DestroyImmediate(component);
                        }
                        var selector=row.GetComponent<Selector>();
                        AddTooltip(row,i);
                        row.SetActive(false); row.transform.SetParent(template.transform.parent,false);
                        row.transform.SetSiblingIndex(controls.Count);
                        row.SetActive(true);
                        selector.LocalizedLabel=false; selector.LocalizedValues=false;
                        int field=i;
                        selector.SelectionChanged+=delegate(IClickable source,int index) { Change(field,index); };
                        rows.Add(selector);
                        controls.Add(row);
                    }
                } finally { Destroy(staging); }
                graphicsScroll=MakeScrollable(template.transform.parent,FindDividerStyle(controller));
                pending=Copy(Main.settings); Refresh();
                Main.modEntry.Logger.Log("LSFG / FSR controls installed in native Graphics settings (6 controls with native descriptions)");
            } catch(Exception e) { failure=e.Message; Main.modEntry.Logger.Error("Graphics settings integration: "+e); }
        }
        void AddTooltip(GameObject row,int field) {
            var help=row.AddComponent<GraphicsSettingsTooltip>(); help.Panel=this; help.Field=field;
            row.AddComponent<UIElementTooltip>();
            tooltips.Add(help);
        }
        internal string Description(int field) {
            switch(field) {
                case 0:
                    return Text("使用 LSFG 生成中间帧，首次启用需要重启游戏以加载插帧层。\n当前状态：","Generate intermediate frames with LSFG. Restart when first enabling to load the generation layer.\nCurrent status: ")
                        +(failure ?? Main.GraphicsStatus)+(Dirty ? Text("（有未应用设置）"," (unapplied changes)") : "");
                case 1:
                    return Text("选择原始帧率的 2、3 或 4 倍作为生成目标，实际帧率取决于 GPU 性能。","Target 2, 3 or 4 times the original frame rate. Actual output depends on GPU performance.");
                case 2:
                    return Text("标准：侧重画质，GPU 计算开销较高。\n性能：降低计算开销，可能增加插帧伪影。","Standard: prioritizes image quality, with higher GPU compute cost.\nPerformance: reduces compute cost, with potentially more interpolation artifacts.");
                case 3:
                    return Text("调整光流计算的分辨率比例。较高值保留更多运动细节；较低值降低 GPU 开销，但可能增加伪影。","Adjust optical flow resolution. Higher values retain more motion detail; lower values reduce GPU cost but may increase artifacts.");
                case 6:
                    return Text("以较低分辨率渲染场景，再使用 FidelityFX Super Resolution 放大到输出分辨率。超高质量更清晰，性能模式降低更多渲染开销。\n当前渲染 / 输出：","Render the scene at a lower resolution and upscale to the output resolution with FidelityFX Super Resolution. Ultra Quality retains more detail; Performance reduces rendering cost further.\nCurrent rendering / output: ")+FsrRuntime.Status;
                case 7:
                    return Text("调整 FSR 放大后的锐化强度。0% 关闭锐化，100% 为最强锐化；过高可能使边缘出现光晕。\n当前渲染 / 输出：","Adjust sharpening after FSR upscaling. 0% disables sharpening; 100% is strongest. Excessive sharpening can cause edge halos.\nCurrent rendering / output: ")+FsrRuntime.Status;
                default: return "";
            }
        }
        GameObject CreateSharpnessSlider(Transform staging) {
            SettingChangeSourceSlider template=null;
            foreach(var candidate in controller.GetComponentsInChildren<SettingChangeSourceSlider>(true)) {
                var slider=candidate.GetComponent<SliderDV>();
                if(slider && slider.GetType()==typeof(SliderDV) && !candidate.GetComponent<TextureStreamingBudgetSliderCustomization>()) {template=candidate; break;}
            }
            if(!template) throw new InvalidOperationException("Native settings slider not found");
            var row=Instantiate(template.gameObject,staging,false); row.name="DVLSFG Graphics 7";
            var binding=row.GetComponent<SettingChangeSourceSlider>();
            sharpnessLabel=AccessTools.Field(typeof(SettingChangeSource),"labelTMPro").GetValue(binding) as TMPro.TextMeshProUGUI;
            foreach(var original in row.GetComponentsInChildren<SettingChangeSource>(true)) DestroyImmediate(original);
            foreach(var component in row.GetComponentsInChildren<MonoBehaviour>(true)) {
                var name=component.GetType().FullName;
                if(name=="I2.Loc.Localize" || name=="DV.Localization.Localize" || name.StartsWith("DV.UI.UIElementTooltip")) DestroyImmediate(component);
            }
            sharpnessSlider=row.GetComponent<SliderDV>();
            sharpnessSlider.onValueChanged=new Slider.SliderEvent();
            sharpnessSlider.minValue=0; sharpnessSlider.maxValue=100; sharpnessSlider.wholeNumbers=true;
            sharpnessSlider.interactable=true;
            AccessTools.Field(typeof(SliderDV),"useStepping").SetValue(sharpnessSlider,false);
            AccessTools.Field(typeof(SliderDV),"localizeValueKey").SetValue(sharpnessSlider,"");
            var value=sharpnessSlider.transform.Find("[texts]/[text value] [noloc]");
            sharpnessValue=value ? value.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if(!sharpnessLabel || !sharpnessValue) throw new InvalidOperationException("Native slider label/readout not found");
            return row;
        }
        void ChangeSharpness(float percent) {
            if(syncing || pending==null) return;
            pending.FsrSharpness=Mathf.Round(percent)/100f;
            Refresh();
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
            if(rows.Count!=10) return;
            if(graphicsScroll) {
                bool visible=graphicsScroll.gameObject.activeInHierarchy;
                if(visible && !graphicsVisible) { Canvas.ForceUpdateCanvases(); graphicsScroll.verticalNormalizedPosition=1; Refresh(); statusTimer=0; }
                graphicsVisible=visible;
            }
            if(!graphicsVisible) return;
            string current=LocalizationManager.CurrentLanguageCode;
            statusTimer+=Time.unscaledDeltaTime;
            if(language!=current || statusTimer>=1f) { statusTimer=0; Refresh(); }
            foreach(var control in controls) if(control) control.SetActive(Main.modEntry.Active);
        }
        void Change(int field,int index) {
            if(syncing || pending==null) return;
            switch(field) {
                case 0: pending.Enabled=index==1; break;
                case 1: pending.Multiplier=index+2; break;
                case 2: pending.Performance=index==1; break;
                case 3: pending.FlowScale=new float[]{.25f,.5f,.75f,1f}[index]; break;
                case 6: pending.FsrMode=index==0 ? 0 : 5-index; break;
                default: return;
            }
            Refresh();
        }
        void Set(int row,string label,List<string> values,int index,bool enabled) {
            rows[row].SetLabel(label); rows[row].SetValues(values); rows[row].SetSelectedIndex(index,false); rows[row].ToggleInteractable(enabled);
        }
        void Refresh() {
            if(rows.Count!=10 || pending==null) return;
            syncing=true;
            try {
                language=LocalizationManager.CurrentLanguageCode;
                Set(0,Text("LSFG 帧生成","LSFG frame generation"),new List<string>{Text("关闭","Off"),Text("开启","On")},pending.Enabled?1:0,true);
                Set(1,Text("帧生成倍率","Frame generation multiplier"),new List<string>{"2x","3x","4x"},pending.Multiplier-2,true);
                Set(2,Text("帧生成模型","Frame generation model"),new List<string>{Text("标准","Standard"),Text("性能","Performance")},pending.Performance?1:0,true);
                int scale=Mathf.Clamp(Mathf.RoundToInt(pending.FlowScale*4)-1,0,3);
                Set(3,Text("光流质量","Optical flow quality"),new List<string>{"25%","50%","75%","100%"},scale,true);
                Set(6,Text("FSR 超分辨率","FSR upscaling"),new List<string>{Text("关闭","Off"),Text("性能","Performance"),Text("平衡","Balanced"),Text("质量","Quality"),Text("超高质量","Ultra Quality")},pending.FsrMode==0 ? 0 : 5-pending.FsrMode,true);
                sharpnessLabel.text=Text("FSR 锐化强度","FSR sharpening");
                int percent=Mathf.RoundToInt(pending.FsrSharpness*100);
                sharpnessSlider.SetValueWithoutNotify(percent);
                sharpnessValue.text=percent+"%";
                foreach(var tooltip in tooltips) if(tooltip) tooltip.RefreshText();
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
    public sealed class GraphicsSettingsTooltip : UIElementTooltipCustomText {
        internal GraphicsSettingsPanel Panel;
        internal int Field;
        string previous;
        public override string GetText() { return Panel ? Panel.Description(Field) : ""; }
        internal void RefreshText() {
            string next=GetText();
            if(next==previous) return;
            previous=next;
            TextChanged_Fire();
        }
    }
    // Fit multiline help inside the existing footer; native items retain their own typography.
    public sealed class GraphicsDescriptionLayout : MonoBehaviour {
        TMPro.TMP_Text text;
        Vector4 margin;
        float fontSize,minSize,maxSize;
        bool autoSize,custom;
        internal void Configure(TMPro.TMP_Text target,bool help) {
            if(!text) {
                text=target; margin=text.margin; fontSize=text.fontSize;
                minSize=text.fontSizeMin; maxSize=text.fontSizeMax; autoSize=text.enableAutoSizing;
            }
            if(custom==help) return;
            custom=help;
            text.margin=help ? new Vector4(margin.x,Mathf.Min(margin.y,9),margin.z,Mathf.Min(margin.w,9)) : margin;
            text.fontSizeMin=help ? fontSize*.8f : minSize;
            text.fontSizeMax=help ? fontSize : maxSize;
            text.enableAutoSizing=help || autoSize;
            if(!help) text.fontSize=fontSize;
        }
    }
    [HarmonyPatch(typeof(TooltipHandler),"UpdateTooltipText")]
    static class FitGraphicsDescriptions {
        static readonly FieldInfo Active=AccessTools.Field(typeof(TooltipHandler),"activeTooltips");
        static readonly FieldInfo Footer=AccessTools.Field(typeof(TooltipHandler),"tooltipText");
        static void Postfix(TooltipHandler __instance) {
            var list=Active.GetValue(__instance) as List<ITooltip>;
            bool custom=false;
            if(list!=null) for(int i=list.Count-1;i>=0;i--) {
                if(list[i]==null) continue;
                var row=list[i].GetGameObject();
                if(!row || !row.activeInHierarchy) continue;
                custom=row.GetComponent<GraphicsSettingsTooltip>()!=null; break;
            }
            var layout=__instance.GetComponent<GraphicsDescriptionLayout>();
            if(!custom && !layout) return;
            var footer=Footer.GetValue(__instance) as TMPro.TMP_Text; if(!footer) return;
            if(!layout) layout=__instance.gameObject.AddComponent<GraphicsDescriptionLayout>();
            layout.Configure(footer,custom);
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

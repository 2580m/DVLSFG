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
        readonly Dictionary<Selector,bool> antialiasingControls=new Dictionary<Selector,bool>();
        void UpdateAntialiasingControls() {
            bool disable=Main.modEntry.Active && pending!=null && pending.FsrMode>0 && pending.FsrTechnology==2;
            if(disable && antialiasingControls.Count==0) {
                foreach(var binding in controller.GetComponentsInChildren<SettingChangeSourceSelector>(true)) {
                    string preference=AccessTools.Field(typeof(SettingChangeSource),"preferencesName").GetValue(binding) as string;
                    if((preference ?? binding.name).IndexOf("AntiAliasing",StringComparison.OrdinalIgnoreCase)<0) continue;
                    var selector=binding.GetComponent<Selector>();
                    if(selector) antialiasingControls[selector]=selector.IsInteractable;
                }
            }
            foreach(var control in antialiasingControls) if(control.Key && control.Key.IsInteractable!=(disable ? false : control.Value)) control.Key.ToggleInteractable(disable ? false : control.Value);
            if(!disable) antialiasingControls.Clear();
        }
        static int Quality(Settings s) { return s.FsrMode>0 ? s.FsrMode : s.FsrQuality; }
        static Settings Copy(Settings s) { return new Settings { Enabled=s.Enabled,FrameGenerationTechnology=s.FrameGenerationTechnology,DllPath=s.DllPath,Multiplier=s.Multiplier,Performance=s.Performance,FlowScale=s.FlowScale,FsrMode=s.FsrMode,FsrTechnology=s.FsrTechnology,FsrQuality=Quality(s),FsrSharpness=s.FsrSharpness }; }
        internal bool Dirty { get { return pending!=null && (pending.Enabled!=Main.settings.Enabled || pending.FrameGenerationTechnology!=Main.settings.FrameGenerationTechnology || pending.Multiplier!=Main.settings.Multiplier || pending.Performance!=Main.settings.Performance || pending.FlowScale!=Main.settings.FlowScale || pending.FsrMode!=Main.settings.FsrMode || pending.FsrTechnology!=Main.settings.FsrTechnology || Quality(pending)!=Quality(Main.settings) || pending.FsrSharpness!=Main.settings.FsrSharpness); } }
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
                        if(i==4 || i==5 || i==9) { rows.Add(null); continue; }
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
                        if(i==6) row.AddComponent<GraphicsValueMarquee>().Configure(selector);
                        int field=i;
                        selector.SelectionChanged+=delegate(IClickable source,int index) { Change(field,index); };
                        rows.Add(selector);
                        controls.Add(row);
                    }
                    var gap=new GameObject("DVLSFG Graphics gap",typeof(RectTransform));
                    gap.transform.SetParent(template.transform.parent,false); gap.transform.SetSiblingIndex(controls.Count);
                } finally { Destroy(staging); }
                graphicsScroll=MakeScrollable(template.transform.parent,FindDividerStyle(controller));
                pending=Copy(Main.settings); Refresh();
                Main.modEntry.Logger.Log("Technology controls installed: 7 controls, reserved gap and native section divider");
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
                    if(pending.FrameGenerationTechnology==2 && pending.Enabled) return L10n.Get("dv_scaling/fsr_fg_description")+(failure ?? Main.GraphicsStatus)+(Dirty ? L10n.Get("dv_scaling/unapplied_changes") : "");
                    return L10n.Get("dv_scaling/generate_intermediate_frames_with_lsfg_restart_when_first_en")
                        +(failure ?? Main.GraphicsStatus)+(Dirty ? L10n.Get("dv_scaling/unapplied_changes") : "");
                case 1:
                    if(pending.FrameGenerationTechnology==2) return L10n.Get("dv_scaling/fsr_fg_fixed_multiplier");
                    return L10n.Get("dv_scaling/target_2_3_or_4_times_the_original_frame_rate_actual_output");
                case 2:
                    if(pending.FrameGenerationTechnology==2) return L10n.Get("dv_scaling/fsr_fg_lsfg_only");
                    return L10n.Get("dv_scaling/standard_prioritizes_image_quality_with_higher_gpu_compute_c");
                case 3:
                    if(pending.FrameGenerationTechnology==2) return L10n.Get("dv_scaling/fsr_fg_lsfg_only");
                    return L10n.Get("dv_scaling/adjust_optical_flow_resolution_higher_values_retain_more_mot");
                case 6:
                    if(pending.FsrTechnology==2 && pending.FsrMode>0) return L10n.Get("dv_scaling/fsr3_description")+FsrRuntime.Status;
                    return L10n.Get("dv_scaling/choose_an_upscaling_technology_fsr_1_0_uses_fidelityfx_super")+FsrRuntime.Status;
                case 7:
                    return L10n.Get("dv_scaling/adjust_sharpening_after_fsr_upscaling_0_disables_sharpening")+FsrRuntime.Status;
                case 8:
                    return L10n.Get("dv_scaling/balance_upscaling_quality_and_performance_move_right_for_hig");
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
        ScrollRect MakeScrollable(Transform parent,Image dividerStyle) {
            var grid=parent.GetComponent<GridLayoutGroup>();
            if(!grid) return null;
            var children=new List<Transform>(); foreach(Transform child in parent) children.Add(child);
            var viewport=new GameObject("DVLSFG Graphics viewport",typeof(RectTransform),typeof(RectMask2D),typeof(Image));
            viewport.transform.SetParent(parent,false);
            // The empty space needs a raycast target too, so wheel events can
            // bubble to ScrollRect even when no setting button is under it.
            var viewportHitArea=viewport.GetComponent<Image>();
            viewportHitArea.color=Color.clear; viewportHitArea.raycastTarget=true;
            var viewportRect=(RectTransform)viewport.transform;
            viewportRect.anchorMin=Vector2.zero; viewportRect.anchorMax=Vector2.one; viewportRect.offsetMin=new Vector2(0,75); viewportRect.offsetMax=Vector2.zero;
            var content=new GameObject("DVLSFG Graphics content",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform,false);
            var contentRect=(RectTransform)content.transform;
            contentRect.anchorMin=new Vector2(0,1); contentRect.anchorMax=Vector2.one; contentRect.pivot=new Vector2(0,1); contentRect.sizeDelta=Vector2.zero;
            var stack=content.GetComponent<VerticalLayoutGroup>();
            stack.childControlWidth=true; stack.childControlHeight=true; stack.childForceExpandWidth=true; stack.childForceExpandHeight=false;
            var custom=new GameObject("DVLSFG technology section",typeof(RectTransform),typeof(GridLayoutGroup),typeof(ContentSizeFitter)); custom.transform.SetParent(content.transform,false);
            var native=new GameObject("DVLSFG native settings section",typeof(RectTransform),typeof(GridLayoutGroup),typeof(ContentSizeFitter));
            // Keep native controls under their provider while reparenting; an inactive
            // settings menu must not wake bindings in a detached active hierarchy.
            native.transform.SetParent(content.transform,false);
            foreach(var block in new[]{custom,native}) {
            var layout=block.GetComponent<GridLayoutGroup>();
            layout.cellSize=grid.cellSize; layout.spacing=grid.spacing; layout.padding=grid.padding;
            layout.startCorner=grid.startCorner; layout.startAxis=grid.startAxis; layout.childAlignment=grid.childAlignment;
            layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount=2;
            block.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            }
            content.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            DestroyImmediate(grid);
            foreach(var child in children) child.SetParent(child.name.StartsWith("DVLSFG Graphics",StringComparison.Ordinal) ? custom.transform : native.transform,false);
            var sectionDivider=new GameObject("DVLSFG technology divider",typeof(RectTransform),typeof(LayoutElement)); sectionDivider.transform.SetParent(content.transform,false);
            // Advanced gives the 20px graphic its own taller row plus layout
            // spacing. Keep that clearance when placing it between two grids.
            var dividerContainer=dividerStyle ? dividerStyle.transform.parent as RectTransform : null;
            var dividerLayout=dividerContainer ? dividerContainer.parent.GetComponent<VerticalLayoutGroup>() : null;
            float dividerHeight=dividerStyle ? dividerStyle.rectTransform.sizeDelta.y : 20;
            float dividerRowHeight=dividerContainer ? dividerContainer.sizeDelta.y : 35;
            sectionDivider.GetComponent<LayoutElement>().preferredHeight=Mathf.Max(dividerRowHeight,dividerHeight)+2*(dividerLayout ? dividerLayout.spacing : 0);
            var sectionGraphic=dividerStyle ? Instantiate(dividerStyle.gameObject,sectionDivider.transform,false) : new GameObject("image",typeof(RectTransform),typeof(Image));
            if(!dividerStyle) sectionGraphic.transform.SetParent(sectionDivider.transform,false);
            var sectionRect=(RectTransform)sectionGraphic.transform;
            sectionRect.anchorMin=new Vector2(0,.5f); sectionRect.anchorMax=new Vector2(1,.5f); sectionRect.pivot=new Vector2(.5f,.5f);
            sectionRect.anchoredPosition=Vector2.zero; sectionRect.sizeDelta=new Vector2(dividerStyle ? dividerStyle.rectTransform.sizeDelta.x : -40,dividerHeight);
            sectionGraphic.GetComponent<Image>().raycastTarget=false;
            if(!dividerStyle) sectionGraphic.GetComponent<Image>().color=new Color(1,1,1,.25f);
            native.transform.SetParent(content.transform,false);
            native.transform.SetAsLastSibling();
            controls.Add(custom); controls.Add(sectionDivider);
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
            UpdateAntialiasingControls();
        }
        void Change(int field,int index) {
            if(syncing || pending==null) return;
            switch(field) {
                case 0: pending.Enabled=index!=0; if(index!=0) pending.FrameGenerationTechnology=index; break;
                case 1: pending.Multiplier=index+2; break;
                case 2: pending.Performance=index==1; break;
                case 3: pending.FlowScale=new float[]{.25f,.5f,.75f,1f}[index]; break;
                case 6: pending.FsrTechnology=index; pending.FsrMode=index==0 ? 0 : pending.FsrQuality; break;
                case 8: pending.FsrQuality=5-index; if(pending.FsrMode>0) pending.FsrMode=pending.FsrQuality; break;
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
                bool lsfg=pending.Enabled && pending.FrameGenerationTechnology==1;
                Set(0,L10n.Get("dv_scaling/frame_generation"),new List<string>{L10n.Get("dv_scaling/off"),"LSFG 3.1","AMD FSR FG 3.1"},pending.Enabled?pending.FrameGenerationTechnology:0,true);
                Set(1,L10n.Get("dv_scaling/frame_generation_multiplier"),new List<string>{"2x","3x","4x"},pending.FrameGenerationTechnology==2 ? 0 : pending.Multiplier-2,lsfg);
                Set(2,L10n.Get("dv_scaling/frame_generation_model"),new List<string>{L10n.Get("dv_scaling/standard"),L10n.Get("dv_scaling/performance")},pending.Performance?1:0,lsfg);
                int scale=Mathf.Clamp(Mathf.RoundToInt(pending.FlowScale*4)-1,0,3);
                Set(3,L10n.Get("dv_scaling/optical_flow_quality"),new List<string>{"25%","50%","75%","100%"},scale,lsfg);
                Set(6,L10n.Get("dv_scaling/upscaling_technology"),new List<string>{L10n.Get("dv_scaling/off"),"FidelityFX™ Super Resolution","AMD FSR 3.1"},pending.FsrMode==0 ? 0 : pending.FsrTechnology==2 ? 2 : 1,true);
                Set(8,L10n.Get("dv_scaling/upscaling_quality"),new List<string>{L10n.Get("dv_scaling/ultra_performance"),L10n.Get("dv_scaling/performance"),L10n.Get("dv_scaling/balanced"),L10n.Get("dv_scaling/quality"),L10n.Get("dv_scaling/ultra_quality")},5-Quality(pending),pending.FsrMode>0);
                sharpnessSlider.ToggleInteractable(pending.FsrMode>0);
                UpdateAntialiasingControls();
                sharpnessLabel.text=L10n.Get("dv_scaling/sharpening");
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
            if(!Main.SaveSettings(Main.modEntry)) { Main.settings=previous; failure=L10n.Get("dv_scaling/save_failed_see_umm"); }
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
    // Keep the native label and arrow padding; only the option value scrolls.
    public sealed class GraphicsValueMarquee : MonoBehaviour {
        TMPro.TextMeshProUGUI text,label,repeat;
        RectTransform viewport;
        LayoutElement labelLayout;
        HorizontalLayoutGroup group;
        TMPro.TextAlignmentOptions originalAlignment;
        string previousText;
        float previousWidth=-1,elapsed;
        public bool IsScrolling {get; private set;}
        public RectTransform Viewport {get {return viewport;}}
        public void Configure(Selector selector) {
            text=(TMPro.TextMeshProUGUI)AccessTools.Field(typeof(Selector),"valueTMPro").GetValue(selector);
            label=selector.GetLabelTMPro();
            var parent=text.transform.parent;
            group=parent.GetComponent<HorizontalLayoutGroup>();
            if(!group) throw new InvalidOperationException("Native selector text layout unavailable");
            originalAlignment=text.alignment;
            var go=new GameObject("DV Scaling option value viewport",typeof(RectTransform),typeof(RectMask2D),typeof(LayoutElement));
            viewport=(RectTransform)go.transform; viewport.SetParent(parent,false); viewport.SetSiblingIndex(text.transform.GetSiblingIndex());
            var layout=go.GetComponent<LayoutElement>(); layout.minWidth=0; layout.preferredWidth=0; layout.flexibleWidth=1;
            labelLayout=label.GetComponent<LayoutElement>(); if(!labelLayout) labelLayout=label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth=0; group.childForceExpandWidth=false; group.spacing=Mathf.Max(group.spacing,18);
            text.transform.SetParent(viewport,false);
            repeat=Instantiate(text.gameObject,viewport,false).GetComponent<TMPro.TextMeshProUGUI>(); repeat.name="Scrolling value repeat";
            text.enableAutoSizing=false; text.enableWordWrapping=false; text.overflowMode=TMPro.TextOverflowModes.Overflow; text.raycastTarget=false;
            repeat.enableAutoSizing=false; repeat.enableWordWrapping=false; repeat.overflowMode=TMPro.TextOverflowModes.Overflow; repeat.raycastTarget=false;
            foreach(var value in new[]{text,repeat}) {var rt=value.rectTransform; rt.anchorMin=rt.anchorMax=new Vector2(0,.5f); rt.pivot=new Vector2(0,.5f); rt.anchoredPosition=Vector2.zero;}
            repeat.gameObject.SetActive(false);
        }
        void OnEnable() {previousText=null; elapsed=0;}
        void LateUpdate() {
            if(!text || !viewport || !label) return;
            float labelWidth=label.GetPreferredValues(label.text).x;
            if(Mathf.Abs(labelLayout.preferredWidth-labelWidth)>.1f) {
                labelLayout.minWidth=labelLayout.preferredWidth=labelWidth;
                LayoutRebuilder.MarkLayoutForRebuild(group.transform as RectTransform);
            }
            float width=viewport.rect.width;
            if(width<=1) return;
            if(previousText!=text.text || Mathf.Abs(previousWidth-width)>.1f) {previousText=text.text; previousWidth=width; elapsed=0;}
            float preferred=text.GetPreferredValues(text.text).x+4;
            IsScrolling=preferred>width+.5f;
            text.rectTransform.sizeDelta=new Vector2(IsScrolling ? preferred : width,viewport.rect.height);
            text.alignment=IsScrolling ? TMPro.TextAlignmentOptions.Left : originalAlignment;
            repeat.gameObject.SetActive(IsScrolling);
            if(!IsScrolling) {text.rectTransform.anchoredPosition=Vector2.zero; return;}
            repeat.text=text.text;
            if(repeat.color!=text.color) repeat.color=text.color;
            if(repeat.font!=text.font) repeat.font=text.font;
            if(repeat.fontSize!=text.fontSize) repeat.fontSize=text.fontSize;
            repeat.alignment=text.alignment;
            repeat.rectTransform.sizeDelta=text.rectTransform.sizeDelta;
            elapsed+=Mathf.Min(Time.unscaledDeltaTime,.1f);
            float cycle=preferred+32;
            float offset=Mathf.Max(0,elapsed-1)*30%cycle;
            text.rectTransform.anchoredPosition=new Vector2(-offset,0);
            repeat.rectTransform.anchoredPosition=new Vector2(cycle-offset,0);
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
    [HarmonyPatch(typeof(SettingChangeSourceSlider),"OnResetOrApplied")]
    static class DeferUninitializedSettingsSlider {
        static readonly FieldInfo Slider=AccessTools.Field(typeof(SettingChangeSourceSlider),"slider");
        static bool Prefix(SettingChangeSourceSlider __instance) {
            // The provider notifies hidden categories too. Their slider cache is
            // populated by Awake only when they first become active; OnEnable
            // will apply the latest provider value then.
            return __instance.gameObject.activeInHierarchy || Slider.GetValue(__instance)!=null;
        }
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

using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
using DVLSFG;
using I2.Loc;
using DV.Localization;
namespace DVLSFGTests {
    public static class GraphicsUiSmoke {
        static DateTime deadline=DateTime.UtcNow.AddSeconds(8);
        static int stage;
        static UnityModManager.ModEntry entry;
        static Selector[] rows;
        static SliderDV sharpness;
        static SettingsController controller;
        static int liveStep,oldModel,oldFlow;
        public static bool Load(UnityModManager.ModEntry mod) { entry=mod; mod.OnUpdate=Update; return true; }
        static void CheckBlankScroll(ScrollRect scroll,RectTransform blank) {
            Canvas.ForceUpdateCanvases();
            var canvas=scroll.GetComponentInParent<Canvas>();
            var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var point=RectTransformUtility.WorldToScreenPoint(camera,blank.TransformPoint(blank.rect.center));
            var pointer=new PointerEventData(EventSystem.current) {position=point,scrollDelta=new Vector2(0,-1)};
            var hits=new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer,hits);
            if(hits.Count==0 || hits[0].gameObject!=scroll.viewport.gameObject) throw new Exception("Blank area does not hit viewport: "+blank.name+(hits.Count>0 ? " hit="+hits[0].gameObject.name : " no hit"));
            var before=scroll.content.anchoredPosition;
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.scrollHandler);
            if(scroll.content.anchoredPosition==before) throw new Exception("Blank area wheel did not move content: "+blank.name);
            scroll.verticalNormalizedPosition=1;
            entry.Logger.Log("BLANK SCROLL PASS: "+blank.name+" raycast and wheel propagation");
        }
        static void Update(UnityModManager.ModEntry mod,float delta) {
            if(stage<0) return;
            if(DateTime.UtcNow<deadline) return;
            try {
                if(stage==0) {
                    var uiType=typeof(UnityModManager).GetNestedType("UI",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                    var ui=AccessTools.Field(uiType,"mInstance").GetValue(null);
                    AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(ui,new object[]{false});
                    foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) {
                        if(!menu.gameObject.activeInHierarchy) continue;
                        ((ButtonDV)AccessTools.Field(typeof(MainMenuController),"settingsButton").GetValue(menu)).Click();
                        stage=1; deadline=DateTime.UtcNow.AddSeconds(2); return;
                    }
                } else if(stage==1) {
                    foreach(var screen in Resources.FindObjectsOfTypeAll<SettingsController>()) {
                        if(!screen.gameObject.activeInHierarchy) continue;
                        controller=screen;
                        var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(screen);
                        foreach(var marker in screen.GetComponentsInChildren<SettingsCategoryMarker>(true)) {
                            if(marker.categoryName!="Graphics") continue;
                            AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
                            rows=marker.GetComponentsInChildren<Selector>(true);
                            var list=new System.Collections.Generic.List<Selector>();
                            foreach(var row in rows) if(row.name.StartsWith("DVLSFG Graphics")) list.Add(row);
                            if(list.Count!=6) throw new Exception("Expected 6 editable selectors plus sharpening slider, found "+list.Count);
                            rows=new Selector[10]; foreach(var row in list) rows[int.Parse(row.name.Substring("DVLSFG Graphics ".Length))]=row;
                            foreach(var slider in marker.GetComponentsInChildren<SliderDV>(true)) if(slider.name=="DVLSFG Graphics 7") sharpness=slider;
                            if(!sharpness || sharpness.minValue!=0 || sharpness.maxValue!=100 || !sharpness.wholeNumbers) throw new Exception("FSR 0-100 percent slider absent");
                            var scroll=marker.GetComponent<ScrollRect>();
                            if(!scroll || !scroll.vertical || !scroll.verticalScrollbar) throw new Exception("Graphics scroll controls absent");
                            foreach(int removed in new[]{4,5,9}) if(rows[removed]) throw new Exception("Read-only status/prompt row remains: "+removed);
                            if(rows[0].GetValues().Count!=3 || rows[0].GetValues()[1]!="LSFG 3.1" || rows[0].GetValues()[2]!="AMD FSR FG 3.1" || rows[6].GetValues()[1]!="FidelityFX™ Super Resolution" || rows[6].GetValues()[2]!="AMD FSR 3.1" || rows[8].GetValues().Count!=5) throw new Exception("Technology/quality selectors incorrect");
                            var section=rows[6].transform.parent;
                            if(section!=rows[8].transform.parent || rows[8].transform.GetSiblingIndex()!=6 || section.childCount!=8 || section.GetChild(7).name!="DVLSFG Graphics gap") throw new Exception("Quality row or reserved gap misplaced");
                            if(section.parent.GetChild(1).name!="DVLSFG technology divider" || section.parent.GetChild(2).name!="DVLSFG native settings section") throw new Exception("Native section divider missing");
                            Canvas.ForceUpdateCanvases();
                            foreach(var advanced in screen.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(advanced.categoryName=="Advanced") {
                                foreach(var image in advanced.GetComponentsInChildren<Image>(true)) if(image.name=="image" && image.transform.parent.name.StartsWith("Divider")) {
                                    var box=(RectTransform)image.transform.parent;
                                    var layout=box.parent.GetComponent<LayoutGroup>();
                                    entry.Logger.Log("NATIVE DIVIDER BOX: rect="+box.rect+" size="+box.sizeDelta+" preferred="+LayoutUtility.GetPreferredHeight(box)+" layout="+layout+" image="+image.rectTransform.rect);
                                    var divider=(RectTransform)section.parent.GetChild(1);
                                    var graphic=divider.GetComponentInChildren<Image>().rectTransform;
                                    float spacing=((VerticalLayoutGroup)layout).spacing;
                                    float expected=box.sizeDelta.y+2*spacing;
                                    if(Math.Abs(divider.rect.height-expected)>.1f || Math.Abs(graphic.rect.height-image.rectTransform.rect.height)>.1f || graphic.anchoredPosition.y!=0 || divider.rect.height<=graphic.rect.height) throw new Exception("Divider does not preserve native vertical clearance");
                                    entry.Logger.Log("DIVIDER CLEARANCE PASS: container="+divider.rect.height+" graphic="+graphic.rect.height+" clearance per side="+((divider.rect.height-graphic.rect.height)/2)+" native spacing="+spacing);
                                }
                            }
                            if(Math.Abs(rows[8].transform.localPosition.x-rows[6].transform.localPosition.x)>.1f || rows[8].transform.localPosition.y>=rows[6].transform.localPosition.y) throw new Exception("Quality is not below the technology selector");
                            entry.Logger.Log("PREVIEW PASS: six selectors plus sharpening slider, quality below technology, reserved empty cell and native divider");
                            stage=2; deadline=DateTime.UtcNow.AddSeconds(2); return;
                        }
                    }
                } else if(stage==2) {
                    var aaControls=new System.Collections.Generic.Dictionary<Selector,bool>();
                    foreach(var binding in controller.GetComponentsInChildren<SettingChangeSourceSelector>(true)) {
                        string preference=AccessTools.Field(typeof(SettingChangeSource),"preferencesName").GetValue(binding) as string;
                        entry.Logger.Log("NATIVE SELECTOR: "+binding.name+" preference="+preference);
                        if((preference ?? binding.name).IndexOf("AntiAliasing",StringComparison.OrdinalIgnoreCase)<0) continue;
                        var selector=binding.GetComponent<Selector>(); if(selector) aaControls[selector]=selector.IsInteractable;
                    }
                    if(aaControls.Count==0) throw new Exception("Native antialiasing selector not found");
                    int originalTechnology=rows[6].SelectedIndex;
                    rows[6].SetSelectedIndex(2,true);
                    foreach(var control in aaControls) if(control.Key.IsInteractable) throw new Exception("FSR3 did not disable native antialiasing immediately");
                    ((Button)AccessTools.Field(typeof(SettingsController),"revertChanges").GetValue(controller)).onClick.Invoke();
                    if(rows[6].SelectedIndex!=originalTechnology) throw new Exception("FSR3 technology discard failed");
                    foreach(var control in aaControls) if(control.Key.IsInteractable!=control.Value) throw new Exception("FSR3 discard did not restore native antialiasing");
                    entry.Logger.Log("FSR3 UI PASS: technology selection immediately disables native antialiasing; Discard restores availability");
                    rows[6].SetSelectedIndex(0,true);
                    var nativeAa=new System.Collections.Generic.Dictionary<Selector,bool>();
                    foreach(var control in aaControls) nativeAa[control.Key]=control.Key.IsInteractable;
                    rows[0].SetSelectedIndex(2,true);
                    if(rows[1].IsInteractable || rows[2].IsInteractable || rows[3].IsInteractable || rows[1].SelectedIndex!=0) throw new Exception("FSR FG does not show fixed 2x and disable LSFG-only controls");
                    foreach(var control in nativeAa) if(control.Key.IsInteractable!=control.Value) throw new Exception("FSR FG changed native antialiasing without FSR upscaling");
                    ((Button)AccessTools.Field(typeof(SettingsController),"revertChanges").GetValue(controller)).onClick.Invoke();
                    entry.Logger.Log("FSR FG UI PASS: fixed 2x, LSFG controls disabled, AA preserved and pending technology discarded");
                    string originalLanguage=LocalizationManager.CurrentLanguage;
                    try {
                        foreach(string language in new[]{"English","Chinese (Simplified)","German"}) {
                            LocalizationManager.CurrentLanguage=language;
                            AccessTools.Method(typeof(GraphicsSettingsPanel),"Refresh").Invoke(controller.GetComponent<GraphicsSettingsPanel>(),null);
                            string label=language=="Chinese (Simplified)" ? "帧生成" : "Frame generation";
                            if(LocalizationAPI.L("dv_scaling/frame_generation")!=label || rows[0].GetLabelTMPro().text!=label) throw new Exception("CSV/API language switch failed: "+language);
                            string quality=language=="Chinese (Simplified)" ? "超级性能" : "Ultra Performance";
                            if(rows[8].GetValues()[0]!=quality) throw new Exception("CSV quality localization failed: "+language);
                            string help=rows[2].GetComponent<UIElementTooltip>().GetText();
                            if(help.Split('\n').Length!=2 || !help.StartsWith(language=="Chinese (Simplified)" ? "标准：" : "Standard:")) throw new Exception("Multiline CSV description failed: "+language);
                        }
                        entry.Logger.Log("LOCALIZATION PASS: game API, English/Chinese switching, German English fallback, quality labels and multiline descriptions");
                    } finally {
                        LocalizationManager.CurrentLanguage=originalLanguage;
                        AccessTools.Method(typeof(GraphicsSettingsPanel),"Refresh").Invoke(controller.GetComponent<GraphicsSettingsPanel>(),null);
                    }
                    var scroll=rows[0].GetComponentInParent<ScrollRect>();
                    scroll.verticalNormalizedPosition=1;
                    CheckBlankScroll(scroll,(RectTransform)rows[0].transform.parent.GetChild(7));
                    CheckBlankScroll(scroll,(RectTransform)scroll.content.GetChild(1));
                    int originalGeneration=rows[0].SelectedIndex,originalUpscaling=rows[6].SelectedIndex;
                    int originalMultiplier=rows[1].SelectedIndex,originalModel=rows[2].SelectedIndex,originalFlow=rows[3].SelectedIndex,originalQuality=rows[8].SelectedIndex;
                    float originalSharpness=sharpness.value;
                    foreach(int index in new[]{0,1}) {
                        rows[0].SetSelectedIndex(index,true); rows[6].SetSelectedIndex(index,true);
                        foreach(int dependent in new[]{1,2,3,8}) if(rows[dependent].IsInteractable!=(index==1)) throw new Exception("Dependent selector did not update immediately: "+dependent);
                        if(sharpness.IsInteractable!=(index==1) || sharpness.interactable!=(index==1)) throw new Exception("Sharpening did not update immediately");
                        if(!rows[0].IsInteractable || !rows[6].IsInteractable) throw new Exception("Technology switches disabled");
                    }
                    ((Button)AccessTools.Field(typeof(SettingsController),"revertChanges").GetValue(controller)).onClick.Invoke();
                    if(rows[0].SelectedIndex!=originalGeneration || rows[6].SelectedIndex!=originalUpscaling || rows[1].SelectedIndex!=originalMultiplier || rows[2].SelectedIndex!=originalModel || rows[3].SelectedIndex!=originalFlow || rows[8].SelectedIndex!=originalQuality || sharpness.value!=originalSharpness) throw new Exception("Technology toggle/discard lost selected values");
                    if(rows[1].IsInteractable!=(originalGeneration==1) || rows[8].IsInteractable!=(originalUpscaling>0) || sharpness.IsInteractable!=(originalUpscaling>0)) throw new Exception("Discard did not restore dependent interactability");
                    entry.Logger.Log("DEPENDENT CONTROLS PASS: immediate disable/enable before Apply, native interactability, retained values and Discard restore");
                    var nativeTooltip=rows[2].GetComponent<UIElementTooltip>();
                    var handler=(TooltipHandler)AccessTools.Field(typeof(UIElementTooltip),"tooltipHandler").GetValue(nativeTooltip);
                    var footer=(TMPro.TMP_Text)AccessTools.Field(typeof(TooltipHandler),"tooltipText").GetValue(handler);
                    var footerRect=footer.rectTransform; var parentRect=footerRect.parent as RectTransform;
                    entry.Logger.Log("FOOTER LAYOUT: rect="+footerRect.rect+" anchors="+footerRect.anchorMin+"/"+footerRect.anchorMax+" offset="+footerRect.offsetMin+"/"+footerRect.offsetMax+" parent="+parentRect.rect+" font="+footer.fontSize+" min/max="+footer.fontSizeMin+"/"+footer.fontSizeMax+" autosize="+footer.enableAutoSizing+" margin="+footer.margin+" overflow="+footer.overflowMode);
                    handler.AddTooltipAndUpdate(nativeTooltip);
                    string modelHelp=footer.text;
                    footer.ForceMeshUpdate(); if(footer.isTextTruncated) throw new Exception("Model description clipped in native footer");
                    if(modelHelp.Split('\n').Length!=2 || (!modelHelp.Contains("标准：") && !modelHelp.Contains("Standard:")) || (!modelHelp.Contains("性能：") && !modelHelp.Contains("Performance:")) || modelHelp.Contains("重启") || modelHelp.Contains("Restart")) throw new Exception("Live model description incorrect: "+modelHelp);
                    handler.RemoveTooltipAndUpdate(nativeTooltip);
                    foreach(var control in new GameObject[]{rows[0].gameObject,rows[1].gameObject,rows[2].gameObject,rows[3].gameObject,rows[6].gameObject,rows[8].gameObject,sharpness.gameObject}) {
                        var tooltip=control.GetComponent<UIElementTooltip>();
                        if(!tooltip || !tooltip.enabled) throw new Exception("Native tooltip missing: "+control.name);
                        handler.AddTooltipAndUpdate(tooltip);
                        footer.ForceMeshUpdate(); if(footer.isTextTruncated) throw new Exception("Description clipped: "+control.name);
                        if(string.IsNullOrEmpty(footer.text) || footer.text=="更改图形质量。" || footer.text=="Change graphics quality.") throw new Exception("Generic/missing footer: "+control.name);
                        if(control==rows[6].gameObject || control==sharpness.gameObject) {
                            if((!footer.text.Contains("渲染 / 输出") && !footer.text.Contains("rendering / output")) || footer.text.Contains("即时生效") || footer.text.Contains("without restarting")) throw new Exception("FSR footer incorrect: "+footer.text);
                        }
                        handler.RemoveTooltipAndUpdate(tooltip);
                    }
                    if(rows[1].GetLabelTMPro().text!="帧生成倍率" && rows[1].GetLabelTMPro().text!="Frame generation multiplier") throw new Exception("Multiplier label incorrect");
                    if(rows[2].GetLabelTMPro().text!="帧生成模型" && rows[2].GetLabelTMPro().text!="Frame generation model") throw new Exception("Model label incorrect");
                    if(rows[1].GetComponent<UIElementTooltip>().GetText().Contains("短暂停顿") || rows[1].GetComponent<UIElementTooltip>().GetText().Contains("without restarting")) throw new Exception("Removed multiplier note remains");
                    entry.Logger.Log("DESCRIPTION PASS: native footer, two-line live model help, generation labels, LSFG status, FSR rendering/output, no redundant apply timing notes");
                    var settingsFile=Path.GetFullPath(Path.Combine(entry.Path,"..","dv-scaling","config.ini"));
                    var previous=File.ReadAllText(settingsFile);
                    try {
                        int old=rows[1].SelectedIndex; int next=(old+1)%3;
                        rows[1].SetSelectedIndex(next,true);
                        if(!controller.HasChanges) throw new Exception("Pending change not detected");
                        var discard=(Button)AccessTools.Field(typeof(SettingsController),"revertChanges").GetValue(controller);
                        discard.onClick.Invoke();
                        if(rows[1].SelectedIndex!=old || File.ReadAllText(settingsFile)!=previous) throw new Exception("Discard did not restore multiplier");
                        rows[1].SetSelectedIndex(next,true);
                        var apply=(Button)AccessTools.Field(typeof(SettingsController),"applyChanges").GetValue(controller);
                        apply.onClick.Invoke();
                        if(Settings.Read(settingsFile).Multiplier!=next+2) throw new Exception("Native Apply did not persist multiplier");
                        if(!(bool)AccessTools.Property(typeof(Main),"NativeLoaded").GetValue(null,null)) throw new Exception("Live test requires loaded native layer");
                        entry.Logger.Log("APPLY/DISCARD PASS: native buttons, pending state and persistence");
                        rows[1].SetSelectedIndex(old,true); apply.onClick.Invoke();
                        int oldEnabled=rows[0].SelectedIndex;
                        rows[0].SetSelectedIndex(1-oldEnabled,true); apply.onClick.Invoke();
                        if(Settings.Read(settingsFile).Enabled!=(oldEnabled==0)) throw new Exception("Native LSFG toggle did not persist");
                        rows[0].SetSelectedIndex(oldEnabled,true); apply.onClick.Invoke();
                        entry.Logger.Log("TOGGLE/STATUS PASS: independent LSFG enablement and live multiplier request");
                        int oldFsr=rows[6].SelectedIndex; int oldQuality=rows[8].SelectedIndex;
                        rows[6].SetSelectedIndex(1-oldFsr,true);
                        if(!controller.HasChanges) throw new Exception("FSR change not detected");
                        discard.onClick.Invoke();
                        if(rows[6].SelectedIndex!=oldFsr) throw new Exception("FSR discard failed");
                        rows[6].SetSelectedIndex(0,true); apply.onClick.Invoke();
                        rows[8].SetSelectedIndex((oldQuality+1)%5,true); apply.onClick.Invoke();
                        var offSettings=Settings.Read(settingsFile);
                        if(offSettings.FsrMode!=0 || offSettings.FsrQuality!=5-(oldQuality+1)%5) throw new Exception("Quality not retained while technology is off");
                        rows[6].SetSelectedIndex(1,true); apply.onClick.Invoke();
                        if(Settings.Read(settingsFile).FsrMode!=offSettings.FsrQuality) throw new Exception("Technology enable did not restore selected quality");
                        foreach(int quality in new[]{0,1,2,3,4}) {rows[8].SetSelectedIndex(quality,true); apply.onClick.Invoke(); if(Settings.Read(settingsFile).FsrMode!=5-quality) throw new Exception("Quality mode mapping incorrect");}
                        rows[8].SetSelectedIndex(oldQuality,true); apply.onClick.Invoke();
                        rows[6].SetSelectedIndex(oldFsr,true); apply.onClick.Invoke();
                        entry.Logger.Log("FSR APPLY/DISCARD PASS");
                        float oldSharp=sharpness.value;
                        sharpness.value=37;
                        if(!controller.HasChanges) throw new Exception("Slider change not detected");
                        var readout=sharpness.transform.Find("[texts]/[text value] [noloc]").GetComponent<TMPro.TextMeshProUGUI>();
                        if(readout.text!="37%") throw new Exception("Percentage readout incorrect: "+readout.text);
                        discard.onClick.Invoke();
                        if(sharpness.value!=oldSharp) throw new Exception("Slider discard failed");
                        foreach(int percent in new[]{0,37,100}) {
                            sharpness.value=percent; apply.onClick.Invoke();
                            if(Math.Abs(Settings.Read(settingsFile).FsrSharpness-percent/100f)>.00001f) throw new Exception("Slider apply failed at "+percent);
                            if(readout.text!=percent+"%") throw new Exception("Slider readout failed at "+percent);
                        }
                        sharpness.value=oldSharp; apply.onClick.Invoke();
                        entry.Logger.Log("FSR SLIDER PASS: 0%, 37%, 100%, percentage readout, Apply/Discard and restore");
                        foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(marker.categoryName=="Advanced") {
                            foreach(var image in marker.GetComponentsInChildren<Image>(true)) {
                                var rect=image.GetComponent<RectTransform>();
                                entry.Logger.Log("ADVANCED IMAGE: "+image.transform.parent.name+"/"+image.name+" size="+rect.sizeDelta+" scale="+rect.localScale+" parentScale="+rect.parent.localScale+" anchors="+rect.anchorMin+"/"+rect.anchorMax+" color="+image.color+" sprite="+(image.sprite?image.sprite.name:"none")+" type="+image.type+" fillCenter="+image.fillCenter);
                            }
                        }
                        var nativeScroll=rows[0].GetComponentInParent<ScrollRect>(); Canvas.ForceUpdateCanvases(); nativeScroll.verticalNormalizedPosition=1;
                    } finally { File.WriteAllText(settingsFile,previous,new System.Text.UTF8Encoding(false)); }
                    oldModel=rows[2].SelectedIndex; oldFlow=rows[3].SelectedIndex;
                    stage=6; liveStep=0; deadline=DateTime.UtcNow.AddSeconds(2);
                } else if(stage==6) {
                    var apply=(Button)AccessTools.Field(typeof(SettingsController),"applyChanges").GetValue(controller);
                    var runtime=(Settings)AccessTools.Field(typeof(Main),"runtimeSettings").GetValue(null);
                    if(liveStep==0) {
                        rows[2].SetSelectedIndex(1-oldModel,true); apply.onClick.Invoke();
                    } else if(liveStep==1) {
                        if(runtime.Performance!=(rows[2].SelectedIndex==1)) throw new Exception("Native Graphics model Apply did not take effect live");
                        rows[3].SetSelectedIndex((oldFlow+1)%4,true); apply.onClick.Invoke();
                    } else if(liveStep==2) {
                        if(runtime.FlowScale!=(rows[3].SelectedIndex+1)*.25f) throw new Exception("Native Graphics flow Apply did not take effect live");
                        rows[2].SetSelectedIndex(oldModel,true); rows[3].SetSelectedIndex(oldFlow,true); apply.onClick.Invoke();
                    } else {
                        if(runtime.Performance!=(oldModel==1) || runtime.FlowScale!=(oldFlow+1)*.25f) throw new Exception("Live settings restore failed");
                        entry.Logger.Log("LIVE SETTINGS APPLY PASS: model and flow applied through native Graphics Apply without restarting, original settings restored");
                        File.WriteAllText(Path.Combine(entry.Path,"passed.txt"),"Native Graphics descriptions, Apply/Discard and live model/flow passed");
                        foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(marker.categoryName=="Advanced") {
                            var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(controller);
                            AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
                        }
                        stage=3; deadline=DateTime.UtcNow.AddSeconds(3);
                        return;
                    }
                    liveStep++; deadline=DateTime.UtcNow.AddSeconds(3);
                } else if(stage==3) {
                    File.WriteAllText(Path.GetFullPath(Path.Combine(entry.Path,"..","dv-scaling","capture.request")),"capture");
                    File.WriteAllText(Path.Combine(entry.Path,"advanced.ready"),"Advanced reference capture requested");
                    stage=4;
                } else if(stage==4 && File.Exists(Path.Combine(entry.Path,"graphics.request"))) {
                    File.Delete(Path.Combine(entry.Path,"graphics.request"));
                    foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(marker.categoryName=="Graphics") {
                        var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(controller);
                        AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
                    }
                    stage=5; deadline=DateTime.UtcNow.AddSeconds(3);
                } else if(stage==5) {
                    File.WriteAllText(Path.GetFullPath(Path.Combine(entry.Path,"..","dv-scaling","capture.request")),"capture"); stage=-1;
                }
            } catch(Exception e) { entry.Logger.Error(e.ToString()); File.WriteAllText(Path.Combine(entry.Path,"failed.txt"),e.ToString()); stage=-1; }
        }
    }
}

using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
using DVLSFG;
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
                            if(list.Count!=5) throw new Exception("Expected 5 editable selectors plus FSR slider, found "+list.Count);
                            rows=new Selector[10]; foreach(var row in list) rows[int.Parse(row.name.Substring("DVLSFG Graphics ".Length))]=row;
                            foreach(var slider in marker.GetComponentsInChildren<SliderDV>(true)) if(slider.name=="DVLSFG Graphics 7") sharpness=slider;
                            if(!sharpness || sharpness.minValue!=0 || sharpness.maxValue!=100 || !sharpness.wholeNumbers) throw new Exception("FSR 0-100 percent slider absent");
                            var scroll=marker.GetComponent<ScrollRect>();
                            if(!scroll || !scroll.vertical || !scroll.verticalScrollbar) throw new Exception("Graphics scroll controls absent");
                            foreach(int removed in new[]{4,5,8,9}) if(rows[removed]) throw new Exception("Read-only status/prompt row remains: "+removed);
                            entry.Logger.Log("PREVIEW PASS: five editable selectors plus native FSR sharpening slider; no status/prompt controls");
                            stage=2; deadline=DateTime.UtcNow.AddSeconds(2); return;
                        }
                    }
                } else if(stage==2) {
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
                    foreach(var control in new GameObject[]{rows[0].gameObject,rows[1].gameObject,rows[2].gameObject,rows[3].gameObject,rows[6].gameObject,sharpness.gameObject}) {
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
                    var settingsFile=Path.GetFullPath(Path.Combine(entry.Path,"..","DVLSFG","config.ini"));
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
                        int oldFsr=rows[6].SelectedIndex; rows[6].SetSelectedIndex((oldFsr+1)%5,true);
                        if(!controller.HasChanges) throw new Exception("FSR change not detected");
                        discard.onClick.Invoke();
                        if(rows[6].SelectedIndex!=oldFsr) throw new Exception("FSR discard failed");
                        rows[6].SetSelectedIndex((oldFsr+1)%5,true); apply.onClick.Invoke();
                        int nextFsr=(oldFsr+1)%5;
                        if(Settings.Read(settingsFile).FsrMode!=(nextFsr==0 ? 0 : 5-nextFsr)) throw new Exception("FSR apply failed");
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
                    File.WriteAllText(Path.GetFullPath(Path.Combine(entry.Path,"..","DVLSFG","capture.request")),"capture");
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
                    File.WriteAllText(Path.GetFullPath(Path.Combine(entry.Path,"..","DVLSFG","capture.request")),"capture"); stage=-1;
                }
            } catch(Exception e) { entry.Logger.Error(e.ToString()); File.WriteAllText(Path.Combine(entry.Path,"failed.txt"),e.ToString()); stage=-1; }
        }
    }
}

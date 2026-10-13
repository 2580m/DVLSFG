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
        static SettingsController controller;
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
                            rows=list.ToArray();
                            if(rows.Length!=6) throw new Exception("Expected 6 LSFG graphics rows, found "+rows.Length);
                            var scroll=marker.GetComponent<ScrollRect>();
                            if(!scroll || !scroll.vertical || !scroll.verticalScrollbar) throw new Exception("Graphics scroll controls absent");
                            entry.Logger.Log("PREVIEW PASS: native Graphics menu contains six LSFG selectors");
                            stage=2; deadline=DateTime.UtcNow.AddSeconds(2); return;
                        }
                    }
                } else if(stage==2) {
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
                        string status=rows[4].GetValues()[0];
                        if(status.IndexOf("重启",StringComparison.Ordinal)<0 && status.IndexOf("Restart",StringComparison.Ordinal)<0 && status.IndexOf("启动",StringComparison.Ordinal)<0 && status.IndexOf("launch",StringComparison.Ordinal)<0)
                            throw new Exception("Changed multiplier did not indicate restart or launch requirement: "+status);
                        entry.Logger.Log("APPLY/DISCARD PASS: native buttons, pending state and persistence");
                        rows[1].SetSelectedIndex(old,true); apply.onClick.Invoke();
                        int oldEnabled=rows[0].SelectedIndex;
                        rows[0].SetSelectedIndex(1-oldEnabled,true); apply.onClick.Invoke();
                        if(Settings.Read(settingsFile).Enabled!=(oldEnabled==0)) throw new Exception("Native LSFG toggle did not persist");
                        rows[0].SetSelectedIndex(oldEnabled,true); apply.onClick.Invoke();
                        entry.Logger.Log("TOGGLE/STATUS PASS: independent LSFG enablement and restart indication");
                        foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(marker.categoryName=="Advanced") {
                            foreach(var image in marker.GetComponentsInChildren<Image>(true)) {
                                var rect=image.GetComponent<RectTransform>();
                                entry.Logger.Log("ADVANCED IMAGE: "+image.transform.parent.name+"/"+image.name+" size="+rect.sizeDelta+" scale="+rect.localScale+" parentScale="+rect.parent.localScale+" anchors="+rect.anchorMin+"/"+rect.anchorMax+" color="+image.color+" sprite="+(image.sprite?image.sprite.name:"none")+" type="+image.type+" fillCenter="+image.fillCenter);
                            }
                        }
                        var nativeScroll=rows[0].GetComponentInParent<ScrollRect>(); Canvas.ForceUpdateCanvases(); nativeScroll.verticalNormalizedPosition=1;
                    } finally { File.WriteAllText(settingsFile,previous,new System.Text.UTF8Encoding(false)); }
                    File.WriteAllText(Path.Combine(entry.Path,"passed.txt"),"Native Graphics preview and Apply/Discard passed");
                    foreach(var marker in controller.GetComponentsInChildren<SettingsCategoryMarker>(true)) if(marker.categoryName=="Advanced") {
                        var menus=(UIMenuController)AccessTools.Field(typeof(SettingsController),"menuController").GetValue(controller);
                        AccessTools.Method(typeof(UIMenuController),"OpenMenu").Invoke(menus,new object[]{marker.GetComponent<UIMenu>()});
                    }
                    stage=3; deadline=DateTime.UtcNow.AddSeconds(3);
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

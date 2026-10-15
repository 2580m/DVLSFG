using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DV.UI;
using DV.UIFramework;
using DVLSFG;
namespace DVLSFGTests {
    public static class SettingsApplySmoke {
        public static bool Load(UnityModManager.ModEntry entry) {
            var go=new GameObject("Settings apply regression"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SettingsApplyRunner>().Entry=entry; return true;
        }
    }
    public sealed class SettingsApplyRunner : MonoBehaviour {
        public UnityModManager.ModEntry Entry;
        void Log(string message) {Entry.Logger.Log(message); File.AppendAllText(Path.Combine(Entry.Path,"result.log"),message+"\n");}
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(12);
            try {
                var uiType=typeof(UnityModManager).GetNestedType("UI",BindingFlags.Public|BindingFlags.NonPublic);
                AccessTools.Method(uiType,"ToggleWindow",new[]{typeof(bool)}).Invoke(AccessTools.Field(uiType,"mInstance").GetValue(null),new object[]{false});
                foreach(var menu in Resources.FindObjectsOfTypeAll<MainMenuController>()) if(menu.gameObject.activeInHierarchy) {
                    ((ButtonDV)AccessTools.Field(typeof(MainMenuController),"settingsButton").GetValue(menu)).Click(); break;
                }
            } catch(Exception error) {Fail(error); yield break;}
            yield return new WaitForSecondsRealtime(2);
            try {
                SettingsController controller=null;
                foreach(var candidate in Resources.FindObjectsOfTypeAll<SettingsController>()) if(candidate.gameObject.activeInHierarchy) {controller=candidate; break;}
                if(!controller) throw new Exception("Settings screen unavailable");
                int checkedTypes=0;
                foreach(var type in new[]{typeof(SettingChangeSourceCheckbox),typeof(SettingChangeSourceSelector),typeof(SettingChangeSourceSlider)}) {
                    var template=controller.GetComponentInChildren(type,true) as Component;
                    if(!template) throw new Exception("Missing template "+type.Name);
                    var clone=Instantiate(template,template.transform.parent,false);
                    // Activate to subscribe to the real provider, then leave a
                    // destroyed callback target just like a discarded screen.
                    clone.gameObject.SetActive(true);
                    DestroyImmediate(clone.gameObject);
                    AccessTools.Method(type,"OnResetOrApplied").Invoke(clone,null);
                    Log("PASS destroyed callback "+type.Name); checkedTypes++;
                }
                var panel=controller.GetComponent<GraphicsSettingsPanel>();
                var pending=(Settings)AccessTools.Field(typeof(GraphicsSettingsPanel),"pending").GetValue(panel);
                float original=pending.FsrSharpness;
                var apply=(Button)AccessTools.Field(typeof(SettingsController),"applyChanges").GetValue(controller);
                var revert=(Button)AccessTools.Field(typeof(SettingsController),"revertChanges").GetValue(controller);
                try {
                    pending.FsrSharpness=original>.5f ? .37f : .75f;
                    if(!controller.HasChanges) throw new Exception("Pending change was not detected");
                    apply.onClick.Invoke();
                    if((bool)AccessTools.Property(typeof(GraphicsSettingsPanel),"Dirty").GetValue(panel,null)) throw new Exception("Mod remains dirty after Apply");
                    if(controller.HasChanges) throw new Exception("Native settings remain dirty after Apply");
                    if(Math.Abs(Settings.Read(Path.Combine(Entry.Path,"..","dv-scaling","config.ini")).FsrSharpness-pending.FsrSharpness)>.00001f) throw new Exception("Settings were not persisted");
                    Log("PASS native Apply with destroyed callbacks: saved settings and HasChanges=false");
                    pending=(Settings)AccessTools.Field(typeof(GraphicsSettingsPanel),"pending").GetValue(panel);
                    pending.FsrSharpness=original; revert.onClick.Invoke();
                    if(controller.HasChanges) throw new Exception("Settings remain dirty after Revert");
                    Log("PASS Revert with destroyed callbacks");
                } finally {
                    pending=(Settings)AccessTools.Field(typeof(GraphicsSettingsPanel),"pending").GetValue(panel);
                    pending.FsrSharpness=original; apply.onClick.Invoke();
                }
                File.WriteAllText(Path.Combine(Entry.Path,"passed.txt"),"Destroyed checkbox/selector/slider callbacks, Apply, Revert, persistence and clean state passed: "+checkedTypes);
            } catch(Exception error) {Fail(error);}
        }
        void Fail(Exception error) {Log("FAIL "+error); File.WriteAllText(Path.Combine(Entry.Path,"failed.txt"),error.ToString());}
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityModManagerNet;
using DVLSFG;
namespace DVLSFGTests {
    internal static class UiCaptureCacheSmoke {
        internal static bool Passed;
        internal static IEnumerator Run(UnityModManager.ModEntry entry) {
            var type=typeof(SceneUiCapture).Assembly.GetType("DVLSFG.UiCaptureCache",true);
            var cache=Activator.CreateInstance(type,true);
            var evaluate=AccessTools.Method(type,"NeedsRedraw");
            var captured=AccessTools.Method(type,"Captured");
            var clear=AccessTools.Method(type,"Clear");
            var root=new GameObject("Temporary UI invalidation test",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup));
            var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var panel=new GameObject("Test panel",typeof(RectTransform),typeof(Image)); panel.transform.SetParent(root.transform,false);
            var image=panel.GetComponent<Image>(); image.rectTransform.sizeDelta=new Vector2(50,50);
            var registry=new Dictionary<Canvas,List<Graphic>> {{canvas,new List<Graphic>{image}}};
            var roots=new HashSet<Canvas>{canvas};
            var material=new Material(Shader.Find("UI/Default"));
            var texture=new Texture2D(2,2); var target=new RenderTexture(2,2,0); target.Create();
            Func<bool> needs=()=> (bool)evaluate.Invoke(cache,new object[]{registry,roots});
            Action<string> dirty=label=> {Canvas.ForceUpdateCanvases(); if(!needs()) throw new Exception("Missed UI invalidation: "+label); captured.Invoke(cache,null); if(needs()) throw new Exception("Unstable UI snapshot: "+label);};
            string failure=null;
            yield return null;
            try {
                Canvas.ForceUpdateCanvases();
                dirty("initial capture");
                image.color=Color.magenta; dirty("vertex color");
                image.rectTransform.anchoredPosition+=new Vector2(1,2); dirty("position");
                image.rectTransform.sizeDelta+=Vector2.one; dirty("size");
                image.canvasRenderer.SetColor(Color.red); dirty("renderer tint");
                root.GetComponent<CanvasGroup>().alpha=.5f; Canvas.ForceUpdateCanvases(); dirty("CanvasGroup opacity");
                image.material=material; dirty("material replacement");
                material.SetFloat("_Stencil",3); dirty("in-place material property");
                var rawObject=new GameObject("Test dynamic texture",typeof(RectTransform),typeof(RawImage)); rawObject.transform.SetParent(root.transform,false);
                var raw=rawObject.GetComponent<RawImage>(); raw.texture=texture; registry[canvas].Add(raw);
                dirty("new graphic and texture");
                texture.SetPixel(0,0,Color.green); texture.Apply(); dirty("texture upload");
                texture.filterMode=FilterMode.Point; dirty("texture filtering");
                texture.wrapMode=TextureWrapMode.Clamp; dirty("texture addressing");
                var textObject=new GameObject("Test text",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI)); textObject.transform.SetParent(root.transform,false);
                var text=textObject.GetComponent<TMPro.TextMeshProUGUI>();
                text.overflowMode=TMPro.TextOverflowModes.Overflow; text.text="Cache 1"; registry[canvas].Add(text); dirty("new text");
                text.text="Cache 2"; dirty("text edit");
                var nestedObject=new GameObject("Test nested canvas",typeof(RectTransform),typeof(Canvas),typeof(Image)); nestedObject.transform.SetParent(root.transform,false);
                var nested=nestedObject.GetComponent<Canvas>(); nested.overrideSorting=true;
                registry[nested]=new List<Graphic>{nestedObject.GetComponent<Image>()}; dirty("nested canvas");
                nested.sortingOrder=12; dirty("nested sorting order");
                raw.texture=target;
                if(!needs()) throw new Exception("RenderTexture did not invalidate"); captured.Invoke(cache,null);
                if(!needs()) throw new Exception("RenderTexture was incorrectly cached");
                raw.texture=texture; dirty("return to static texture");
                image.canvasRenderer.EnableRectClipping(new Rect(0,0,100,100));
                if(!needs()) throw new Exception("Clipped UI did not invalidate"); captured.Invoke(cache,null);
                if(!needs()) throw new Exception("Clipped UI was incorrectly cached");
                image.canvasRenderer.DisableRectClipping(); dirty("remove clipping");
                panel.SetActive(false); dirty("deactivation");
                panel.SetActive(true); dirty("reactivation");
                registry[canvas].Remove(raw); UnityEngine.Object.DestroyImmediate(rawObject); dirty("remove graphic");
                clear.Invoke(cache,null); captured.Invoke(cache,null);
                image.SetVerticesDirty();
                if((bool)AccessTools.Field(type,"dirty").GetValue(cache)) throw new Exception("Dirty callbacks leaked after clear");
                entry.Logger.Log("PASS UI cache stable reuse, geometry/tint/opacity, material mutation, texture upload, dynamic/clipped fallback, activation and callback cleanup");
            } catch(Exception error) {failure=error.ToString(); entry.Logger.Error(failure);}
            finally {
                clear.Invoke(cache,null); UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target);
            }
            Passed=failure==null;
            File.WriteAllText(Path.Combine(entry.Path,Passed ? "ui-cache-passed.txt" : "ui-cache-failed.txt"),failure ?? "UI cache invalidation checks passed");
        }
    }
}

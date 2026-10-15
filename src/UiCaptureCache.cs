using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DVLSFG {
    // Reuse only the UI surfaces, never scene/depth/motion or the complete-frame
    // reference. Sample after Unity rendered its overlay so native UI changes
    // invalidate the capture in the same frame.
    internal sealed class UiCaptureCache {
        sealed class GraphicState {
            internal int Epoch;
            internal Matrix4x4 Matrix;
            internal Rect Rect;
            internal Color Color;
            internal float Alpha;
            internal int Depth;
            internal bool Culled;
            internal bool Clipped;
            internal int PopMaterials;
            internal ulong Materials;
        }
        struct TransformState {
            internal Transform Transform;
            internal Matrix4x4 Matrix;
            internal Rect Rect;
        }
        sealed class MaterialState {internal Shader Shader; internal int[] Textures; internal bool Stable;}
        readonly Dictionary<Graphic,GraphicState> graphics=new Dictionary<Graphic,GraphicState>();
        readonly Dictionary<Material,MaterialState> materials=new Dictionary<Material,MaterialState>();
        readonly Dictionary<Material,ulong> frameMaterials=new Dictionary<Material,ulong>();
        readonly Dictionary<Texture,ulong> frameTextures=new Dictionary<Texture,ulong>();
        readonly List<Graphic> removed=new List<Graphic>();
        readonly List<Material> unusedMaterials=new List<Material>();
        readonly List<Transform> children=new List<Transform>();
        readonly List<TransformState> transforms=new List<TransformState>();
        readonly UnityAction changed;
        bool dirty=true;
        int epoch;
        ulong canvasStamp;
        internal bool Capturing;
        internal UiCaptureCache() {changed=Invalidate;}
        internal void Invalidate() {if(!Capturing) dirty=true;}
        static ulong Mix(ulong value,int part) {return (value^(uint)part)*1099511628211UL;}
        ulong TextureStamp(Texture texture,ref bool reusable) {
            if(!texture) return 0;
            if(texture is RenderTexture) reusable=false;
            ulong stamp;
            if(frameTextures.TryGetValue(texture,out stamp)) return stamp;
            stamp=Mix(Mix(1469598103934665603UL,texture.GetInstanceID()),unchecked((int)texture.updateCount));
            stamp=Mix(Mix(stamp,texture.width),texture.height);
            stamp=Mix(Mix(stamp,(int)texture.filterMode),texture.anisoLevel);
            stamp=Mix(Mix(stamp,(int)texture.wrapModeU),(int)texture.wrapModeV);
            frameTextures[texture]=stamp; return stamp;
        }
        static ulong MixStamp(ulong stamp,ulong part) {return Mix(Mix(stamp,unchecked((int)part)),unchecked((int)(part>>32)));}
        static ulong CanvasStamp(ulong stamp,Canvas canvas) {
            stamp=Mix(stamp,canvas.GetInstanceID()); stamp=Mix(stamp,canvas.sortingLayerID); stamp=Mix(stamp,canvas.sortingOrder);
            stamp=Mix(stamp,canvas.renderOrder); stamp=Mix(stamp,canvas.overrideSorting ? 1 : 0); stamp=Mix(stamp,canvas.pixelPerfect ? 1 : 0);
            stamp=Mix(stamp,canvas.scaleFactor.GetHashCode()); return Mix(stamp,canvas.referencePixelsPerUnit.GetHashCode());
        }
        static bool StableShader(string name) {
            switch(name) {
                case "UI/Default":
                case "TextMeshPro/Distance Field":
                case "TextMeshPro/Distance Field Overlay":
                case "TextMeshPro/Distance Field SSD":
                case "TextMeshPro/Mobile/Distance Field":
                case "TextMeshPro/Mobile/Distance Field Overlay":
                case "TextMeshPro/Mobile/Distance Field SSD":
                case "TextMeshPro/Bitmap":
                case "TextMeshPro/Mobile/Bitmap":
                case "TextMeshPro/Sprite": return true;
                default: return false;
            }
        }
        ulong MaterialStamp(Material material,ref bool reusable) {
            if(!material) return 0;
            ulong result;
            if(frameMaterials.TryGetValue(material,out result)) return result;
            var shader=material.shader;
            MaterialState state;
            if(!materials.TryGetValue(material,out state) || state.Shader!=shader) {
                state=new MaterialState {Shader=shader,Textures=material.GetTexturePropertyNameIDs(),Stable=StableShader(shader ? shader.name : "")}; materials[material]=state;
            }
            // Custom shaders may use time, global properties or external buffers
            // without dirtying their Graphic. Keep their original capture path.
            if(!state.Stable) reusable=false;
            if(material.HasProperty("_FaceUVSpeedX") && (material.GetFloat("_FaceUVSpeedX")!=0 || material.GetFloat("_FaceUVSpeedY")!=0)) reusable=false;
            if(material.HasProperty("_OutlineUVSpeedX") && (material.GetFloat("_OutlineUVSpeedX")!=0 || material.GetFloat("_OutlineUVSpeedY")!=0)) reusable=false;
            result=Mix(Mix(Mix(1469598103934665603UL,material.GetInstanceID()),shader ? shader.GetInstanceID() : 0),material.ComputeCRC());
            foreach(int property in state.Textures) {
                var texture=material.GetTexture(property);
                result=MixStamp(result,TextureStamp(texture,ref reusable));
            }
            frameMaterials[material]=result;
            return result;
        }
        internal bool NeedsRedraw(IDictionary registry,HashSet<Canvas> roots) {
            ++epoch; frameMaterials.Clear(); frameTextures.Clear();
            bool reusable=true;
            ulong nextStamp=1469598103934665603UL;
            foreach(DictionaryEntry item in registry) {
                var canvas=item.Key as Canvas;
                if(!canvas || !canvas.isActiveAndEnabled || !roots.Contains(canvas.rootCanvas)) continue;
                nextStamp=CanvasStamp(nextStamp,canvas);
                // Unity's IndexedSet intentionally throws from both enumerator
                // interfaces; its IList indexer is the supported traversal.
                var registered=(IList<Graphic>)item.Value;
                for(int graphicIndex=0;graphicIndex<registered.Count;++graphicIndex) {
                    var graphic=registered[graphicIndex];
                    if(!graphic || !graphic.isActiveAndEnabled) continue;
                    GraphicState state;
                    if(!graphics.TryGetValue(graphic,out state)) {
                        state=new GraphicState(); graphics.Add(graphic,state); dirty=true;
                        graphic.RegisterDirtyVerticesCallback(changed); graphic.RegisterDirtyMaterialCallback(changed); graphic.RegisterDirtyLayoutCallback(changed);
                    }
                    state.Epoch=epoch;
                    var renderer=graphic.canvasRenderer;
                    var matrix=graphic.transform.localToWorldMatrix;
                    var rect=graphic.rectTransform.rect;
                    var color=renderer.GetColor(); float alpha=renderer.GetInheritedAlpha();
                    int depth=renderer.absoluteDepth; bool culled=renderer.cull; bool clipped=renderer.hasRectClipping; int pop=renderer.popMaterialCount;
                    ulong stamp=1469598103934665603UL;
                    if(!culled) {
                        var type=graphic.GetType();
                        if(type!=typeof(Image) && type!=typeof(RawImage) && type!=typeof(Text) && type!=typeof(TMPro.TextMeshProUGUI) && type!=typeof(TMPro.TMP_SubMeshUI)) reusable=false;
                        // Rect/stencil masks can update renderer-only clipping
                        // state. Conservatively redraw masked UI every frame.
                        if(clipped || pop!=0) reusable=false;
                        for(int i=0;i<renderer.materialCount;++i) {
                            ulong value=MaterialStamp(renderer.GetMaterial(i),ref reusable);
                            stamp=MixStamp(stamp,value);
                        }
                        var texture=graphic.mainTexture;
                        stamp=MixStamp(stamp,TextureStamp(texture,ref reusable));
                    }
                    if(!state.Matrix.Equals(matrix) || !state.Rect.Equals(rect) || !state.Color.Equals(color) || state.Alpha!=alpha || state.Depth!=depth || state.Culled!=culled || state.Clipped!=clipped || state.PopMaterials!=pop || state.Materials!=stamp) dirty=true;
                    state.Matrix=matrix; state.Rect=rect; state.Color=color; state.Alpha=alpha; state.Depth=depth; state.Culled=culled; state.Clipped=clipped; state.PopMaterials=pop; state.Materials=stamp;
                }
            }
            removed.Clear();
            foreach(var item in graphics) if(item.Value.Epoch!=epoch) removed.Add(item.Key);
            foreach(var graphic in removed) {Unregister(graphic); graphics.Remove(graphic); dirty=true;}
            unusedMaterials.Clear();
            foreach(var material in materials.Keys) if(!frameMaterials.ContainsKey(material)) unusedMaterials.Add(material);
            foreach(var material in unusedMaterials) materials.Remove(material);
            int index=0;
            foreach(var canvas in roots) {
                nextStamp=CanvasStamp(nextStamp,canvas);
                children.Clear(); canvas.GetComponentsInChildren<Transform>(false,children);
                foreach(var child in children) {
                    var rect=child as RectTransform;
                    var current=new TransformState {Transform=child,Matrix=child.localToWorldMatrix,Rect=rect ? rect.rect : default(Rect)};
                    if(index>=transforms.Count) {transforms.Add(current); dirty=true;}
                    else {var previous=transforms[index]; if(previous.Transform!=child || !previous.Matrix.Equals(current.Matrix) || !previous.Rect.Equals(current.Rect)) dirty=true; transforms[index]=current;}
                    ++index;
                }
            }
            if(index!=transforms.Count) {transforms.RemoveRange(index,transforms.Count-index); dirty=true;}
            if(canvasStamp!=nextStamp) dirty=true;
            canvasStamp=nextStamp;
            return dirty || !reusable;
        }
        internal void Captured() {dirty=false;}
        void Unregister(Graphic graphic) {
            if(!graphic) return;
            graphic.UnregisterDirtyVerticesCallback(changed); graphic.UnregisterDirtyMaterialCallback(changed); graphic.UnregisterDirtyLayoutCallback(changed);
        }
        internal void Clear() {
            foreach(var graphic in graphics.Keys) Unregister(graphic);
            graphics.Clear(); materials.Clear(); frameMaterials.Clear(); frameTextures.Clear(); transforms.Clear(); children.Clear(); removed.Clear(); unusedMaterials.Clear(); dirty=true; Capturing=false;
        }
    }
}

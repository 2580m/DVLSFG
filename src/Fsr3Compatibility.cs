using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
namespace FidelityFX.FSR3 {
    internal static class Fsr3Compatibility {
        static readonly Dictionary<ComputeShader,int[]> bindings=new Dictionary<ComputeShader,int[]>();
        static readonly int[] empty=new int[0];
        internal static void Forget(ComputeShader shader) {if(shader) bindings.Remove(shader);}
        internal static void BindScratchUavs(CommandBuffer commands,ComputeShader shader,Dictionary<int,RenderTexture> aliases) {
            int[] targets;
            if(!bindings.TryGetValue(shader,out targets)) {
            string name=shader.name;
            if(name.StartsWith("ffx_fsr3upscaler_luma_pyramid_pass",StringComparison.Ordinal)) targets=new[]{Fsr3ShaderIDs.UavFarthestDepthMip1};
            else if(name.StartsWith("ffx_fsr3upscaler_shading_change_pass",StringComparison.Ordinal)) targets=new[]{Fsr3ShaderIDs.UavShadingChange};
            else if(name.StartsWith("ffx_fsr3upscaler_prepare_reactivity_pass",StringComparison.Ordinal)) targets=new[]{Fsr3ShaderIDs.UavDilatedReactiveMasks,Fsr3ShaderIDs.UavNewLocks};
            else if(name.StartsWith("ffx_fsr3upscaler_accumulate_pass",StringComparison.Ordinal)) targets=new[]{Fsr3ShaderIDs.UavNewLocks};
            else targets=empty;
            bindings[shader]=targets;
            }
            foreach(int target in targets) {
                commands.SetComputeTextureParam(shader,shader.FindKernel("CS"),target,aliases[target]);
                if(shader.HasKernel("CS_SHARPEN")) commands.SetComputeTextureParam(shader,shader.FindKernel("CS_SHARPEN"),target,aliases[target]);
            }
        }
    }
    internal partial class Fsr3UpscalerResources {
        // Unity 2019 cannot reliably bind temporary RT identifiers as compute
        // UAVs. Allocate the five scratch surfaces once per context and bind
        // their Texture objects directly. They are still overwritten each frame.
        internal readonly Dictionary<int,RenderTexture> Aliases=new Dictionary<int,RenderTexture>();
        void Scratch(int id,Vector2Int size,GraphicsFormat format) {
            var texture=new RenderTexture(size.x,size.y,0,format) {enableRandomWrite=true,name="FSR3 scratch "+id};
            if(!texture.Create()) {UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("FSR3 scratch allocation failed");}
            Aliases[id]=texture;
        }
        public void CreateAliasableResources(CommandBuffer commands,Fsr3Upscaler.ContextDescription context,Fsr3Upscaler.DispatchDescription dispatch) {
            if(Aliases.Count!=0) return;
            Scratch(Fsr3ShaderIDs.UavIntermediate,context.MaxRenderSize,GraphicsFormat.R16_SFloat);
            Scratch(Fsr3ShaderIDs.UavShadingChange,context.MaxRenderSize/2,GraphicsFormat.R8_UNorm);
            Scratch(Fsr3ShaderIDs.UavNewLocks,context.MaxUpscaleSize,GraphicsFormat.R8_UNorm);
            Scratch(Fsr3ShaderIDs.UavFarthestDepthMip1,context.MaxRenderSize/2,GraphicsFormat.R16_SFloat);
            Scratch(Fsr3ShaderIDs.UavDilatedReactiveMasks,context.MaxRenderSize,GraphicsFormat.R8G8B8A8_UNorm);
        }
        public void DestroyAliasableResources(CommandBuffer commands) {}
        internal void DisposeAliases() {
            foreach(var texture in Aliases.Values) {texture.Release(); UnityEngine.Object.Destroy(texture);}
            Aliases.Clear();
        }
    }
}

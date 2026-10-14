// AMD FSR 1.0, full precision for Unity's D3D11 renderer (including DXVK).
#define A_GPU 1
#define A_HLSL 1
#include "ffx_a.h"
Texture2D<float4> Input : register(t0);
SamplerState ClampSampler : register(s0);
RWTexture2D<float4> Output : register(u0);
cbuffer Parameters : register(b0) {
    uint4 Con0, Con1, Con2, Con3;
    uint2 OutputSize;
    uint2 Padding;
};
#if FSR_EASU
#define FSR_EASU_F 1
float4 FsrEasuRF(float2 p) { return Input.GatherRed(ClampSampler,p); }
float4 FsrEasuGF(float2 p) { return Input.GatherGreen(ClampSampler,p); }
float4 FsrEasuBF(float2 p) { return Input.GatherBlue(ClampSampler,p); }
#else
#define FSR_RCAS_F 1
float4 FsrRcasLoadF(int2 p) { return Input.Load(int3(clamp(p,int2(0,0),int2(OutputSize)-1),0)); }
void FsrRcasInputF(inout float r,inout float g,inout float b) {}
#endif
#include "ffx_fsr1.h"
[numthreads(8,8,1)]
void main(uint3 id : SV_DispatchThreadID) {
    if(any(id.xy>=OutputSize)) return;
    float3 color;
#if FSR_EASU
    FsrEasuF(color,id.xy,Con0,Con1,Con2,Con3);
#else
    FsrRcasF(color.r,color.g,color.b,id.xy,Con0);
#endif
    Output[id.xy]=float4(color,1);
}

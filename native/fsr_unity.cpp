#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <atomic>
#include <cstring>
#include <mutex>
#include "fsr_easu.h"
#include "fsr_rcas.h"
#define A_CPU 1
#include "ffx_a.h"
#include "ffx_fsr1.h"
using Microsoft::WRL::ComPtr;
struct Constants { uint32_t con[4][4]; uint32_t width,height,padding[2]; };
struct FsrJob {
    ComPtr<ID3D11Texture2D> input,output,intermediate;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11ComputeShader> easu,rcas;
    ComPtr<ID3D11ShaderResourceView> inputView,intermediateView;
    ComPtr<ID3D11UnorderedAccessView> intermediateUav,outputUav;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11SamplerState> sampler;
    std::atomic<int> status{0}; // 0 pending; 1 successfully dispatched; HRESULT on failure.
    std::atomic<uint64_t> frames{0};
    std::atomic<float> sharpness{.2f};
    Constants upscale{},sharpen{};
    HRESULT initialize() {
        input->GetDevice(&device);
        D3D11_TEXTURE2D_DESC in{},out{}; input->GetDesc(&in); output->GetDesc(&out);
        ComPtr<ID3D11Device> outputDevice; output->GetDevice(&outputDevice);
        if(device.Get()!=outputDevice.Get() || in.SampleDesc.Count!=1 || out.SampleDesc.Count!=1 ||
           in.Width>out.Width || in.Height>out.Height ||
           (in.Format!=DXGI_FORMAT_R8G8B8A8_TYPELESS && in.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && in.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) ||
           (out.Format!=DXGI_FORMAT_R8G8B8A8_TYPELESS && out.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && out.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)) return E_INVALIDARG;
        HRESULT hr;
#define CHECK(call) if(FAILED(hr=(call))) return hr
        CHECK(device->CreateComputeShader(g_fsr_easu,sizeof(g_fsr_easu),nullptr,&easu));
        CHECK(device->CreateComputeShader(g_fsr_rcas,sizeof(g_fsr_rcas),nullptr,&rcas));
        // Read stored display-encoded RGB directly; sRGB sampling would linearize it.
        D3D11_SHADER_RESOURCE_VIEW_DESC srv{}; srv.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        srv.ViewDimension=D3D11_SRV_DIMENSION_TEXTURE2D; srv.Texture2D.MipLevels=1;
        CHECK(device->CreateShaderResourceView(input.Get(),&srv,&inputView));
        auto mid=out; mid.Format=DXGI_FORMAT_R8G8B8A8_UNORM; mid.MipLevels=1; mid.ArraySize=1;
        mid.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_UNORDERED_ACCESS;
        mid.Usage=D3D11_USAGE_DEFAULT; mid.CPUAccessFlags=0; mid.MiscFlags=0;
        CHECK(device->CreateTexture2D(&mid,nullptr,&intermediate));
        CHECK(device->CreateShaderResourceView(intermediate.Get(),&srv,&intermediateView));
        D3D11_UNORDERED_ACCESS_VIEW_DESC uav{}; uav.Format=DXGI_FORMAT_R8G8B8A8_UNORM; uav.ViewDimension=D3D11_UAV_DIMENSION_TEXTURE2D;
        CHECK(device->CreateUnorderedAccessView(intermediate.Get(),&uav,&intermediateUav));
        CHECK(device->CreateUnorderedAccessView(output.Get(),&uav,&outputUav));
        D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth=sizeof(Constants); buffer.Usage=D3D11_USAGE_DEFAULT; buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        CHECK(device->CreateBuffer(&buffer,nullptr,&constants));
        D3D11_SAMPLER_DESC sample{}; sample.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sample.AddressU=sample.AddressV=sample.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP; sample.MaxLOD=D3D11_FLOAT32_MAX;
        CHECK(device->CreateSamplerState(&sample,&sampler));
        FsrEasuCon(upscale.con[0],upscale.con[1],upscale.con[2],upscale.con[3],float(in.Width),float(in.Height),float(in.Width),float(in.Height),float(out.Width),float(out.Height));
        upscale.width=sharpen.width=out.Width; upscale.height=sharpen.height=out.Height;
        return S_OK;
#undef CHECK
    }
    void render() {
        if(status.load()<0) return;
        if(!device) { HRESULT hr=initialize(); if(FAILED(hr)) {status=hr; return;} }
        ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
        // Unity caches its own D3D state. Restore every slot touched by this plugin.
        ComPtr<ID3D11ComputeShader> oldShader;
        ID3D11ClassInstance* classes[256]{}; UINT classCount=256;
        context->CSGetShader(&oldShader,classes,&classCount);
        ComPtr<ID3D11ShaderResourceView> oldSrv; context->CSGetShaderResources(0,1,&oldSrv);
        ComPtr<ID3D11UnorderedAccessView> oldUav; context->CSGetUnorderedAccessViews(0,1,&oldUav);
        ComPtr<ID3D11Buffer> oldBuffer; context->CSGetConstantBuffers(0,1,&oldBuffer);
        ComPtr<ID3D11SamplerState> oldSampler; context->CSGetSamplers(0,1,&oldSampler);
        ID3D11ShaderResourceView* noSrv=nullptr; ID3D11UnorderedAccessView* noUav=nullptr;
        context->CSSetShaderResources(0,1,&noSrv); context->CSSetUnorderedAccessViews(0,1,&noUav,nullptr);
        context->UpdateSubresource(constants.Get(),0,nullptr,&upscale,0,0);
        auto cb=constants.Get(); context->CSSetConstantBuffers(0,1,&cb);
        auto samp=sampler.Get(); context->CSSetSamplers(0,1,&samp);
        context->CSSetShader(easu.Get(),nullptr,0);
        auto srv=inputView.Get(); context->CSSetShaderResources(0,1,&srv);
        auto uav=intermediateUav.Get(); context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);
        context->Dispatch((upscale.width+7)/8,(upscale.height+7)/8,1);
        context->CSSetShaderResources(0,1,&noSrv); context->CSSetUnorderedAccessViews(0,1,&noUav,nullptr);
        // Public sharpness 0..1: AMD's RCAS parameter is attenuation in stops (0 strongest).
        float amount=sharpness.load();
        if(amount<=0) context->CopyResource(output.Get(),intermediate.Get());
        else {
            FsrRcasCon(sharpen.con[0],(1.f-amount)*2.f);
            context->UpdateSubresource(constants.Get(),0,nullptr,&sharpen,0,0);
            context->CSSetShader(rcas.Get(),nullptr,0);
            srv=intermediateView.Get(); context->CSSetShaderResources(0,1,&srv);
            uav=outputUav.Get(); context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);
            context->Dispatch((upscale.width+7)/8,(upscale.height+7)/8,1);
        }
        context->CSSetShaderResources(0,1,&noSrv); context->CSSetUnorderedAccessViews(0,1,&noUav,nullptr);
        srv=oldSrv.Get(); context->CSSetShaderResources(0,1,&srv);
        uav=oldUav.Get(); context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);
        cb=oldBuffer.Get(); context->CSSetConstantBuffers(0,1,&cb);
        samp=oldSampler.Get(); context->CSSetSamplers(0,1,&samp);
        context->CSSetShader(oldShader.Get(),classes,classCount);
        for(UINT i=0;i<classCount;i++) classes[i]->Release();
        ++frames; status=1;
    }
};
// UnityRenderingEventAndData ABI. Both render and disposal execute on Unity's render thread.
static void __stdcall RenderEvent(int event,void* data) {
    auto job=static_cast<FsrJob*>(data); if(!job) return;
    if(event==1) job->render(); else if(event==2) delete job;
}
extern "C" __declspec(dllexport) void* __cdecl DVFSR_GetRenderEvent() { return reinterpret_cast<void*>(&RenderEvent); }
extern "C" __declspec(dllexport) void* __cdecl DVFSR_Create(void* input,void* output,float sharpness) {
    if(!input || !output) return nullptr;
    auto job=new(std::nothrow) FsrJob; if(!job) return nullptr;
    job->input=static_cast<ID3D11Texture2D*>(input); job->output=static_cast<ID3D11Texture2D*>(output); job->sharpness=sharpness;
    return job;
}
extern "C" __declspec(dllexport) int __cdecl DVFSR_Status(void* handle,uint64_t* frames) {
    if(!handle) {if(frames) *frames=0; return E_POINTER;} auto job=static_cast<FsrJob*>(handle);
    if(frames) *frames=job->frames.load(); return job->status.load();
}
extern "C" __declspec(dllexport) void __cdecl DVFSR_Sharpness(void* handle,float value) {
    if(handle) static_cast<FsrJob*>(handle)->sharpness=value;
}

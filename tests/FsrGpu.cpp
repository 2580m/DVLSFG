#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <vector>
#include <iostream>
#include <cmath>
#include <stdexcept>
#include <string>
#include <algorithm>
using Microsoft::WRL::ComPtr;
extern "C" void* __cdecl DVFSR_GetRenderEvent();
extern "C" void* __cdecl DVFSR_Create(void*,void*,float);
extern "C" int __cdecl DVFSR_Status(void*,uint64_t*);
using Event=void(__stdcall*)(int,void*);
static void check(HRESULT hr) {if(FAILED(hr)) throw std::runtime_error("HRESULT "+std::to_string(hr));}
int wmain(int argc,wchar_t** argv) {
 try {
    HMODULE module=LoadLibraryW(argc>1 ? argv[1] : L"d3d11.dll");
    if(!module) throw std::runtime_error("D3D11 module unavailable");
    auto create=reinterpret_cast<decltype(&D3D11CreateDevice)>(GetProcAddress(module,"D3D11CreateDevice"));
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL level;
    check(create(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context));
    auto event=reinterpret_cast<Event>(DVFSR_GetRenderEvent());
    for(int pattern=0;pattern<2;pattern++) {
        const UINT iw=67,ih=39,ow=127,oh=73;
        std::vector<unsigned char> pixels(iw*ih*4);
        for(UINT y=0;y<ih;y++) for(UINT x=0;x<iw;x++) {
            auto pos=(y*iw+x)*4;
            unsigned char value=pattern==0 ? 128 : (x+y*2>70 ? 224 : 32);
            pixels[pos]=value; pixels[pos+1]=pattern==0 ? 64 : value; pixels[pos+2]=pattern==0 ? 192 : value; pixels[pos+3]=255;
        }
        D3D11_TEXTURE2D_DESC in{}; in.Width=iw; in.Height=ih; in.MipLevels=in.ArraySize=in.SampleDesc.Count=1;
        in.Format=DXGI_FORMAT_R8G8B8A8_TYPELESS; in.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{pixels.data(),iw*4,0};
        ComPtr<ID3D11Texture2D> input,output,readback;
        check(device->CreateTexture2D(&in,&data,&input));
        auto out=in; out.Width=ow; out.Height=oh; out.BindFlags|=D3D11_BIND_UNORDERED_ACCESS;
        check(device->CreateTexture2D(&out,nullptr,&output));
        auto staging=out; staging.Usage=D3D11_USAGE_STAGING; staging.BindFlags=0; staging.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        check(device->CreateTexture2D(&staging,nullptr,&readback));
        D3D11_BUFFER_DESC bd{}; bd.ByteWidth=16; bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        ComPtr<ID3D11Buffer> sentinel; check(device->CreateBuffer(&bd,nullptr,&sentinel));
        auto buffer=sentinel.Get(); context->CSSetConstantBuffers(0,1,&buffer);
        void* job=DVFSR_Create(input.Get(),output.Get(),pattern==0 ? 0.f : .2f); if(!job) throw std::runtime_error("create failed");
        event(1,job); uint64_t frames=0; int state=DVFSR_Status(job,&frames);
        if(state!=1 || frames!=1) throw std::runtime_error("dispatch failed "+std::to_string(state));
        ComPtr<ID3D11Buffer> restored; context->CSGetConstantBuffers(0,1,&restored);
        if(restored.Get()!=sentinel.Get()) throw std::runtime_error("Unity CS state not restored");
        context->CopyResource(readback.Get(),output.Get()); D3D11_MAPPED_SUBRESOURCE mapped{};
        check(context->Map(readback.Get(),0,D3D11_MAP_READ,0,&mapped));
        size_t differing=0; float largest=0;
        auto sample=[&](int x,int y) {x=std::max(0,std::min(int(iw)-1,x));y=std::max(0,std::min(int(ih)-1,y));return float(pixels[(y*iw+x)*4]);};
        for(UINT y=0;y<oh;y++) for(UINT x=0;x<ow;x++) {
            auto p=static_cast<unsigned char*>(mapped.pData)+y*mapped.RowPitch+x*4;
            if(pattern==0) {if(abs(int(p[0])-128)>1 || abs(int(p[1])-64)>1 || abs(int(p[2])-192)>1 || p[3]!=255) throw std::runtime_error("color encoding/border mismatch");}
            else {
                float sx=(x+.5f)*iw/ow-.5f,sy=(y+.5f)*ih/oh-.5f; int ix=int(floor(sx)),iy=int(floor(sy)); float fx=sx-ix,fy=sy-iy;
                float bilinear=(sample(ix,iy)*(1-fx)+sample(ix+1,iy)*fx)*(1-fy)+(sample(ix,iy+1)*(1-fx)+sample(ix+1,iy+1)*fx)*fy;
                float delta=abs(p[0]-bilinear); if(delta>3) ++differing; largest=std::max(largest,delta);
            }
        }
        context->Unmap(readback.Get(),0);
        if(pattern==1 && differing<50) throw std::runtime_error("FSR appears to be bilinear passthrough");
        std::cout<<"PASS "<<(pattern==0 ? "display-encoded constant/color/borders" : "edge reconstruction")<<" 67x39 -> 127x73; different-from-bilinear="<<differing<<" max-difference="<<largest<<"; compute-state restored\n";
        event(2,job);
    }
    return 0;
 } catch(const std::exception& e) {std::cerr<<"FAIL "<<e.what()<<"\n"; return 1;}
}

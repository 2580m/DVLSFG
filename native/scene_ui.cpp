#include "scene_ui.hpp"
#include "layer.hpp"
#include "mini/image.hpp"
#include "scene_ui_spv.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <atomic>
#include <array>
#include <mutex>
#include <stdexcept>
#include <vector>
using Microsoft::WRL::ComPtr;
// Narrow ABI declarations for DXVK's public DXGI Vulkan interop interfaces.
// Interface IDs/method order: dxvk src/dxgi/dxgi_interfaces.h (v3.1.1).
struct InteropDevice;
struct __declspec(uuid("5546cf8c-77e7-4341-b05d-8d4d5000e77d")) InteropSurface : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetDevice(InteropDevice**)=0;
    virtual HRESULT STDMETHODCALLTYPE GetVulkanImageInfo(VkImage*,VkImageLayout*,VkImageCreateInfo*)=0;
};
struct __declspec(uuid("e2ef5fa5-dc21-4af7-90c4-f67ef6a09323")) InteropDevice : IUnknown {
    virtual void STDMETHODCALLTYPE GetVulkanHandles(VkInstance*,VkPhysicalDevice*,VkDevice*)=0;
    virtual void STDMETHODCALLTYPE GetSubmissionQueue(VkQueue*,uint32_t*)=0;
    virtual void STDMETHODCALLTYPE TransitionSurfaceLayout(InteropSurface*,const VkImageSubresourceRange*,VkImageLayout,VkImageLayout)=0;
    virtual void STDMETHODCALLTYPE FlushRenderingCommands()=0;
    virtual void STDMETHODCALLTYPE LockSubmissionQueue()=0;
    virtual void STDMETHODCALLTYPE ReleaseSubmissionQueue()=0;
};
namespace {
std::mutex frameMutex;
std::shared_ptr<const DVLSFG::SceneUiFrame> latest;
// Production starts before UMM loads: loading/menu UI must never be fed to LSFG.
// The standalone Vulkan regression host opts into the legacy complete-frame path.
std::atomic<bool> requested{GetEnvironmentVariableW(L"DVLSFG_TEST_HOST",nullptr,0)==0};
std::atomic<unsigned long long> serial{0};
struct Resources {std::array<ComPtr<ID3D11Texture2D>,3> textures; std::array<ComPtr<InteropSurface>,3> surfaces; ComPtr<InteropDevice> device;};
std::mutex retiredMutex;
std::vector<Resources*> retired;
void drainRetired() {
    std::vector<Resources*> values;
    {std::lock_guard guard(retiredMutex); values.swap(retired);}
    for(auto value:values) delete value;
}
struct Job {
    // A present-boundary resize may drop the final frame reference while DXVK
    // holds its queue lock. Defer COM Release calls to the Unity render callback.
    std::shared_ptr<Resources> resources{new Resources,[](Resources* value) {std::lock_guard guard(retiredMutex); retired.push_back(value);}};
    std::atomic<int> status{0};
};
void __stdcall event(int kind,void* data) {
    drainRetired();
    if(kind==3) {std::lock_guard guard(frameMutex); latest.reset(); return;}
    auto job=static_cast<Job*>(data); if(!job) return;
    if(kind==2) {
        {std::lock_guard guard(frameMutex); if(latest && latest->resources==job->resources) latest.reset();}
        delete job; drainRetired(); return;
    }
    if(kind!=1 || !requested || job->status<0) return;
    try {
        auto& r=*job->resources;
        if(!r.device) {
            for(size_t i=0;i<3;++i) if(FAILED(r.textures[i].As(&r.surfaces[i]))) throw std::runtime_error("DXVK surface interop unavailable");
            if(FAILED(r.surfaces[0]->GetDevice(&r.device))) throw std::runtime_error("DXVK device interop unavailable");
        }
        // This callback runs after the UI render commands on Unity's render thread.
        // Do not call D3D from vkQueuePresentKHR (DXVK already owns its queue lock).
        r.device->FlushRenderingCommands();
        auto frame=std::make_shared<DVLSFG::SceneUiFrame>();
        r.device->GetVulkanHandles(nullptr,nullptr,&frame->device);
        std::array<DVLSFG::SceneUiFrame::Image*,3> images{&frame->scene,&frame->black,&frame->white};
        for(size_t i=0;i<3;++i) {
            VkImageCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
            if(FAILED(r.surfaces[i]->GetVulkanImageInfo(&images[i]->image,&images[i]->layout,&info)) || info.samples!=VK_SAMPLE_COUNT_1_BIT || !(info.usage&VK_IMAGE_USAGE_SAMPLED_BIT)) throw std::runtime_error("Invalid UI capture texture");
            // DXVK may allocate an R16G16B16A16_UNORM typeless base image for
            // Unity's ARGBHalf target. Sampling that base format interprets the
            // float bit patterns as UNORM. Bind the intended float view instead.
            D3D11_TEXTURE2D_DESC desc{}; r.textures[i]->GetDesc(&desc);
            if(desc.Format!=DXGI_FORMAT_R16G16B16A16_FLOAT && desc.Format!=DXGI_FORMAT_R16G16B16A16_TYPELESS) throw std::runtime_error("Expected Unity ARGBHalf capture texture");
            images[i]->format=VK_FORMAT_R16G16B16A16_SFLOAT;
            if(i==0) frame->extent={info.extent.width,info.extent.height};
            else if(info.extent.width!=frame->extent.width || info.extent.height!=frame->extent.height) throw std::runtime_error("UI capture dimensions mismatch");
        }
        frame->resources=job->resources; frame->serial=++serial;
        {std::lock_guard guard(frameMutex); latest=frame;}
        job->status=1;
    } catch(const std::exception& e) {job->status=-1; DVLSFG::status(std::string("Scene/UI separation unavailable: ")+e.what());}
}
}
extern "C" __declspec(dllexport) void __cdecl DVLSFG_SetSceneUiEnabled(int value) {
    requested=value!=0;
    if(!value) {std::lock_guard guard(frameMutex); latest.reset();}
}
extern "C" __declspec(dllexport) void* __cdecl DVLSFG_GetSceneUiEvent() {return reinterpret_cast<void*>(&event);}
extern "C" __declspec(dllexport) void* __cdecl DVLSFG_CreateSceneUi(void* scene,void* black,void* white) {
    if(!scene || !black || !white) return nullptr;
    auto job=new(std::nothrow) Job; if(!job) return nullptr;
    job->resources->textures={static_cast<ID3D11Texture2D*>(scene),static_cast<ID3D11Texture2D*>(black),static_cast<ID3D11Texture2D*>(white)};
    return job;
}
extern "C" __declspec(dllexport) int __cdecl DVLSFG_SceneUiStatus(void* handle,unsigned long long* frames) {
    if(frames) *frames=serial.load(); return handle ? static_cast<Job*>(handle)->status.load() : 0;
}
namespace DVLSFG {
bool sceneUiRequested() {return requested;}
std::shared_ptr<const SceneUiFrame> acquireSceneUi(VkDevice device,VkExtent2D extent) {
    std::lock_guard guard(frameMutex);
    if(!latest || latest->device!=device || latest->extent.width!=extent.width || latest->extent.height!=extent.height) return {};
    return latest;
}
struct UiComposite::State {
    VkDevice device{}; VkExtent2D extent{}; Mini::Image output;
    bool encoded=true;
    bool bindingsReady=false;
    VkSampler sampler{}; VkDescriptorSetLayout setLayout{}; VkPipelineLayout layout{}; VkPipeline pipeline{}; VkDescriptorPool pool{};
    std::vector<VkImageView> views; std::vector<VkDescriptorSet> sets;
    std::shared_ptr<void> retained;
    template<class T> T fn(const char* name) const {auto value=reinterpret_cast<T>(Layer::ovkGetDeviceProcAddr(device,name)); if(!value) throw std::runtime_error(name); return value;}
    static void check(VkResult result) {if(result!=VK_SUCCESS) throw std::runtime_error("UI compositor Vulkan result "+std::to_string(result));}
    void clearBindings() {
        bindingsReady=false;
        if(pool) {fn<PFN_vkDestroyDescriptorPool>("vkDestroyDescriptorPool")(device,pool,nullptr); pool={};}
        for(auto view:views) fn<PFN_vkDestroyImageView>("vkDestroyImageView")(device,view,nullptr);
        views.clear(); sets.clear(); retained.reset();
    }
    ~State() {
        clearBindings();
        if(pipeline) fn<PFN_vkDestroyPipeline>("vkDestroyPipeline")(device,pipeline,nullptr);
        if(layout) fn<PFN_vkDestroyPipelineLayout>("vkDestroyPipelineLayout")(device,layout,nullptr);
        if(setLayout) fn<PFN_vkDestroyDescriptorSetLayout>("vkDestroyDescriptorSetLayout")(device,setLayout,nullptr);
        if(sampler) fn<PFN_vkDestroySampler>("vkDestroySampler")(device,sampler,nullptr);
    }
    VkImageView view(VkImage image,VkFormat format) {
        VkImageViewCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO}; info.image=image; info.viewType=VK_IMAGE_VIEW_TYPE_2D; info.format=format; info.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
        VkImageView result{}; check(fn<PFN_vkCreateImageView>("vkCreateImageView")(device,&info,nullptr,&result)); views.push_back(result); return result;
    }
};
UiComposite::UiComposite(VkDevice device,VkPhysicalDevice physical,VkExtent2D extent,VkFormat swapchainFormat):state(std::make_shared<State>()) {
    auto& s=*state; s.device=device; s.extent=extent;
    s.encoded=swapchainFormat!=VK_FORMAT_B8G8R8A8_SRGB && swapchainFormat!=VK_FORMAT_R8G8B8A8_SRGB;
    HANDLE handle{}; s.output=Mini::Image(device,physical,extent,VK_FORMAT_R16G16B16A16_SFLOAT,VK_IMAGE_USAGE_STORAGE_BIT,VK_IMAGE_ASPECT_COLOR_BIT,&handle);
    VkSamplerCreateInfo sampler{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO}; sampler.magFilter=sampler.minFilter=VK_FILTER_NEAREST; sampler.addressModeU=sampler.addressModeV=sampler.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    State::check(s.fn<PFN_vkCreateSampler>("vkCreateSampler")(device,&sampler,nullptr,&s.sampler));
    std::array<VkDescriptorSetLayoutBinding,4> bindings{};
    for(uint32_t i=0;i<4;++i) bindings[i]={i,i==3 ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr};
    VkDescriptorSetLayoutCreateInfo desc{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO}; desc.bindingCount=4; desc.pBindings=bindings.data();
    State::check(s.fn<PFN_vkCreateDescriptorSetLayout>("vkCreateDescriptorSetLayout")(device,&desc,nullptr,&s.setLayout));
    VkPushConstantRange push{VK_SHADER_STAGE_COMPUTE_BIT,0,sizeof(uint32_t)};
    VkPipelineLayoutCreateInfo layout{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO}; layout.setLayoutCount=1; layout.pSetLayouts=&s.setLayout; layout.pushConstantRangeCount=1; layout.pPushConstantRanges=&push;
    State::check(s.fn<PFN_vkCreatePipelineLayout>("vkCreatePipelineLayout")(device,&layout,nullptr,&s.layout));
    VkShaderModuleCreateInfo shader{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO}; shader.codeSize=sizeof(scene_ui_spv); shader.pCode=scene_ui_spv;
    VkShaderModule module{}; State::check(s.fn<PFN_vkCreateShaderModule>("vkCreateShaderModule")(device,&shader,nullptr,&module));
    VkComputePipelineCreateInfo pipeline{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO}; pipeline.layout=s.layout; pipeline.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,module,"main",nullptr};
    auto result=s.fn<PFN_vkCreateComputePipelines>("vkCreateComputePipelines")(device,VK_NULL_HANDLE,1,&pipeline,nullptr,&s.pipeline);
    s.fn<PFN_vkDestroyShaderModule>("vkDestroyShaderModule")(device,module,nullptr); State::check(result);
}
void UiComposite::prepare(const SceneUiFrame& frame,const std::vector<VkImage>& generated) {
    auto& s=*state;
    if(s.bindingsReady && s.retained==frame.resources && s.sets.size()==generated.size()+1) return;
    s.clearBindings(); s.retained=frame.resources;
    auto count=static_cast<uint32_t>(generated.size()+1);
    std::array<VkDescriptorPoolSize,2> sizes{{{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,count*3},{VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,count}}};
    VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO}; pool.maxSets=count; pool.poolSizeCount=2; pool.pPoolSizes=sizes.data();
    State::check(s.fn<PFN_vkCreateDescriptorPool>("vkCreateDescriptorPool")(s.device,&pool,nullptr,&s.pool));
    std::vector<VkDescriptorSetLayout> layouts(count,s.setLayout); s.sets.resize(count);
    VkDescriptorSetAllocateInfo alloc{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO}; alloc.descriptorPool=s.pool; alloc.descriptorSetCount=count; alloc.pSetLayouts=layouts.data();
    State::check(s.fn<PFN_vkAllocateDescriptorSets>("vkAllocateDescriptorSets")(s.device,&alloc,s.sets.data()));
    auto black=s.view(frame.black.image,frame.black.format),white=s.view(frame.white.image,frame.white.format),output=s.view(s.output.handle(),VK_FORMAT_R16G16B16A16_SFLOAT);
    for(uint32_t i=0;i<count;++i) {
        auto source=i<generated.size() ? generated[i] : frame.scene.image;
        std::array<VkDescriptorImageInfo,4> info{{{s.sampler,s.view(source,VK_FORMAT_R16G16B16A16_SFLOAT),VK_IMAGE_LAYOUT_GENERAL},{s.sampler,black,VK_IMAGE_LAYOUT_GENERAL},{s.sampler,white,VK_IMAGE_LAYOUT_GENERAL},{VK_NULL_HANDLE,output,VK_IMAGE_LAYOUT_GENERAL}}};
        std::array<VkWriteDescriptorSet,4> writes{};
        for(uint32_t j=0;j<4;++j) {writes[j]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET}; writes[j].dstSet=s.sets[i]; writes[j].dstBinding=j; writes[j].descriptorCount=1; writes[j].descriptorType=j==3 ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER; writes[j].pImageInfo=&info[j];}
        s.fn<PFN_vkUpdateDescriptorSets>("vkUpdateDescriptorSets")(s.device,4,writes.data(),0,nullptr);
    }
    s.bindingsReady=true;
}
VkImage UiComposite::record(VkCommandBuffer command,size_t index,bool sceneOnly) {
    auto& s=*state;
    VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER}; b.image=s.output.handle(); b.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED; b.newLayout=VK_IMAGE_LAYOUT_GENERAL; b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED; b.srcAccessMask=VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT; b.dstAccessMask=VK_ACCESS_SHADER_WRITE_BIT; b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
    Layer::ovkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,0,0,nullptr,0,nullptr,1,&b);
    s.fn<PFN_vkCmdBindPipeline>("vkCmdBindPipeline")(command,VK_PIPELINE_BIND_POINT_COMPUTE,s.pipeline);
    s.fn<PFN_vkCmdBindDescriptorSets>("vkCmdBindDescriptorSets")(command,VK_PIPELINE_BIND_POINT_COMPUTE,s.layout,0,1,&s.sets.at(index),0,nullptr);
    uint32_t mode=(sceneOnly ? 1u : 0u)|(s.encoded ? 2u : 0u);
    s.fn<PFN_vkCmdPushConstants>("vkCmdPushConstants")(command,s.layout,VK_SHADER_STAGE_COMPUTE_BIT,0,sizeof(mode),&mode);
    s.fn<PFN_vkCmdDispatch>("vkCmdDispatch")(command,(s.extent.width+7)/8,(s.extent.height+7)/8,1);
    b.oldLayout=VK_IMAGE_LAYOUT_GENERAL; b.newLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL; b.srcAccessMask=VK_ACCESS_SHADER_WRITE_BIT; b.dstAccessMask=VK_ACCESS_TRANSFER_READ_BIT;
    Layer::ovkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&b);
    return s.output.handle();
}
}

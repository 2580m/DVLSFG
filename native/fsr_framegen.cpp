#include "fsr_framegen.hpp"
#include <FidelityFX/host/backends/vk/ffx_vk.h>
#include <FidelityFX/host/ffx_frameinterpolation.h>
#include <FidelityFX/host/ffx_opticalflow.h>
#include <array>
#include <algorithm>
#include <stdexcept>
#include <filesystem>

namespace {
PFN_vkEnumerateDeviceExtensionProperties enumerateExtensions{};
PFN_vkGetPhysicalDeviceMemoryProperties memoryProperties{};
PFN_vkGetPhysicalDeviceProperties properties{};
PFN_vkGetPhysicalDeviceProperties2 properties2{};
PFN_vkGetPhysicalDeviceFeatures features{};
PFN_vkGetPhysicalDeviceFeatures2 features2{};
PFN_vkGetPhysicalDeviceQueueFamilyProperties queueProperties{};
PFN_vkGetDeviceProcAddr deviceProc{};
bool profilingRequested() {
    wchar_t path[32768]{};
    auto module=GetModuleHandleW(L"DVLSFG.FSRFG.dll");
    if(!module || !GetModuleFileNameW(module,path,32768)) return false;
    std::error_code error;
    return std::filesystem::exists(std::filesystem::path(path).parent_path()/L"profile.request",error);
}
void VKAPI_CALL noBeginLabel(VkCommandBuffer,const VkDebugUtilsLabelEXT*) {}
void VKAPI_CALL noEndLabel(VkCommandBuffer) {}
VkResult VKAPI_CALL noObjectName(VkDevice,const VkDebugUtilsObjectNameInfoEXT*) {return VK_SUCCESS;}
PFN_vkVoidFunction VKAPI_CALL sdkDeviceProc(VkDevice device,const char* name) {
    auto result=deviceProc(device,name);
    // DXVK uses Vulkan core 1.3 and need not enable promoted KHR extensions.
    // AMD's backend still requests several of their older alias names.
    std::string command=name;
    if(!result && command.size()>3 && command.ends_with("KHR")) result=deviceProc(device,command.substr(0,command.size()-3).c_str());
    if(!result && command=="vkCmdBeginDebugUtilsLabelEXT") return reinterpret_cast<PFN_vkVoidFunction>(noBeginLabel);
    if(!result && command=="vkCmdEndDebugUtilsLabelEXT") return reinterpret_cast<PFN_vkVoidFunction>(noEndLabel);
    if(!result && command=="vkSetDebugUtilsObjectNameEXT") return reinterpret_cast<PFN_vkVoidFunction>(noObjectName);
    return result;
}
}
extern "C" {
VKAPI_ATTR VkResult VKAPI_CALL dvFsrEnumerateDeviceExtensionProperties(VkPhysicalDevice device,const char* name,uint32_t* count,VkExtensionProperties* values) {return enumerateExtensions(device,name,count,values);}
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceMemoryProperties(VkPhysicalDevice device,VkPhysicalDeviceMemoryProperties* value) {memoryProperties(device,value);}
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceProperties(VkPhysicalDevice device,VkPhysicalDeviceProperties* value) {properties(device,value);}
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceProperties2(VkPhysicalDevice device,VkPhysicalDeviceProperties2* value) {properties2(device,value);}
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceFeatures(VkPhysicalDevice device,VkPhysicalDeviceFeatures* value) {features(device,value);}
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceFeatures2(VkPhysicalDevice device,VkPhysicalDeviceFeatures2* value) {features2(device,value);}
VKAPI_ATTR VkResult VKAPI_CALL dvFsrCreateBuffer(VkDevice device,const VkBufferCreateInfo* info,const VkAllocationCallbacks* allocator,VkBuffer* value) {return reinterpret_cast<PFN_vkCreateBuffer>(deviceProc(device,"vkCreateBuffer"))(device,info,allocator,value);}
}

namespace DVLSFG {
void FsrFrameGeneration::configureInstance(VkInstance instance,PFN_vkGetInstanceProcAddr proc) {
    enumerateExtensions=reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(proc(instance,"vkEnumerateDeviceExtensionProperties"));
    memoryProperties=reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(proc(instance,"vkGetPhysicalDeviceMemoryProperties"));
    properties=reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(proc(instance,"vkGetPhysicalDeviceProperties"));
    properties2=reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(proc(instance,"vkGetPhysicalDeviceProperties2"));
    features=reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures>(proc(instance,"vkGetPhysicalDeviceFeatures"));
    features2=reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures2>(proc(instance,"vkGetPhysicalDeviceFeatures2"));
    queueProperties=reinterpret_cast<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(proc(instance,"vkGetPhysicalDeviceQueueFamilyProperties"));
}
namespace {
void check(FfxErrorCode result,const char* operation) {
    if(result!=FFX_OK) throw std::runtime_error(std::string(operation)+": FidelityFX error "+std::to_string(result));
}
FfxResource resource(VkImage image,VkExtent2D size,FfxSurfaceFormat format,FfxResourceStates state,FfxResourceUsage usage) {
    FfxResourceDescription description{};
    description.type=FFX_RESOURCE_TYPE_TEXTURE2D; description.format=format;
    description.width=size.width; description.height=size.height; description.depth=1; description.mipCount=1; description.usage=usage;
    return ffxGetResourceVK(reinterpret_cast<void*>(image),description,L"DV Scaling",state);
}
}
struct FsrFrameGeneration::State {
    VkDeviceContext device{};
    VkExtent2D display{},render{};
    bool encoded{},fiCreated{},ofCreated{},sharedCreated{};
    std::vector<uint8_t> scratch;
    FfxInterface backend{};
    FfxFrameInterpolationContext fi{};
    FfxOpticalflowContext of{};
    FfxUInt32 sharedId{};
    std::array<FfxResourceInternal,5> images{};
    size_t allocated{};
    uint64_t frameId{};
    VkQueryPool profileQueries{};
    float timestampPeriod{};
    uint64_t timestampMask=UINT64_MAX;
    bool profilePending{};
    unsigned profileCount{};
    std::array<double,3> profileSum{};
    void (*logger)(const std::string&){};
    void clear() {
        if(fiCreated) {ffxFrameInterpolationContextDestroy(&fi); fiCreated=false;}
        if(ofCreated) {ffxOpticalflowContextDestroy(&of); ofCreated=false;}
        for(size_t i=0;i<allocated;++i) backend.fpDestroyResource(&backend,images[i],sharedId);
        allocated=0;
        if(sharedCreated) {backend.fpDestroyBackendContext(&backend,sharedId); sharedCreated=false;}
    }
    ~State() {
        clear();
        if(profileQueries) reinterpret_cast<PFN_vkDestroyQueryPool>(deviceProc(device.vkDevice,"vkDestroyQueryPool"))(device.vkDevice,profileQueries,nullptr);
    }
    void profileBegin(VkCommandBuffer command) {
        if(!profileQueries) return;
        if(profilePending) {
            std::array<uint64_t,4> stamps{};
            auto result=reinterpret_cast<PFN_vkGetQueryPoolResults>(deviceProc(device.vkDevice,"vkGetQueryPoolResults"))(device.vkDevice,profileQueries,0,4,sizeof(stamps),stamps.data(),sizeof(uint64_t),VK_QUERY_RESULT_64_BIT);
            if(result==VK_SUCCESS) {
                for(size_t i=0;i<3;++i) profileSum[i]+=((stamps[i+1]-stamps[i])&timestampMask)*timestampPeriod/1e6;
                if(++profileCount==120) {
                    logger("FSR FG GPU ms (120 frames): prepare="+std::to_string(profileSum[0]/profileCount)+" optical="+std::to_string(profileSum[1]/profileCount)+" interpolation="+std::to_string(profileSum[2]/profileCount));
                    profileSum={}; profileCount=0;
                }
            }
        }
        reinterpret_cast<PFN_vkCmdResetQueryPool>(deviceProc(device.vkDevice,"vkCmdResetQueryPool"))(command,profileQueries,0,4);
        profileStamp(command,0); profilePending=true;
    }
    void profileStamp(VkCommandBuffer command,uint32_t index) {
        if(profileQueries) reinterpret_cast<PFN_vkCmdWriteTimestamp>(deviceProc(device.vkDevice,"vkCmdWriteTimestamp"))(command,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,profileQueries,index);
    }
    FfxResource get(size_t index) {return backend.fpGetResource(&backend,images.at(index));}
    void create(VkExtent2D size) {
        clear(); render=size; frameId=0;
        scratch.assign(ffxGetScratchMemorySizeVK(device.vkPhysicalDevice,3),0);
        check(ffxGetInterfaceVK(&backend,ffxGetDeviceVK(&device),scratch.data(),scratch.size(),3),"Vulkan backend");
        check(backend.fpCreateBackendContext(&backend,FFX_EFFECT_SHAREDRESOURCES,nullptr,&sharedId),"Shared context"); sharedCreated=true;
        FfxFrameInterpolationContextDescription description{};
        description.flags=FFX_FRAMEINTERPOLATION_ENABLE_DEPTH_INVERTED;
        description.maxRenderSize={size.width,size.height}; description.displaySize={display.width,display.height};
        description.backBufferFormat=description.previousInterpolationSourceFormat=FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT;
        description.backendInterface=backend;
        check(ffxFrameInterpolationContextCreate(&fi,&description),"Frame interpolation context"); fiCreated=true;
        FfxOpticalflowContextDescription optical{}; optical.backendInterface=backend; optical.resolution={display.width,display.height};
        check(ffxOpticalflowContextCreate(&of,&optical),"Optical flow context"); ofCreated=true;
        FfxFrameInterpolationSharedResourceDescriptions fiImages{};
        FfxOpticalflowSharedResourceDescriptions ofImages{};
        check(ffxFrameInterpolationGetSharedResourceDescriptions(&fi,&fiImages),"Interpolation resource descriptions");
        check(ffxOpticalflowGetSharedResourceDescriptions(&of,&ofImages),"Optical flow resource descriptions");
        std::array<FfxCreateResourceDescription*,5> descriptions{&fiImages.dilatedDepth,&fiImages.dilatedMotionVectors,&fiImages.reconstructedPrevNearestDepth,&ofImages.opticalFlowVector,&ofImages.opticalFlowSCD};
        for(auto desc:descriptions) {check(backend.fpCreateResource(&backend,desc,sharedId,&images[allocated]),"Shared image"); ++allocated;}
        logger("AMD FSR 3.1 frame generation ready: "+std::to_string(size.width)+"x"+std::to_string(size.height));
    }
};
FsrFrameGeneration::FsrFrameGeneration(VkDevice device,VkPhysicalDevice physical,VkExtent2D display,bool encoded,PFN_vkGetDeviceProcAddr proc,void (*logger)(const std::string&)):state(std::make_unique<State>()) {
    state->device={device,physical,sdkDeviceProc}; state->display=display; state->encoded=encoded; state->logger=logger;
    deviceProc=proc;
    if(!enumerateExtensions || !memoryProperties || !properties || !properties2 || !features || !features2) throw std::runtime_error("FSR downstream instance dispatch unavailable");
    if(profilingRequested() && queueProperties) {
        VkPhysicalDeviceProperties info{}; properties(physical,&info);
        uint32_t count{}; queueProperties(physical,&count,nullptr);
        std::vector<VkQueueFamilyProperties> queues(count); queueProperties(physical,&count,queues.data());
        unsigned bits=64;
        for(auto& queue:queues) if(queue.queueFlags&(VK_QUEUE_GRAPHICS_BIT|VK_QUEUE_COMPUTE_BIT)) bits=std::min(bits,queue.timestampValidBits);
        if(info.limits.timestampComputeAndGraphics && bits) {
            state->timestampMask=bits==64 ? UINT64_MAX : (uint64_t(1)<<bits)-1;
            VkQueryPoolCreateInfo query{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO}; query.queryType=VK_QUERY_TYPE_TIMESTAMP; query.queryCount=4;
            if(reinterpret_cast<PFN_vkCreateQueryPool>(proc(device,"vkCreateQueryPool"))(device,&query,nullptr,&state->profileQueries)==VK_SUCCESS) state->timestampPeriod=info.limits.timestampPeriod;
        }
    }
}
FsrFrameGeneration::~FsrFrameGeneration()=default;
void FsrFrameGeneration::dispatch(VkCommandBuffer command,VkImage color,VkImage output,const SceneUiFrame& frame,bool reset) {
    auto& s=*state;
    if(!frame.depth.image || !frame.motion.image) throw std::runtime_error("FSR frame generation requires depth and motion vectors");
    if(!s.fiCreated || s.render.width!=frame.renderExtent.width || s.render.height!=frame.renderExtent.height) s.create(frame.renderExtent);
    s.profileBegin(command);
    reset|=s.frameId==0 || frame.reset;
    const auto input=resource(color,s.display,FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT,FFX_RESOURCE_STATE_COMMON,FFX_RESOURCE_USAGE_READ_ONLY);
    FfxFrameInterpolationPrepareDescription prepare{};
    prepare.commandList=ffxGetCommandListVK(command); prepare.renderSize={s.render.width,s.render.height};
    prepare.jitterOffset={frame.jitterX,frame.jitterY}; prepare.motionVectorScale={-float(s.render.width),float(s.render.height)};
    prepare.frameTimeDelta=frame.deltaMs; prepare.cameraNear=frame.nearPlane; prepare.cameraFar=frame.farPlane;
    prepare.cameraFovAngleVertical=frame.fov; prepare.viewSpaceToMetersFactor=1;
    prepare.depth=resource(frame.depth.image,s.render,FFX_SURFACE_FORMAT_R32_FLOAT,FFX_RESOURCE_STATE_COMMON,FFX_RESOURCE_USAGE_READ_ONLY);
    prepare.motionVectors=resource(frame.motion.image,s.render,FFX_SURFACE_FORMAT_R32G32_FLOAT,FFX_RESOURCE_STATE_COMMON,FFX_RESOURCE_USAGE_READ_ONLY);
    prepare.dilatedDepth=s.get(0); prepare.dilatedMotionVectors=s.get(1); prepare.reconstructedPrevDepth=s.get(2); prepare.frameID=++s.frameId;
    check(ffxFrameInterpolationPrepare(&s.fi,&prepare),"Frame generation prepare");
    s.profileStamp(command,1);
    FfxOpticalflowDispatchDescription optical{}; optical.commandList=prepare.commandList; optical.color=input;
    optical.opticalFlowVector=s.get(3); optical.opticalFlowSCD=s.get(4); optical.reset=reset;
    optical.backbufferTransferFunction=s.encoded ? FFX_BACKBUFFER_TRANSFER_FUNCTION_SRGB : FFX_BACKBUFFER_TRANSFER_FUNCTION_SCRGB;
    optical.minMaxLuminance={0,1};
    check(ffxOpticalflowContextDispatch(&s.of,&optical),"Optical flow dispatch");
    s.profileStamp(command,2);
    FfxFrameInterpolationDispatchDescription dispatch{};
    dispatch.commandList=prepare.commandList; dispatch.displaySize={s.display.width,s.display.height}; dispatch.renderSize=prepare.renderSize;
    // This input already excludes UI. Registering it in both slots makes the
    // SDK track two conflicting layouts for the same VkImage.
    dispatch.currentBackBuffer=input;
    dispatch.output=resource(output,s.display,FFX_SURFACE_FORMAT_R16G16B16A16_FLOAT,FFX_RESOURCE_STATE_UNORDERED_ACCESS,FFX_RESOURCE_USAGE_UAV);
    dispatch.interpolationRect={0,0,int32_t(s.display.width),int32_t(s.display.height)};
    dispatch.opticalFlowVector=optical.opticalFlowVector; dispatch.opticalFlowSceneChangeDetection=optical.opticalFlowSCD;
    dispatch.opticalFlowBlockSize=8; dispatch.opticalFlowScale={1.f/s.display.width,1.f/s.display.height};
    dispatch.opticalFlowBufferSize={optical.opticalFlowVector.description.width,optical.opticalFlowVector.description.height};
    dispatch.cameraNear=prepare.cameraNear; dispatch.cameraFar=prepare.cameraFar; dispatch.cameraFovAngleVertical=prepare.cameraFovAngleVertical;
    dispatch.viewSpaceToMetersFactor=1; dispatch.frameTimeDelta=prepare.frameTimeDelta; dispatch.reset=reset; dispatch.frameID=prepare.frameID;
    dispatch.backBufferTransferFunction=static_cast<FfxBackbufferTransferFunction>(optical.backbufferTransferFunction);
    dispatch.minMaxLuminance[1]=1;
    dispatch.dilatedDepth=prepare.dilatedDepth; dispatch.dilatedMotionVectors=prepare.dilatedMotionVectors; dispatch.reconstructedPrevDepth=prepare.reconstructedPrevDepth;
    check(ffxFrameInterpolationDispatch(&s.fi,&dispatch),"Frame interpolation dispatch");
    s.profileStamp(command,3);
}
}

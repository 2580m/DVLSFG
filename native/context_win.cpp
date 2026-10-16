#include "platform.hpp"
#include "context.hpp"
#include "config/config.hpp"
#include "extract/extract.hpp"
#include "extract/trans.hpp"
#include "utils/utils.hpp"
#include "layer.hpp"
#include "common/exception.hpp"
#include "readback.hpp"
#include <lsfg_3_1.hpp>
#include <lsfg_3_1p.hpp>
#include <array>
#include <vector>
#include <chrono>
#include <thread>
#include <deque>

namespace {
using OutputStamp=std::pair<std::chrono::steady_clock::time_point,bool>;
thread_local std::deque<OutputStamp> outputHistory;
std::atomic<bool> legacyPresentWait{false};
std::atomic<float> pacingHeadroom{.98f};
void recordOutput(std::chrono::steady_clock::time_point time,bool generated) {
    outputHistory.emplace_back(time,generated);
    if(outputHistory.size()>120) outputHistory.pop_front();
}
void barrier(VkCommandBuffer buf, VkImage image, VkImageLayout oldLayout, VkImageLayout newLayout,
    unsigned srcFamily, unsigned dstFamily, VkAccessFlags srcAccess, VkAccessFlags dstAccess) {
    VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    b.srcAccessMask=srcAccess; b.dstAccessMask=dstAccess;
    b.oldLayout=oldLayout; b.newLayout=newLayout;
    b.srcQueueFamilyIndex=srcFamily; b.dstQueueFamilyIndex=dstFamily;
    b.image=image; b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
    Layer::ovkCmdPipelineBarrier(buf,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
        0,0,nullptr,0,nullptr,1,&b);
}
void blit(VkCommandBuffer buf, VkImage src, VkImage dst, VkExtent2D extent) {
    VkImageBlit region{};
    region.srcSubresource=region.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};
    region.srcOffsets[1]=region.dstOffsets[1]={int(extent.width),int(extent.height),1};
    Layer::ovkCmdBlitImage(buf,src,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,dst,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        1,&region,VK_FILTER_NEAREST);
}
void waitIdle(VkDevice device) {
    auto fn=reinterpret_cast<PFN_vkDeviceWaitIdle>(Layer::ovkGetDeviceProcAddr(device,"vkDeviceWaitIdle"));
    VkResult res=fn(device); if(res!=VK_SUCCESS) throw LSFG::vulkan_error(res,"Game device wait failed");
}
void waitGeneratedImage(VkDevice device,VkQueue queue) {
    // Start the display interval only once interpolation/copy work is ready.
    // Otherwise GPU work eats the interval and the compositor may receive the
    // generated and real images together, discarding the generated image.
    auto create=reinterpret_cast<PFN_vkCreateFence>(Layer::ovkGetDeviceProcAddr(device,"vkCreateFence"));
    auto destroy=reinterpret_cast<PFN_vkDestroyFence>(Layer::ovkGetDeviceProcAddr(device,"vkDestroyFence"));
    auto submit=reinterpret_cast<PFN_vkQueueSubmit>(Layer::ovkGetDeviceProcAddr(device,"vkQueueSubmit"));
    auto wait=reinterpret_cast<PFN_vkWaitForFences>(Layer::ovkGetDeviceProcAddr(device,"vkWaitForFences"));
    VkFenceCreateInfo desc{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
    VkFence fence{};
    auto result=create(device,&desc,nullptr,&fence);
    if(result!=VK_SUCCESS) throw LSFG::vulkan_error(result,"Generated-image fence creation failed");
    VkSubmitInfo marker{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    result=submit(queue,1,&marker,fence);
    if(result==VK_SUCCESS) result=wait(device,1,&fence,VK_TRUE,UINT64_MAX);
    destroy(device,fence,nullptr);
    if(result!=VK_SUCCESS) throw LSFG::vulkan_error(result,"Generated-image readiness wait failed");
}
// Observe a GPU event on the presentation worker. The graphics-queue command
// stream orders all image reads before later Unity writes; presentation still
// waits on its explicit semaphores. Do not retire acquired-image submissions
// through a host fence before their images reach the presentation engine.
class ImageReadiness {
    VkDevice device{};
    VkEvent event{};
    PFN_vkGetEventStatus getFn{};
    PFN_vkDestroyEvent destroyFn{};
public:
    ImageReadiness(VkDevice value,VkCommandBuffer command):device(value) {
        auto create=reinterpret_cast<PFN_vkCreateEvent>(Layer::ovkGetDeviceProcAddr(device,"vkCreateEvent"));
        getFn=reinterpret_cast<PFN_vkGetEventStatus>(Layer::ovkGetDeviceProcAddr(device,"vkGetEventStatus"));
        destroyFn=reinterpret_cast<PFN_vkDestroyEvent>(Layer::ovkGetDeviceProcAddr(device,"vkDestroyEvent"));
        VkEventCreateInfo desc{VK_STRUCTURE_TYPE_EVENT_CREATE_INFO};
        auto result=create(device,&desc,nullptr,&event);
        if(result!=VK_SUCCESS) throw LSFG::vulkan_error(result,"Generated-image event creation failed");
        auto set=reinterpret_cast<PFN_vkCmdSetEvent>(Layer::ovkGetDeviceProcAddr(device,"vkCmdSetEvent"));
        set(command,event,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
    }
    VkResult wait() const {for(;;) {auto result=getFn(device,event); if(result==VK_EVENT_SET) return VK_SUCCESS; if(result!=VK_EVENT_RESET) return result; SwitchToThread();}}
    ~ImageReadiness() {if(event) destroyFn(device,event,nullptr);}
};
void completeRejectedPresentFence(VkDevice device,VkQueue queue,const void* metadata,bool queueOnly=false) {
    // Some WSI paths reject a second present on an already-outdated chain
    // without completing its fence. DXVK expects normal swapchain errors to
    // complete queue operations. After idle, finish an unsignaled rejected fence.
    auto get=reinterpret_cast<PFN_vkGetFenceStatus>(Layer::ovkGetDeviceProcAddr(device,"vkGetFenceStatus"));
    auto submit=reinterpret_cast<PFN_vkQueueSubmit>(Layer::ovkGetDeviceProcAddr(device,"vkQueueSubmit"));
    auto reset=reinterpret_cast<PFN_vkResetFences>(Layer::ovkGetDeviceProcAddr(device,"vkResetFences"));
    for(auto* node=static_cast<const VkBaseInStructure*>(metadata);node;node=node->pNext) {
        if(node->sType!=VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT) continue;
        auto* fences=reinterpret_cast<const VkSwapchainPresentFenceInfoEXT*>(node);
        if(fences->swapchainCount!=1 || !fences->pFences || !fences->pFences[0]) continue;
        auto fence=fences->pFences[0];
        if(get(device,fence)==VK_NOT_READY) {
            auto resetResult=reset(device,1,&fence);
            if(resetResult!=VK_SUCCESS) throw LSFG::vulkan_error(resetResult,"Rejected-present fence reset failed");
            VkSubmitInfo complete{VK_STRUCTURE_TYPE_SUBMIT_INFO};
            auto result=submit(queue,1,&complete,fence);
            if(result!=VK_SUCCESS) throw LSFG::vulkan_error(result,"Rejected-present fence completion failed");
            if(queueOnly) {
                auto idle=reinterpret_cast<PFN_vkQueueWaitIdle>(Layer::ovkGetDeviceProcAddr(device,"vkQueueWaitIdle"));
                auto idleResult=idle(queue);
                if(idleResult!=VK_SUCCESS) throw LSFG::vulkan_error(idleResult,"Rejected-present queue wait failed");
            } else waitIdle(device);
            DVLSFG::status("Completed application fence after rejected resize present");
        }
    }
}
void waitUntil(std::chrono::steady_clock::time_point deadline) {
    struct Timer {
        HANDLE handle=CreateWaitableTimerExW(nullptr,nullptr,0x2,TIMER_ALL_ACCESS);
        ~Timer() { if(handle) CloseHandle(handle); }
    };
    thread_local Timer timer;
    // Windows can resume a waitable-timer thread several hundred microseconds
    // late. Finish the last fraction actively so the two alternating intervals
    // do not inherit that systematic scheduling delay.
    auto wake=deadline-std::chrono::microseconds(800);
    auto remaining=wake-std::chrono::steady_clock::now();
    if(remaining>std::chrono::steady_clock::duration::zero()) {
        LARGE_INTEGER due{};
        due.QuadPart=-std::max<long long>(1,std::chrono::duration_cast<std::chrono::nanoseconds>(remaining).count()/100);
        if(timer.handle && SetWaitableTimer(timer.handle,&due,0,nullptr,nullptr,FALSE)) WaitForSingleObject(timer.handle,INFINITE);
        else std::this_thread::sleep_until(wake);
    }
    while(std::chrono::steady_clock::now()<deadline) YieldProcessor();
}
}
// Test-only A/B control; defaults to the pipelined production path.
extern "C" __declspec(dllexport) void __cdecl DVLSFG_SetLegacyPresentWait(int enabled) {legacyPresentWait.store(enabled!=0);}
extern "C" __declspec(dllexport) void __cdecl DVLSFG_SetPacingHeadroom(float value) {if(value>=.5f && value<=1) pacingHeadroom.store(value);}
LsContext::LsContext(const Hooks::DeviceInfo& info,VkSwapchainKHR chain,VkExtent2D size,const std::vector<VkImage>& images,VkFormat sourceFormat)
    : swapchain(chain),swapchainImages(images),extent(size),storedDevice(info.device),swapchainFormat(sourceFormat) {
    const auto& conf=Config::activeConf;
    if(size.width<128 || size.height<128 || (DVLSFG::generationTechnology()==1 && (size.width*conf.flowScale<64 || size.height*conf.flowScale<64)))
        throw std::runtime_error("Swapchain or flow resolution too small for LSFG pyramid");
    const VkFormat format=VK_FORMAT_R16G16B16A16_SFLOAT;
    std::array<HANDLE,2> inputs{};
    frame_0=Mini::Image(info.device,info.physicalDevice,size,format,VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_COLOR_BIT,&inputs[0]);
    frame_1=Mini::Image(info.device,info.physicalDevice,size,format,VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_COLOR_BIT,&inputs[1]);
    std::vector<HANDLE> outputs(conf.multiplier-1);
    for(size_t i=0;i<outputs.size();++i) out_n.emplace_back(info.device,info.physicalDevice,size,format,VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_IMAGE_ASPECT_COLOR_BIT,&outputs[i]);
    if(DVLSFG::generationTechnology()==2) {
        fsrFrameGeneration=std::make_shared<DVLSFG::FsrFrameGeneration>(info.device,info.physicalDevice,size,sourceFormat!=VK_FORMAT_B8G8R8A8_SRGB && sourceFormat!=VK_FORMAT_R8G8B8A8_SRGB,Layer::ovkGetDeviceProcAddr,DVLSFG::status);
    } else {
    auto init=conf.performance ? LSFG_3_1P::initialize : LSFG_3_1::initialize;
    auto create=conf.performance ? LSFG_3_1P::createContext : LSFG_3_1::createContext;
    auto destroy=conf.performance ? LSFG_3_1P::deleteContext : LSFG_3_1::deleteContext;
    {
        DVLSFG::BackendScope scope;
        init(Utils::getDeviceUUID(info.physicalDevice),false,1.f/conf.flowScale,conf.multiplier-1,
            [](const std::string& name) { return Extract::translateShader(Extract::getShader(name)); });
        lsfgCtxId=std::shared_ptr<int32_t>(new int32_t(create(inputs[0],inputs[1],outputs,size,format)),
            [destroy](int32_t* id) { try { destroy(*id); } catch(...) {} delete id; });
    }
    }
    cmdPool=Mini::CommandPool(info.device,info.queue.first);
    DVLSFG::status("Swapchain ready: " + std::to_string(size.width) + "x" + std::to_string(size.height));
}
LsContext::~LsContext() noexcept {
    try {
        finishPending();
        if(!lsfgCtxId && !fsrFrameGeneration) return;
        DVLSFG::status("Destroy context: waiting for game device");
        if(storedDevice) waitIdle(storedDevice);
        DVLSFG::status("Destroy context: releasing backend");
        lsfgCtxId.reset();
        fsrFrameGeneration.reset();
        DVLSFG::status("Destroy context: backend released");
    } catch(...) {}
}
namespace {
bool usesFence(const DVLSFG::DeferredPresent& present,VkFence fence) {
    for(auto* node=static_cast<const VkBaseInStructure*>(present.metadata.get());node;node=node->pNext) {
        if(node->sType==VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT && reinterpret_cast<const VkSwapchainPresentFenceInfoEXT*>(node)->pFences[0]==fence) return true;
    }
    return false;
}
}
VkResult LsContext::finishPendingCount(size_t count) {
    VkResult result=VK_SUCCESS;
    while(count-- && !deferredPresents.empty()) {
        auto job=deferredPresents.front(); job->join();
        if(job->generatedSubmitted.time_since_epoch().count()) recordOutput(job->generatedSubmitted,true);
        if(job->submitted.time_since_epoch().count()) {previousOutput=job->submitted; recordOutput(previousOutput,false);}
        if(result==VK_SUCCESS || result==VK_SUBOPTIMAL_KHR) result=job->result;
        deferredPresents.pop_front();
    }
    return result;
}
VkResult LsContext::finishPending(VkFence fence) {
    if(!fence) return finishPendingCount(deferredPresents.size());
    size_t count=0; for(auto& job:deferredPresents) {++count; if(usesFence(*job,fence)) return finishPendingCount(count);}
    return VK_SUCCESS;
}
VkResult LsContext::finishPendingId(uint64_t id) {
    size_t count=0; for(auto& job:deferredPresents) {++count; if(job->metadata.usesId(id)) return finishPendingCount(count);}
    return VK_SUCCESS;
}
bool LsContext::hasPendingFence(VkFence fence) const {
    for(auto& job:deferredPresents) if(usesFence(*job,fence)) return true;
    return false;
}
bool LsContext::pendingFinished(VkFence fence,uint64_t id) const {
    for(auto& job:deferredPresents) if((!fence && !id) || (fence && usesFence(*job,fence)) || (id && job->metadata.usesId(id))) {if(!job->finished.load()) return false;}
    return true;
}
bool LsContext::hasPendingPresentId(uint64_t id) const {
    for(auto& job:deferredPresents) if(job->metadata.usesId(id)) return true;
    return false;
}
VkResult LsContext::present(const Hooks::DeviceInfo& info,const void* pNext,VkQueue queue,const std::vector<VkSemaphore>& waits,uint32_t index) {
    size_t completed=0; for(auto& job:deferredPresents) {if(!job->finished.load()) break; ++completed;}
    auto pendingResult=finishPendingCount(completed);
    auto ui=DVLSFG::acquireSceneUi(info.device,extent);
    const bool fsr=bool(fsrFrameGeneration);
    if((pendingResult!=VK_SUCCESS && pendingResult!=VK_SUBOPTIMAL_KHR) || (DVLSFG::sceneUiRequested() && (!ui || ui->serial==lastSceneSerial)) || (fsr && (!ui || !ui->depth.image || !ui->motion.image))) {
        // Menu/loading frames retain ordinary presentation, never UI interpolation.
        finishPending();
        VkPresentInfoKHR actual{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR}; actual.pNext=pNext;
        actual.waitSemaphoreCount=static_cast<uint32_t>(waits.size()); actual.pWaitSemaphores=waits.data();
        actual.swapchainCount=1; actual.pSwapchains=&swapchain; actual.pImageIndices=&index;
        lastEpoch=0; previousInput={}; previousOutput={};
        VkResult result;
        {std::lock_guard guard(*chainMutex); result=Layer::ovkQueuePresentKHR(queue,&actual);}
        if(result==VK_ERROR_OUT_OF_DATE_KHR || result==VK_ERROR_SURFACE_LOST_KHR) {waitIdle(info.device); completeRejectedPresentFence(info.device,info.queue.second,pNext);}
        return result;
    }
    const auto& conf=Config::activeConf;
    auto deferred=std::make_shared<DVLSFG::DeferredPresent>();
    bool async=info.presentQueue && queue==info.queue.second && conf.multiplier==2 && deferred->metadata.build(pNext);
    bool deferGenerated=async && swapchainImages.size()>=5 && !legacyPresentWait.load();
    if(!deferGenerated) finishPending();
    else if(!deferredPresents.empty() && !deferredPresents.back()->pipelined) finishPending();
    else if(deferredPresents.size()>=2) finishPendingCount(deferredPresents.size()-1);
    deferred->pipelined=deferGenerated;
    if(ui) {
        if(!uiComposite) uiComposite=std::make_shared<DVLSFG::UiComposite>(info.device,info.physicalDevice,extent,swapchainFormat);
        if(!uiComposite->matches(*ui)) finishPending();
        std::vector<VkImage> generated; for(auto& image:out_n) generated.push_back(image.handle());
        uiComposite->prepare(*ui,generated); lastSceneSerial=ui->serial;
    }
    auto started=std::chrono::steady_clock::now();
    // Higher LSFG multipliers retain synchronous resource ownership.
    std::shared_ptr<ImageReadiness> readiness;
    const auto family=info.queue.first;
    auto epoch=DVLSFG::epoch();
    bool seedBoth=frameIdx==0 || lastEpoch!=epoch;
    int sourceLimit=DVLSFG::inputFrameLimit();
    if(seedBoth || sourceLimit!=previousLimit) { previousInput={}; previousOutput={}; inputPeriodSeconds=0; previousPacingSeconds=0; }
    previousLimit=sourceLimit;
    if(previousInput.time_since_epoch().count()) {
        // Exclude our own pacing waits: feeding them back as input cost would
        // progressively reduce an uncapped game's frame rate on every frame.
        auto sample=std::chrono::duration<double>(started-previousInput).count()-previousPacingSeconds;
        if(sample>0 && sample<0.1)
            inputPeriodSeconds=inputPeriodSeconds>0 ? inputPeriodSeconds*0.75+sample*0.25 : sample;
    }
    previousInput=started;
    previousPacingSeconds=0;
    double interval=(sourceLimit>0 ? 1.0/sourceLimit : inputPeriodSeconds)/conf.multiplier;
    // With both presents paced, using exactly the observed source period makes
    // our own waits a stable rate limit after any slow frame. A small amount of
    // headroom lets uncapped pacing converge back to rendering throughput.
    // Explicit game source limits keep their exact N-times presentation period.
    if(deferGenerated && sourceLimit<=0) interval*=pacingHeadroom.load();
    auto pace=[&] {
        if(interval>0 && previousOutput.time_since_epoch().count()) {
            auto deadline=previousOutput+std::chrono::duration_cast<std::chrono::steady_clock::duration>(std::chrono::duration<double>(interval));
            auto waitStart=std::chrono::steady_clock::now();
            waitUntil(deadline);
            previousPacingSeconds+=std::chrono::duration<double>(std::chrono::steady_clock::now()-waitStart).count();
        }
    };
    // The prior call completes this context before recycling its resources.
    // Current input readiness is carried by the application's wait semaphores.
    if(frameIdx==0) DVLSFG::status("First frame: copying input");
    DVLSFG::Readback inputCapture,generatedCapture,overlayCapture,previousSceneCapture,currentSceneCapture;
    bool capture=DVLSFG::takeCapture();
    auto capturedHistory=capture ? outputHistory : std::deque<OutputStamp>{};
    auto noteOutput=[&](bool generated) {recordOutput(previousOutput,generated);};
    if(capture) {
        try {
            inputCapture.create(info.device,info.physicalDevice,extent); generatedCapture.create(info.device,info.physicalDevice,extent);
            if(ui) overlayCapture.create(info.device,info.physicalDevice,extent);
            if(ui && fsr) {previousSceneCapture.create(info.device,info.physicalDevice,extent); currentSceneCapture.create(info.device,info.physicalDevice,extent);}
        }
        catch(const std::exception& e) { capture=false; DVLSFG::status(std::string("Capture allocation failed: ")+e.what()); }
    }
    auto& pass=passInfos[deferGenerated ? frameIdx%passInfos.size() : 0];
    pass=RenderPassInfo{};
    HANDLE inputReady{};
    pass.preCopySemaphores[0]=Mini::Semaphore(info.device,&inputReady);
    Mini::Semaphore actualReady(info.device);
    Mini::Semaphore generatedReady(info.device);
    pass.preCopyBuf=Mini::CommandBuffer(info.device,cmdPool);
    auto& before=pass.preCopyBuf;
    before.begin();
    barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
        VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_TRANSFER_READ_BIT);
    if(capture && !ui) inputCapture.record(before.handle(),swapchainImages.at(index));
    VkImage sceneInput=swapchainImages.at(index);
    if(ui) {
        // Save late Vulkan overlays before replacing the application's real image.
        auto presented=uiComposite->presentedImage();
        barrier(before.handle(),presented,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_READ_BIT,VK_ACCESS_TRANSFER_WRITE_BIT);
        blit(before.handle(),swapchainImages.at(index),presented,extent);
        barrier(before.handle(),presented,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_GENERAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
        for(auto image:{ui->scene,ui->black,ui->white,ui->complete}) barrier(before.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
        sceneInput=uiComposite->record(before.handle(),out_n.size(),true);
        for(auto image:{ui->scene,ui->black,ui->white,ui->complete}) barrier(before.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
    }
    std::array<VkImage,2> frames{frame_0.handle(),frame_1.handle()};
    for(unsigned i=0;i<2;++i) {
        barrier(before.handle(),frames[i],frameIdx ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL,
            frameIdx && !fsr ? VK_QUEUE_FAMILY_EXTERNAL : VK_QUEUE_FAMILY_IGNORED,frameIdx && !fsr ? family : VK_QUEUE_FAMILY_IGNORED,0,VK_ACCESS_TRANSFER_WRITE_BIT);
        if(seedBoth || i==frameIdx%2) {
            barrier(before.handle(),frames[i],VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,0,VK_ACCESS_TRANSFER_WRITE_BIT);
            blit(before.handle(),sceneInput,frames[i],extent);
            barrier(before.handle(),frames[i],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_GENERAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,0);
        }
        barrier(before.handle(),frames[i],VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL,fsr ? VK_QUEUE_FAMILY_IGNORED : family,fsr ? VK_QUEUE_FAMILY_IGNORED : VK_QUEUE_FAMILY_EXTERNAL,VK_ACCESS_TRANSFER_WRITE_BIT,fsr ? VK_ACCESS_SHADER_READ_BIT : 0);
    }
    if(frameIdx==0) for(auto& image:out_n) {
        barrier(before.handle(),image.handle(),VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,0,0);
        if(!fsr) barrier(before.handle(),image.handle(),VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL,family,VK_QUEUE_FAMILY_EXTERNAL,0,0);
    }
    if(ui) {
        // Use the same antialiased UI for the real frame too; alternating native
        // single-sample UI with resolved UI would make its edges shimmer.
        if(capture && fsr) {
            for(unsigned sample=0;sample<2;++sample) {
                auto source=frames[(frameIdx+1-sample)%2];
                barrier(before.handle(),source,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_TRANSFER_READ_BIT);
                barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_TRANSFER_WRITE_BIT);
                blit(before.handle(),source,swapchainImages.at(index),extent);
                barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_TRANSFER_READ_BIT);
                (sample==0 ? previousSceneCapture : currentSceneCapture).record(before.handle(),swapchainImages.at(index));
                barrier(before.handle(),source,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_GENERAL,
                    VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_SHADER_READ_BIT);
            }
        }
        for(auto image:{ui->scene,ui->black,ui->white,ui->complete}) barrier(before.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
        auto real=uiComposite->record(before.handle(),out_n.size(),false,true);
        for(auto image:{ui->scene,ui->black,ui->white,ui->complete}) barrier(before.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
        barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_TRANSFER_WRITE_BIT);
        blit(before.handle(),real,swapchainImages.at(index),extent);
        if(capture) {
            barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_TRANSFER_READ_BIT);
            inputCapture.record(before.handle(),swapchainImages.at(index));
        }
        barrier(before.handle(),swapchainImages.at(index),capture ? VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL : VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,capture ? VK_ACCESS_TRANSFER_READ_BIT : VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_MEMORY_READ_BIT);
    } else barrier(before.handle(),swapchainImages.at(index),VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
        VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT);
    std::vector<HANDLE> ready(conf.multiplier-1);
    for(auto& handle:ready) pass.renderSemaphores.emplace_back(info.device,&handle);
    if(fsr) {
        for(auto image:{ui->depth,ui->motion}) barrier(before.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
        fsrFrameGeneration->dispatch(before.handle(),frames[frameIdx%2],out_n[0].handle(),*ui,seedBoth);
        for(auto image:{ui->depth,ui->motion}) barrier(before.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
        before.end(); before.submit(info.queue.second,waits,{pass.renderSemaphores[0].handle(),actualReady.handle()});
    } else {
    before.end(); before.submit(info.queue.second,waits,{pass.preCopySemaphores[0].handle(),actualReady.handle()});
    if(frameIdx==0) DVLSFG::status("First frame: input submitted");
    if(conf.performance) LSFG_3_1P::presentContext(*lsfgCtxId,inputReady,ready);
    else LSFG_3_1::presentContext(*lsfgCtxId,inputReady,ready);
    }
    if(frameIdx==0) DVLSFG::status("First frame: compute submitted");
    VkResult result=VK_SUCCESS;
    VkResult resizeResult=VK_SUCCESS;
    size_t copiedOutputs=0;
    unsigned presentedOutputs=0;
    for(size_t i=0;i<out_n.size();++i) {
        pass.acquireSemaphores.emplace_back(info.device);
        uint32_t target{};
        const auto acquireStart=std::chrono::steady_clock::now();
        do {
            {std::lock_guard guard(*chainMutex); result=Layer::ovkAcquireNextImageKHR(info.device,swapchain,0,pass.acquireSemaphores.back().handle(),VK_NULL_HANDLE,&target);}
            if(result!=VK_NOT_READY && result!=VK_TIMEOUT) break;
            if(std::chrono::steady_clock::now()-acquireStart>=std::chrono::milliseconds(100)) {result=VK_TIMEOUT; break;}
            std::this_thread::yield();
        } while(true);
        if(result==VK_ERROR_OUT_OF_DATE_KHR || result==VK_ERROR_SURFACE_LOST_KHR || result==VK_TIMEOUT || result==VK_NOT_READY) {
            resizeResult=result==VK_TIMEOUT || result==VK_NOT_READY ? VK_ERROR_OUT_OF_DATE_KHR : result;
            break;
        }
        if(result!=VK_SUCCESS && result!=VK_SUBOPTIMAL_KHR) throw LSFG::vulkan_error(result,"Generated-frame acquire failed");
        if(frameIdx==0) DVLSFG::status("First frame: generated image acquired");
        pass.postCopyBufs.emplace_back(info.device,cmdPool);
        pass.postCopySemaphores.emplace_back(info.device);
        auto& copy=pass.postCopyBufs.back(); copy.begin();
        barrier(copy.handle(),out_n[i].handle(),VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL,fsr ? VK_QUEUE_FAMILY_IGNORED : VK_QUEUE_FAMILY_EXTERNAL,fsr ? VK_QUEUE_FAMILY_IGNORED : family,fsr ? VK_ACCESS_SHADER_WRITE_BIT : 0,ui ? VK_ACCESS_SHADER_READ_BIT : VK_ACCESS_TRANSFER_READ_BIT);
        if(!ui) barrier(copy.handle(),out_n[i].handle(),VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,0,VK_ACCESS_TRANSFER_READ_BIT);
        VkImage generated=out_n[i].handle();
        if(ui) {
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
            generated=uiComposite->record(copy.handle(),i);
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
        }
        barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,0,VK_ACCESS_TRANSFER_WRITE_BIT);
        if(capture && ui && i==0) {
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
            auto mask=uiComposite->record(copy.handle(),i,false,false,true);
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
            blit(copy.handle(),mask,swapchainImages[target],extent);
            barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_TRANSFER_READ_BIT);
            overlayCapture.record(copy.handle(),swapchainImages[target]);
            barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_TRANSFER_WRITE_BIT);
            // The diagnostic reused the compositor output; restore the actual image.
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,image.layout,VK_IMAGE_LAYOUT_GENERAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_MEMORY_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT);
            generated=uiComposite->record(copy.handle(),i);
            for(auto image:{ui->black,ui->white,ui->complete}) barrier(copy.handle(),image.image,VK_IMAGE_LAYOUT_GENERAL,image.layout,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_SHADER_READ_BIT,VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT);
        }
        blit(copy.handle(),generated,swapchainImages[target],extent);
        if(capture && i==0) {
            barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_TRANSFER_READ_BIT);
            generatedCapture.record(copy.handle(),swapchainImages[target]);
            barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,VK_ACCESS_TRANSFER_WRITE_BIT);
        }
        if(!ui) barrier(copy.handle(),out_n[i].handle(),VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_GENERAL,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_READ_BIT,0);
        if(!fsr) barrier(copy.handle(),out_n[i].handle(),VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL,family,VK_QUEUE_FAMILY_EXTERNAL,ui ? VK_ACCESS_SHADER_READ_BIT : VK_ACCESS_TRANSFER_READ_BIT,0);
        barrier(copy.handle(),swapchainImages[target],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
            VK_QUEUE_FAMILY_IGNORED,VK_QUEUE_FAMILY_IGNORED,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_MEMORY_READ_BIT);
        if(deferGenerated) readiness=std::make_shared<ImageReadiness>(info.device,copy.handle());
        copy.end();
        std::vector<VkSemaphore> signals{pass.postCopySemaphores[i].handle()};
        copy.submit(info.queue.second,{pass.acquireSemaphores[i].handle(),pass.renderSemaphores[i].handle()},signals);
        if(deferGenerated) {
            deferred->addedReady=pass.postCopySemaphores[i]; deferred->generatedIndex=target;
            ++copiedOutputs; ++presentedOutputs;
            continue;
        }
        if(async) waitGeneratedImage(info.device,info.queue.second);
        ++copiedOutputs;
        const auto done=pass.postCopySemaphores[i].handle();
        VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
        present.waitSemaphoreCount=1; present.pWaitSemaphores=&done;
        present.swapchainCount=1; present.pSwapchains=&swapchain; present.pImageIndices=&target;
        if(!async) pace();
        previousOutput=std::chrono::steady_clock::now();
        {std::lock_guard guard(*chainMutex); result=Layer::ovkQueuePresentKHR(queue,&present);}
        noteOutput(true);
        if(frameIdx==0) DVLSFG::status("First frame: generated image presented");
        if(result==VK_ERROR_OUT_OF_DATE_KHR || result==VK_ERROR_SURFACE_LOST_KHR) { resizeResult=result; break; }
        if(result!=VK_SUCCESS && result!=VK_SUBOPTIMAL_KHR) throw LSFG::vulkan_error(result,"Generated-frame present failed");
        ++presentedOutputs;
    }
    // Even when resize interrupts the added presents, consume every outstanding
    // compute signal before recycling shared resources. Always reach the real
    // present: DXVK's application present fence/ID must not be silently dropped.
    std::vector<VkSemaphore> pending;
    for(size_t i=copiedOutputs;i<pass.renderSemaphores.size();++i) pending.push_back(pass.renderSemaphores[i].handle());
    std::vector<VkPipelineStageFlags> stages(pending.size(),VK_PIPELINE_STAGE_ALL_COMMANDS_BIT);
    const auto drained=generatedReady.handle();
    VkSubmitInfo drain{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    drain.waitSemaphoreCount=static_cast<uint32_t>(pending.size()); drain.pWaitSemaphores=pending.data(); drain.pWaitDstStageMask=stages.data();
    drain.signalSemaphoreCount=1; drain.pSignalSemaphores=&drained;
    auto submit=reinterpret_cast<PFN_vkQueueSubmit>(Layer::ovkGetDeviceProcAddr(info.device,"vkQueueSubmit"));
    auto drainResult=submit(info.queue.second,1,&drain,VK_NULL_HANDLE);
    if(drainResult!=VK_SUCCESS) throw LSFG::vulkan_error(drainResult,"Frame generation drain failed");
    VkPresentInfoKHR actual{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
    actual.pNext=pNext;
    actual.swapchainCount=1; actual.pSwapchains=&swapchain; actual.pImageIndices=&index;
    // Presentation needs an explicit semaphore dependency on the source layout
    // transition, even when conservative host-side device waits have completed.
    const std::array<VkSemaphore,2> sourceReady{actualReady.handle(),generatedReady.handle()};
    actual.waitSemaphoreCount=2; actual.pWaitSemaphores=sourceReady.data();
    if(async && resizeResult==VK_SUCCESS && presentedOutputs) {
        // All reads are already submitted on DXVK's graphics queue, before its
        // next frame can write these textures. Retain imports until the worker's
        // semaphore waits complete; no device-wide CPU wait is necessary.
        deferred->ready={actualReady,generatedReady};
        deferred->inputs=ui;
        const auto deadline=previousOutput+std::chrono::duration_cast<std::chrono::steady_clock::duration>(std::chrono::duration<double>(interval));
        const auto device=info.device; const auto presentQueue=info.presentQueue; const auto chain=swapchain;
        auto prior=deferredPresents.empty() ? std::shared_ptr<DVLSFG::DeferredPresent>{} : deferredPresents.back();
        auto mutex=chainMutex;
        deferred->worker=std::thread([job=deferred.get(),device,presentQueue,chain,index,deadline,presentedOutputs,deferGenerated,readiness,interval,prior,mutex] {
            // Preserve WSI order and exclusive reserved-queue ownership while
            // up to two source frames render/compute on the graphics queue.
            if(prior) prior->join();
            auto realDeadline=deadline;
            VkResult generatedResult=VK_SUCCESS;
            unsigned generatedCount=presentedOutputs;
            if(deferGenerated) {
                auto ready=readiness->wait();
                if(ready!=VK_SUCCESS) {job->result=ready; job->finished.store(true); return;}
                if(prior && prior->submitted.time_since_epoch().count()) waitUntil(prior->submitted+std::chrono::duration_cast<std::chrono::steady_clock::duration>(std::chrono::duration<double>(interval)));
                const auto semaphore=job->addedReady.handle();
                VkPresentInfoKHR generated{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
                generated.waitSemaphoreCount=1; generated.pWaitSemaphores=&semaphore;
                generated.swapchainCount=1; generated.pSwapchains=&chain; generated.pImageIndices=&job->generatedIndex;
                const auto time=std::chrono::steady_clock::now();
                {std::lock_guard guard(*mutex); generatedResult=Layer::ovkQueuePresentKHR(presentQueue,&generated);}
                if(generatedResult==VK_SUCCESS || generatedResult==VK_SUBOPTIMAL_KHR) {
                    job->generatedSubmitted=time;
                    realDeadline=time+std::chrono::duration_cast<std::chrono::steady_clock::duration>(std::chrono::duration<double>(interval));
                } else {generatedCount=0; realDeadline={};}
            }
            waitUntil(realDeadline);
            const std::array<VkSemaphore,2> semaphores{job->ready[0].handle(),job->ready[1].handle()};
            VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR}; present.pNext=job->metadata.get();
            present.waitSemaphoreCount=2; present.pWaitSemaphores=semaphores.data();
            present.swapchainCount=1; present.pSwapchains=&chain; present.pImageIndices=&index;
            job->submitted=std::chrono::steady_clock::now();
            {std::lock_guard guard(*mutex); job->result=Layer::ovkQueuePresentKHR(presentQueue,&present);}
            // This queue belongs exclusively to this worker. Do not wait the
            // whole device while DXVK is submitting the next scene in parallel.
            auto idle=reinterpret_cast<PFN_vkQueueWaitIdle>(Layer::ovkGetDeviceProcAddr(device,"vkQueueWaitIdle"));
            auto idleResult=idle(presentQueue);
            if(job->result==VK_SUCCESS || job->result==VK_SUBOPTIMAL_KHR) {
                if(idleResult!=VK_SUCCESS) job->result=idleResult;
                else {DVLSFG::countFrame(generatedCount); if(generatedResult!=VK_SUCCESS) job->result=generatedResult;}
            } else if(idleResult==VK_SUCCESS) {
                try {completeRejectedPresentFence(device,presentQueue,job->metadata.get(),true);}
                catch(const std::exception& e) {DVLSFG::status(std::string("Deferred resize completion failed: ")+e.what()); job->result=VK_ERROR_DEVICE_LOST;}
            }
            job->finished.store(true);
        });
        deferredPresents.push_back(deferred);
        if(frameIdx==0) DVLSFG::status(deferGenerated ? "2x bounded two-frame GPU/presentation pipeline" : "2x presentation pacing on reserved queue (overlaps next game render)");
    } else {
        finishPending();
        pace();
        previousOutput=std::chrono::steady_clock::now();
        {std::lock_guard guard(*chainMutex); result=Layer::ovkQueuePresentKHR(queue,&actual);}
        noteOutput(false);
        bool realPresented=result==VK_SUCCESS || result==VK_SUBOPTIMAL_KHR;
        if(!realPresented && result!=VK_ERROR_OUT_OF_DATE_KHR && result!=VK_ERROR_SURFACE_LOST_KHR) throw LSFG::vulkan_error(result,"Real-frame present failed");
        waitIdle(info.device);
        if(!realPresented) completeRejectedPresentFence(info.device,info.queue.second,pNext);
        if(realPresented) DVLSFG::countFrame(presentedOutputs);
    }
    if(resizeResult!=VK_SUCCESS) { DVLSFG::status("Swapchain resize: drained generation and forwarded application present metadata"); result=resizeResult; }
    ++frameIdx;
    DVLSFG::recordFrameTime(std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count());
    lastEpoch=epoch;
    if(frameIdx==1) DVLSFG::status("Frame generation active");
    if(capture && copiedOutputs) {
        try {
            finishPending();
            auto directory=DVLSFG::captureDirectory(); std::filesystem::create_directories(directory);
            auto name="frame-"+std::to_string(GetCurrentProcessId())+"-"+std::to_string(frameIdx)+"-"+std::to_string(GetTickCount64());
            inputCapture.save(directory/(name+"-input.bmp"),swapchainFormat);
            generatedCapture.save(directory/(name+"-generated.bmp"),swapchainFormat);
            if(ui) overlayCapture.save(directory/(name+"-overlay-mask.bmp"),swapchainFormat);
            if(ui && fsr) {
                previousSceneCapture.save(directory/(name+"-previous-scene.bmp"),swapchainFormat);
                currentSceneCapture.save(directory/(name+"-current-scene.bmp"),swapchainFormat);
            }
            if(!capturedHistory.empty()) {
                std::ofstream timings(directory/(name+"-outputs.csv")); timings<<"milliseconds,generated\n";
                for(auto stamp:capturedHistory) timings<<std::chrono::duration<double,std::milli>(stamp.first-capturedHistory.front().first).count()<<','<<stamp.second<<'\n';
            }
            DVLSFG::status("Framebuffer comparison saved in captures/");
        } catch(const std::exception& e) { DVLSFG::status(std::string("Capture failed: ")+e.what()); }
    }
    return result;
}

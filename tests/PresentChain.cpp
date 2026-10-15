#include "present_chain.hpp"
#include <iostream>
void DVLSFG::status(const std::string& text) { std::cout<<text<<'\n'; }
int main() {
    VkPresentModeKHR appMode=VK_PRESENT_MODE_FIFO_KHR;
    uint64_t appId=7;
    VkPresentIdKHR id{VK_STRUCTURE_TYPE_PRESENT_ID_KHR}; id.swapchainCount=1; id.pPresentIds=&appId;
    VkSwapchainPresentModeInfoEXT mode{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODE_INFO_EXT}; mode.swapchainCount=1; mode.pPresentModes=&appMode; mode.pNext=&id;
    VkSwapchainPresentFenceInfoEXT fences{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT}; fences.swapchainCount=1;
    VkFence fence=VK_NULL_HANDLE; fences.pFences=&fence; id.pNext=&fences;
    DVLSFG::PresentChain clone;
    if(!clone.build(&mode)) return 1;
    auto* clonedMode=static_cast<const VkSwapchainPresentModeInfoEXT*>(clone.get());
    auto* clonedId=static_cast<const VkPresentIdKHR*>(clonedMode->pNext);
    auto* clonedFences=static_cast<const VkSwapchainPresentFenceInfoEXT*>(clonedId->pNext);
    if(*clonedMode->pPresentModes!=VK_PRESENT_MODE_IMMEDIATE_KHR || *mode.pPresentModes!=VK_PRESENT_MODE_FIFO_KHR
        || mode.pNext!=&id || id.pNext!=&fences || *clonedId->pPresentIds!=appId || *clonedFences->pFences!=fence || clonedFences->pNext) return 1;
    appId=99;
    if(*clonedId->pPresentIds!=7) return 1;
    VkRectLayerKHR rect{{10,20},{30,40},0};
    VkPresentRegionKHR region{1,&rect};
    VkPresentRegionsKHR regions{VK_STRUCTURE_TYPE_PRESENT_REGIONS_KHR}; regions.swapchainCount=1; regions.pRegions=&region;
    VkPresentTimeGOOGLE time{12,345};
    VkPresentTimesInfoGOOGLE times{VK_STRUCTURE_TYPE_PRESENT_TIMES_INFO_GOOGLE}; times.swapchainCount=1; times.pTimes=&time; times.pNext=&regions;
    DVLSFG::PresentChain retained;
    if(!retained.build(&times)) return 1;
    rect.offset.x=999; time.desiredPresentTime=999;
    auto* copiedTimes=static_cast<const VkPresentTimesInfoGOOGLE*>(retained.get());
    auto* copiedRegions=static_cast<const VkPresentRegionsKHR*>(copiedTimes->pNext);
    if(copiedTimes->pTimes->desiredPresentTime!=345 || copiedRegions->pRegions->pRectangles[0].offset.x!=10) return 1;
    std::cout<<"Present metadata clone passed: combined mode/ID/fence, immutable input, preserved payloads."<<std::endl;
    VkSwapchainPresentModesCreateInfoEXT allowed{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT};
    allowed.presentModeCount=1; allowed.pPresentModes=&appMode;
    VkSwapchainLatencyCreateInfoNV latency{VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV}; latency.latencyModeEnable=VK_TRUE; latency.pNext=&allowed;
    DVLSFG::SwapchainChain createClone;
    if(!createClone.build(&latency)) return 1;
    auto* copiedLatency=static_cast<const VkSwapchainLatencyCreateInfoNV*>(createClone.get());
    auto* copiedAllowed=static_cast<const VkSwapchainPresentModesCreateInfoEXT*>(copiedLatency->pNext);
    if(!copiedLatency->latencyModeEnable || *copiedAllowed->pPresentModes!=VK_PRESENT_MODE_IMMEDIATE_KHR || *allowed.pPresentModes!=VK_PRESENT_MODE_FIFO_KHR || latency.pNext!=&allowed) return 1;
    std::cout<<"Swapchain mode restriction clone passed through NVIDIA latency prefix."<<std::endl;
    return 0;
}

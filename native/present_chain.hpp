#pragma once
#include "platform.hpp"
#include <array>
#include <variant>
#include <vector>
namespace DVLSFG {
// Copy only the prefix through the dynamic-mode restriction. The untouched tail
// remains owned by the application for the duration of vkCreateSwapchainKHR.
class SwapchainChain {
    using Item=std::variant<VkSwapchainPresentModesCreateInfoEXT,VkSwapchainLatencyCreateInfoNV,
        VkImageFormatListCreateInfo,VkSurfaceFullScreenExclusiveInfoEXT,
        VkSurfaceFullScreenExclusiveWin32InfoEXT,VkDeviceGroupSwapchainCreateInfoKHR>;
    std::array<Item,16> items{};
    size_t count=0;
    VkPresentModeKHR mode=VK_PRESENT_MODE_IMMEDIATE_KHR;
    VkBaseOutStructure* head=nullptr;
    VkBaseOutStructure* tail=nullptr;
    template<class T> void append(const VkBaseInStructure* node) {
        items[count]=*reinterpret_cast<const T*>(node);
        auto* copy=std::visit([](auto& value){return reinterpret_cast<VkBaseOutStructure*>(&value);},items[count++]);
        if(tail) tail->pNext=copy; else head=copy;
        tail=copy;
    }
public:
    bool build(const void* chain) {
        auto* first=static_cast<const VkBaseInStructure*>(chain);
        auto* node=first;
        unsigned length=0;
        while(node && node->sType!=VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT) {
            if(++length>16) return false;
            node=node->pNext;
        }
        if(!node) { head=const_cast<VkBaseOutStructure*>(reinterpret_cast<const VkBaseOutStructure*>(chain)); return true; }
        for(node=first;node;node=node->pNext) {
            if(count==items.size()) return false;
            switch(node->sType) {
                case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT: {
                    append<VkSwapchainPresentModesCreateInfoEXT>(node);
                    auto& modes=std::get<VkSwapchainPresentModesCreateInfoEXT>(items[count-1]);
                    modes.presentModeCount=1; modes.pPresentModes=&mode; return true;
                }
                case VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV: append<VkSwapchainLatencyCreateInfoNV>(node); break;
                case VK_STRUCTURE_TYPE_IMAGE_FORMAT_LIST_CREATE_INFO: append<VkImageFormatListCreateInfo>(node); break;
                case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_INFO_EXT: append<VkSurfaceFullScreenExclusiveInfoEXT>(node); break;
                case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_WIN32_INFO_EXT: append<VkSurfaceFullScreenExclusiveWin32InfoEXT>(node); break;
                case VK_STRUCTURE_TYPE_DEVICE_GROUP_SWAPCHAIN_CREATE_INFO_KHR: append<VkDeviceGroupSwapchainCreateInfoKHR>(node); break;
                default: status("Pass-through: unsupported swapchain metadata before mode list "+std::to_string(node->sType)); return false;
            }
        }
        return false;
    }
    const void* get() const { return head; }
};
// Clone per-present metadata so only real frames consume the application's IDs,
// timing, regions and fences. Generated frames carry no application metadata.
class PresentChain {
    using Item=std::variant<VkSwapchainPresentModeInfoEXT,VkPresentIdKHR,
        VkPresentTimesInfoGOOGLE,VkPresentRegionsKHR,VkSwapchainPresentFenceInfoEXT>;
    std::array<Item,16> items{};
    size_t count=0;
    VkPresentModeKHR immediate=VK_PRESENT_MODE_IMMEDIATE_KHR;
    VkBaseOutStructure* head=nullptr;
    VkBaseOutStructure* tail=nullptr;
    uint64_t id{};
    VkPresentTimeGOOGLE time{};
    VkFence fence{};
    VkPresentRegionKHR region{};
    std::vector<VkRectLayerKHR> rectangles;
    template<class T> void append(const VkBaseInStructure* node) {
        items[count]=*reinterpret_cast<const T*>(node);
        auto* copy=std::visit([](auto& value) { return reinterpret_cast<VkBaseOutStructure*>(&value); },items[count++]);
        copy->pNext=nullptr;
        if(tail) tail->pNext=copy; else head=copy;
        tail=copy;
    }
public:
    PresentChain()=default;
    PresentChain(const PresentChain&)=delete;
    PresentChain& operator=(const PresentChain&)=delete;
    bool usesId(uint64_t value) const {return value!=0 && id==value;}
    bool build(const void* chain) {
        unsigned length=0;
        for(auto* node=static_cast<const VkBaseInStructure*>(chain);node;node=node->pNext) {
            if(++length>16) return false;
            switch(node->sType) {
                case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODE_INFO_EXT:
                    append<VkSwapchainPresentModeInfoEXT>(node);
                    if(std::get<VkSwapchainPresentModeInfoEXT>(items[count-1]).swapchainCount!=1) return false;
                    std::get<VkSwapchainPresentModeInfoEXT>(items[count-1]).pPresentModes=&immediate;
                    break;
                case VK_STRUCTURE_TYPE_PRESENT_ID_KHR: {
                    append<VkPresentIdKHR>(node); auto& value=std::get<VkPresentIdKHR>(items[count-1]);
                    if(value.swapchainCount!=1 || !value.pPresentIds) return false;
                    id=*value.pPresentIds; value.pPresentIds=&id; break;
                }
                case VK_STRUCTURE_TYPE_PRESENT_TIMES_INFO_GOOGLE: {
                    append<VkPresentTimesInfoGOOGLE>(node); auto& value=std::get<VkPresentTimesInfoGOOGLE>(items[count-1]);
                    if(value.swapchainCount!=1 || !value.pTimes) return false;
                    time=*value.pTimes; value.pTimes=&time; break;
                }
                case VK_STRUCTURE_TYPE_PRESENT_REGIONS_KHR: {
                    append<VkPresentRegionsKHR>(node); auto& value=std::get<VkPresentRegionsKHR>(items[count-1]);
                    if(value.swapchainCount!=1 || !value.pRegions) return false;
                    region=*value.pRegions;
                    if(region.rectangleCount) {
                        if(!region.pRectangles) return false;
                        rectangles.assign(region.pRectangles,region.pRectangles+region.rectangleCount);
                    }
                    region.pRectangles=rectangles.data(); value.pRegions=&region; break;
                }
                case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT: {
                    append<VkSwapchainPresentFenceInfoEXT>(node); auto& value=std::get<VkSwapchainPresentFenceInfoEXT>(items[count-1]);
                    if(value.swapchainCount!=1 || !value.pFences) return false;
                    fence=*value.pFences; value.pFences=&fence; break;
                }
                default:
                    status("Pass-through: unsupported present metadata " + std::to_string(node->sType));
                    return false;
            }
        }
        return true;
    }
    const void* get() const { return head; }
};
}

#pragma once
#include "scene_ui.hpp"
#include <memory>
namespace DVLSFG {
#ifdef DVSCALING_FSRFG_BUILD
#define DVSCALING_FSRFG_API __declspec(dllexport)
#else
#define DVSCALING_FSRFG_API __declspec(dllimport)
#endif
class DVSCALING_FSRFG_API FsrFrameGeneration {
    struct State;
    std::unique_ptr<State> state;
public:
    static void configureInstance(VkInstance instance,PFN_vkGetInstanceProcAddr proc);
    FsrFrameGeneration(VkDevice device,VkPhysicalDevice physical,VkExtent2D display,bool encoded,PFN_vkGetDeviceProcAddr proc,void (*logger)(const std::string&));
    ~FsrFrameGeneration();
    void dispatch(VkCommandBuffer command,VkImage color,VkImage output,const SceneUiFrame& frame,bool reset);
};
}

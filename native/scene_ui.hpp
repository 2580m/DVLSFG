#pragma once
#include "platform.hpp"
#include <memory>
#include <vector>
namespace DVLSFG {
struct SceneUiFrame {
    struct Image { VkImage image{}; VkImageLayout layout{}; VkFormat format{}; };
    VkDevice device{};
    VkExtent2D extent{};
    Image scene,black,white;
    unsigned long long serial{};
    // Retains the Unity D3D resources until all presentation commands complete.
    std::shared_ptr<void> resources;
};
bool sceneUiRequested();
std::shared_ptr<const SceneUiFrame> acquireSceneUi(VkDevice device,VkExtent2D extent);
class UiComposite {
    struct State;
    std::shared_ptr<State> state;
public:
    UiComposite(VkDevice device,VkPhysicalDevice physical,VkExtent2D extent,VkFormat swapchainFormat);
    void prepare(const SceneUiFrame& frame,const std::vector<VkImage>& generated);
    VkImage record(VkCommandBuffer command,size_t generatedIndex,bool sceneOnly=false);
};
}

#pragma once
#include "present_chain.hpp"
#include "mini/semaphore.hpp"
#include <chrono>
#include <atomic>
#include <memory>
#include <thread>

namespace DVLSFG {
// Only presentation is deferred. All reads of Unity's reusable scene/UI images
// and interpolation work are submitted on the game queue before returning.
// The worker owns a reserved queue, the real swapchain image and its metadata.
struct DeferredPresent {
    PresentChain metadata;
    std::array<Mini::Semaphore,2> ready;
    std::shared_ptr<const void> inputs;
    std::thread worker;
    std::atomic<bool> finished{false};
    VkResult result=VK_SUCCESS;
    std::chrono::steady_clock::time_point submitted{};
    ~DeferredPresent() {join();}
    void join() {if(worker.joinable()) worker.join();}
};
}

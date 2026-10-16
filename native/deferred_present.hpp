#pragma once
#include "present_chain.hpp"
#include "mini/semaphore.hpp"
#include <chrono>
#include <atomic>
#include <memory>
#include <thread>
#include <mutex>

namespace DVLSFG {
// Rendering, interpolation and UI reads are submitted on the graphics queue
// before returning. Workers observe readiness and present in source order on
// the reserved queue, retaining both swapchain images and application metadata.
struct DeferredPresent {
    PresentChain metadata;
    std::array<Mini::Semaphore,2> ready;
    Mini::Semaphore addedReady;
    bool pipelined{};
    uint32_t generatedIndex{};
    std::shared_ptr<const void> inputs;
    std::thread worker;
    std::mutex joinMutex;
    std::atomic<bool> finished{false};
    VkResult result=VK_SUCCESS;
    std::chrono::steady_clock::time_point submitted{};
    std::chrono::steady_clock::time_point generatedSubmitted{};
    ~DeferredPresent() {join();}
    void join() {std::lock_guard guard(joinMutex); if(worker.joinable()) worker.join();}
};
}

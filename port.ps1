$ErrorActionPreference = 'Stop'
$up = Join-Path $PSScriptRoot 'vendor/lsfg-vk'
$port = Join-Path $PSScriptRoot 'build/port'
New-Item -ItemType Directory $port -Force | Out-Null
foreach ($dir in @('include','src','framegen')) { Copy-Item (Join-Path $up $dir) $port -Recurse -Force }
Copy-Item (Join-Path $up 'thirdparty/dxbc') $port -Recurse -Force
# Generate the Windows port from the pinned MIT upstream, preserving original sources.
foreach ($file in Get-ChildItem $port -Recurse -File | Where-Object Extension -in '.cpp','.hpp','.h') {
    $s = [IO.File]::ReadAllText($file.FullName)
    $s = $s.Replace('__attribute__((visibility("default")))','')
    $s = $s.Replace('VK_KHR_external_memory_fd','VK_KHR_external_memory_win32').Replace('VK_KHR_external_semaphore_fd','VK_KHR_external_semaphore_win32')
    foreach ($pair in @(
        @('GetMemoryFdKHR','GetMemoryWin32HandleKHR'), @('GetSemaphoreFdKHR','GetSemaphoreWin32HandleKHR'),
        @('ImportSemaphoreFdKHR','ImportSemaphoreWin32HandleKHR'), @('MemoryGetFdInfoKHR','MemoryGetWin32HandleInfoKHR'),
        @('SemaphoreGetFdInfoKHR','SemaphoreGetWin32HandleInfoKHR'), @('ImportMemoryFdInfoKHR','ImportMemoryWin32HandleInfoKHR'),
        @('ImportSemaphoreFdInfoKHR','ImportSemaphoreWin32HandleInfoKHR'),
        @('MEMORY_GET_FD_INFO_KHR','MEMORY_GET_WIN32_HANDLE_INFO_KHR'), @('SEMAPHORE_GET_FD_INFO_KHR','SEMAPHORE_GET_WIN32_HANDLE_INFO_KHR'),
        @('IMPORT_MEMORY_FD_INFO_KHR','IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR'), @('IMPORT_SEMAPHORE_FD_INFO_KHR','IMPORT_SEMAPHORE_WIN32_HANDLE_INFO_KHR'),
        @('OPAQUE_FD_BIT','OPAQUE_WIN32_BIT'))) { $s = $s.Replace($pair[0],$pair[1]) }
    $s = $s.Replace('std::vector<int>','std::vector<HANDLE>').Replace('std::array<int, 2>','std::array<HANDLE, 2>')
    $s = [regex]::Replace($s,'\bint(\*)? (fd|pFd|in0|in1|inSem|preCopySemaphoreFd)\b','HANDLE$1 $2')
    $s = $s.Replace('.fd = fd // closes the fd','.handle = fd')
    $s = $s.Replace('*fd < 0','!*fd').Replace('fd == -1','fd == nullptr').Replace('inSem >= 0','inSem != nullptr').Replace('inSem < 0','inSem == nullptr')
    $s = $s.Replace('fds.empty() ? -1','fds.empty() ? nullptr').Replace('outSem.empty() ? -1','outSem.empty() ? nullptr')
    # Imported Win32 handles remain owned by this process; close them after import.
    if ($file.FullName -match 'framegen\\src\\core\\image.cpp$') {
        $shared = $s.IndexOf('// shared memory constructor')
        $s = $s.Substring(0,$shared) + $s.Substring($shared).Replace('.usage = usage,','.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT,')
        $shared = $s.IndexOf('// shared memory constructor')
        $s = $s.Substring(0,$shared) + $s.Substring($shared).Replace('std::make_shared<VkImageLayout>(VK_IMAGE_LAYOUT_UNDEFINED)','std::make_shared<VkImageLayout>(VK_IMAGE_LAYOUT_GENERAL)')
    }
    # Exporters own Win32 HANDLEs; import does not transfer their ownership.
    if ($file.FullName -match 'src\\context.cpp$' -and $file.FullName -notmatch 'framegen') {
        $s = $s.Replace('setenv("DISABLE_LSFG", "1", 1);','DVLSFG::BackendScope backendScope;')
        $s = $s.Replace('unsetenv("DISABLE_LSFG");','')
    }
    if ($file.FullName -match 'src\\mini\\image.cpp$') {
        $s = $s.Replace('.usage = usage,','.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT,')
    }
    if ($file.FullName -match 'src\\mini\\(image|semaphore).cpp$') {
        $s = $s.Replace('    // store objects in shared ptr','    exportedHandle = std::shared_ptr<void>(*fd, [](void* h) { CloseHandle(h); });' + "`n    // store objects in shared ptr")
        # Only the export constructor has fd; the regular semaphore constructor does not.
        if ($file.Name -eq 'semaphore.cpp') {
            $s = $s.Replace('    // store semaphore in shared ptr','    exportedHandle = std::shared_ptr<void>(*fd, [](void* h) { CloseHandle(h); });' + "`n    // store semaphore in shared ptr")
            $position = $s.IndexOf('    exportedHandle =')
            $s = $s.Remove($position,'    exportedHandle = std::shared_ptr<void>(*fd, [](void* h) { CloseHandle(h); });'.Length)
        }
    }
    if ($file.FullName -match 'include\\mini\\(image|semaphore).hpp$') {
        $s = $s.Replace('    private:', '    private:' + "`n        std::shared_ptr<void> exportedHandle;")
    }
    if ($file.FullName -match 'include\\core\\image.hpp$|include\\mini\\image.hpp$') {
        $s = $s.Replace('std::shared_ptr<VkImage> image;' + "`n        std::shared_ptr<VkDeviceMemory> memory;",'std::shared_ptr<VkDeviceMemory> memory;' + "`n        std::shared_ptr<VkImage> image;")
    }
    if ($file.FullName -match 'src\\layer.cpp$') {
        $s = '#include "fsr_framegen.hpp"' + "`n" + $s
        $s = $s.Replace('std::cerr << "lsfg-vk: Vulkan instance layer initialized successfully.', 'DVLSFG::FsrFrameGeneration::configureInstance(*pInstance, next_vkGetInstanceProcAddr);' + "`n            std::cerr << " + '"lsfg-vk: Vulkan instance layer initialized successfully.')
    }
    if ($file.FullName -match 'include\\context.hpp$' -and $file.FullName -notmatch 'framegen') {
        $s = $s.Replace('VkExtent2D extent, const std::vector<VkImage>& swapchainImages);','VkExtent2D extent, const std::vector<VkImage>& swapchainImages, VkFormat swapchainFormat);')
        $s = $s.Replace('~LsContext() = default;','~LsContext() noexcept;')
        $s = $s.Replace('    VkSwapchainKHR swapchain;', '    VkDevice storedDevice{};' + "`n    unsigned long long lastEpoch{};`n    std::chrono::steady_clock::time_point previousInput{}, previousOutput{};`n    double inputPeriodSeconds{}, previousPacingSeconds{};`n    int previousLimit{};`n    VkFormat swapchainFormat{};`n    VkSwapchainKHR swapchain;")
        $s = $s.Replace('    VkDevice storedDevice{};', '    unsigned long long lastSceneSerial{}; std::shared_ptr<DVLSFG::UiComposite> uiComposite;' + "`n    VkDevice storedDevice{};")
        $s = $s.Replace('    VkDevice storedDevice{};', '    std::shared_ptr<DVLSFG::FsrFrameGeneration> fsrFrameGeneration;' + "`n    VkDevice storedDevice{};")
        $s = '#include <chrono>' + "`n" + '#include "scene_ui.hpp"' + "`n" + '#include "fsr_framegen.hpp"' + "`n" + $s
    }
    if ($file.FullName -match 'framegen\\v3\.1p?_include\\.*\\shaders\\generate.hpp$') {
        $s = $s.Replace('    private:', '        const std::vector<Core::Image>& sharedOutputs() const { return outImgs; }' + "`n    private:")
    }
    if ($file.FullName -match 'framegen\\v3\.1p?_src\\context.cpp$') {
        $s = $s.Replace('this->mipmaps.Dispatch(data.cmdBuffer1, this->frameIdx);', 'DVLSFG::externalBarrier(data.cmdBuffer1.handle(), inImg_0.handle(), vk.device.getComputeFamilyIdx(), true);' + "`n    DVLSFG::externalBarrier(data.cmdBuffer1.handle(), inImg_1.handle(), vk.device.getComputeFamilyIdx(), true);`n    this->mipmaps.Dispatch(data.cmdBuffer1, this->frameIdx);")
        $s = $s.Replace('this->generate.Dispatch(buf2, this->frameIdx, pass);', 'DVLSFG::externalBarrier(buf2.handle(), generate.sharedOutputs().at(pass).handle(), vk.device.getComputeFamilyIdx(), true);' + "`n        this->generate.Dispatch(buf2, this->frameIdx, pass);`n        DVLSFG::externalBarrier(buf2.handle(), generate.sharedOutputs().at(pass).handle(), vk.device.getComputeFamilyIdx(), false);`n        if (pass + 1 == vk.generationCount) {`n            DVLSFG::externalBarrier(buf2.handle(), inImg_0.handle(), vk.device.getComputeFamilyIdx(), false);`n            DVLSFG::externalBarrier(buf2.handle(), inImg_1.handle(), vk.device.getComputeFamilyIdx(), false);`n        }")
    }
    if ($file.FullName -match 'framegen\\v3\.1p?_src\\lsfg.cpp$') {
        # Reset a partially initialized backend too, so failed live rebuilds can recover.
        $s = [regex]::Replace($s,'void (LSFG_3_1P?)::finalize\(\) \{[\s\S]*?\n\}', {
            param($m)
            'void ' + $m.Groups[1].Value + "::finalize() {`n    if (device.has_value()) vkDeviceWaitIdle(device->device.handle());`n    contexts.clear();`n    device.reset();`n    instance.reset();`n}"
        })
    }
    # Vulkan objects must be destroyed before the memory backing them; shared_ptr also owns its wrapper.
    if ($file.FullName -match '(framegen\\src\\core|src\\mini)\\.*\.cpp$') {
        $s = [regex]::Replace($s,'((?:vk|Layer::ovk)\w+\([^;]+;)(\s*)(\}\s*\);)', '$1$2delete ' + '$' + 'PLACEHOLDER;$2$3')
        # Use each lambda argument rather than a fixed name.
        $s = [regex]::Replace($s,'\[([^\]]*)\]\((Vk\w+)\* (\w+)\) \{([^}]+)\}', { param($m) $m.Value.Replace('$PLACEHOLDER',$m.Groups[3].Value) })
    }
    if ($file.FullName -match 'framegen\\src\\core\\instance.cpp$') {
        # Bypass our layer for the private backend instance using the system loader.
        $s = $s.Replace('volkInitialize();','if (volkInitialize() != VK_SUCCESS) throw std::runtime_error("Vulkan loader unavailable");')
        $s = $s.Replace('#include <cstdint>','#include <cstdint>' + "`n#include <stdexcept>")
    }
    if ($file.FullName -match 'dxbc\\include\\util\\util_likely.h$') {
        $s = "#pragma once`n#define likely(x) bool(x)`n#define unlikely(x) bool(x)`n#define force_inline __forceinline`n"
    }
    if ($file.FullName -match 'src\\utils\\utils.cpp$') {
        $s = $s.Replace('#include <sys/types.h>','').Replace('#include <unistd.h>','')
        $s = $s.Substring(0,$s.IndexOf('std::pair<std::string, std::string> Utils::getProcessName()')) + @'
std::pair<std::string, std::string> Utils::getProcessName() {
    wchar_t name[32768]{}; GetModuleFileNameW(nullptr, name, 32768);
    const auto path = std::filesystem::path(name);
    return { path.string(), path.filename().string() };
}
std::string Utils::getConfigFile() {
    const char* file = std::getenv("DVLSFG_CONFIG");
    return file ? file : "";
}
'@
        $s = $s.Replace('#include <array>','#include <array>' + "`n#include <filesystem>")
    }
    if ($file.FullName -match 'src\\layer.cpp$') {
        $s = $s.Replace('    PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR next_vkGetPhysicalDeviceSurfaceCapabilitiesKHR{};', '    PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR next_vkGetPhysicalDeviceSurfaceCapabilitiesKHR{};' + "`n    PFN_vkGetPhysicalDeviceSurfacePresentModesKHR next_vkGetPhysicalDeviceSurfacePresentModesKHR{};")
        $s = $s.Replace('"vkGetPhysicalDeviceSurfaceCapabilitiesKHR", &next_vkGetPhysicalDeviceSurfaceCapabilitiesKHR);', '"vkGetPhysicalDeviceSurfaceCapabilitiesKHR", &next_vkGetPhysicalDeviceSurfaceCapabilitiesKHR);' + "`n            success &= initInstanceFunc(*pInstance, `"vkGetPhysicalDeviceSurfacePresentModesKHR`", &next_vkGetPhysicalDeviceSurfacePresentModesKHR);")
        $s = $s.Replace('    VkResult ovkCreateSwapchainKHR(', "    VkResult ovkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint32_t* count, VkPresentModeKHR* modes) {`n        return next_vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface, count, modes);`n    }`n    VkResult ovkCreateSwapchainKHR(")
        $s = $s.Replace('// prepare layer | NOLINTBEGIN','DVLSFG::initialize();' + "`n            // prepare layer | NOLINTBEGIN")
    }
    if ($file.FullName -match 'include\\layer.hpp$') {
        $s = $s.Replace('    VkResult ovkCreateSwapchainKHR(', "    VkResult ovkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint32_t* count, VkPresentModeKHR* modes);`n    VkResult ovkCreateSwapchainKHR(")
    }
    if ($file.FullName -match 'src\\hooks.cpp$') {
        $s = '#include "present_chain.hpp"' + "`n" + $s
        $s = '#include <mutex>' + "`n" + $s
        $s = $s.Replace('namespace {', 'namespace {' + "`n    std::recursive_mutex framegenLock;")
        # Never cap generated output to refresh rate. If IMMEDIATE is unavailable,
        # leave the application's swapchain alone and report frame generation unavailable.
        $s = $s.Replace('        // increase amount of images in swapchain', @'
        uint32_t modeCount=0;
        if (Layer::ovkGetPhysicalDeviceSurfacePresentModesKHR(deviceInfo.physicalDevice, pCreateInfo->surface, &modeCount, nullptr) != VK_SUCCESS)
            return Layer::ovkCreateSwapchainKHR(device, pCreateInfo, pAllocator, pSwapchain);
        std::vector<VkPresentModeKHR> modes(modeCount);
        if (Layer::ovkGetPhysicalDeviceSurfacePresentModesKHR(deviceInfo.physicalDevice, pCreateInfo->surface, &modeCount, modes.data()) != VK_SUCCESS ||
            std::find(modes.begin(), modes.end(), VK_PRESENT_MODE_IMMEDIATE_KHR) == modes.end()) {
            DVLSFG::status("Frame generation unavailable: surface does not support unsynchronized IMMEDIATE presentation");
            return Layer::ovkCreateSwapchainKHR(device, pCreateInfo, pAllocator, pSwapchain);
        }
        DVLSFG::status("Presentation mode: IMMEDIATE (no refresh-rate cap)");
        // increase amount of images in swapchain
'@)
        # Clone swapchain metadata; DXVK may restrict dynamically selectable modes.
        $s = $s.Replace('        // retire potential old swapchain', @'
        DVLSFG::SwapchainChain swapchainChain;
        if(!swapchainChain.build(createInfo.pNext)) return Layer::ovkCreateSwapchainKHR(device,pCreateInfo,pAllocator,pSwapchain);
        createInfo.pNext=swapchainChain.get();
        // retire potential old swapchain
'@)
        $s = $s.Replace('                swapchainImages' + "`n", '                swapchainImages, pCreateInfo->imageFormat' + "`n")
        $s = $s.Replace('std::unordered_map<VkDevice, DeviceInfo> deviceToInfo;', 'std::unordered_map<VkDevice, DeviceInfo> deviceToInfo;' + "`n    VkDevice framegenOwner{};")
        $s = $s.Replace('deviceToInfo.erase(device);', 'if (framegenOwner == device) {' + "`n            DVLSFG::finalizeBackend(); framegenOwner = VK_NULL_HANDLE;`n        }`n        deviceToInfo.erase(device);")
        $s = $s.Replace('// find device' + "`n", 'if (framegenOwner && framegenOwner != device) return Layer::ovkCreateSwapchainKHR(device, pCreateInfo, pAllocator, pSwapchain);' + "`n        // find device`n")
        $s = $s.Replace('            swapchains.emplace(*pSwapchain, LsContext(', '            framegenOwner = device;' + "`n            swapchains.emplace(*pSwapchain, LsContext(")
        $s = $s.Replace('Utils::logLimitN("swapCtxCreate", 5,', 'DVLSFG::status(std::string("Frame generation unavailable: ") + e.what());' + "`n            " + 'Utils::logLimitN("swapCtxCreate", 5,')
        $s = $s.Replace('// find device' + "`n", 'if (pCreateInfo->imageArrayLayers != 1 || pCreateInfo->imageSharingMode != VK_SHARING_MODE_EXCLUSIVE ||' + "`n            (pCreateInfo->imageFormat != VK_FORMAT_B8G8R8A8_UNORM && pCreateInfo->imageFormat != VK_FORMAT_B8G8R8A8_SRGB &&`n             pCreateInfo->imageFormat != VK_FORMAT_R8G8B8A8_UNORM && pCreateInfo->imageFormat != VK_FORMAT_R8G8B8A8_SRGB))`n            return Layer::ovkCreateSwapchainKHR(device, pCreateInfo, pAllocator, pSwapchain);`n        // find device`n")
        $s = $s.Replace('if (conf.multiplier <= 1)','if (!DVLSFG::isEnabled() || conf.multiplier <= 1)')
        $s = $s.Replace('return VK_ERROR_INITIALIZATION_FAILED;' + "`n        }`n        if (pPresentInfo", 'DVLSFG::status(std::string("Frame generation failed: ") + e.what());' + "`n            return VK_ERROR_INITIALIZATION_FAILED;`n        }`n        if (pPresentInfo")
        $s = $s.Replace('// find swapchain device', @'
if (pPresentInfo->swapchainCount != 1) return Layer::ovkQueuePresentKHR(queue, pPresentInfo);
        VkPresentInfoKHR safeInfo = *pPresentInfo;
        DVLSFG::PresentChain chain;
        if (!chain.build(pPresentInfo->pNext)) return Layer::ovkQueuePresentKHR(queue, pPresentInfo);
        safeInfo.pNext = chain.get();
        pPresentInfo = &safeInfo;
        // find swapchain device
'@)
        $begin = $s.IndexOf('        // enforce present mode | NOLINTBEGIN')
        $end = $s.IndexOf('        // NOLINTEND | present the next frame', $begin)
        $s = $s.Remove($begin,$end-$begin)
        $s = $s.Replace('return res;' + "`n    }`n`n    /// Erase the swapchain", 'if (pPresentInfo->pResults) pPresentInfo->pResults[0] = res;' + "`n        return res;`n    }`n`n    /// Erase the swapchain")
        # All native changes occur under the hook lock, never on Unity's settings thread.
        $s = $s.Replace('        if (framegenOwner &&', '        std::lock_guard<std::recursive_mutex> guard(framegenLock);' + "`n        if (framegenOwner &&")
        $s = $s.Replace('        if (pPresentInfo->swapchainCount != 1)', '        std::lock_guard<std::recursive_mutex> guard(framegenLock);' + "`n        if (pPresentInfo->swapchainCount != 1)")
        $s = $s.Replace('        swapchains.erase(swapchain);', '        std::lock_guard<std::recursive_mutex> guard(framegenLock);' + "`n        swapchainRecipes.erase(swapchain);`n        swapchains.erase(swapchain);")
        $s = $s.Replace('        if (framegenOwner == device)', '        std::lock_guard<std::recursive_mutex> guard(framegenLock);' + "`n        if (framegenOwner == device)")
        $s = $s.Replace('        deviceToInfo.emplace(*pDevice', '        std::lock_guard<std::recursive_mutex> guard(framegenLock);' + "`n        deviceToInfo.emplace(*pDevice")
        $s = $s.Replace('            swapchains.erase(pCreateInfo->oldSwapchain);', '            swapchainRecipes.erase(pCreateInfo->oldSwapchain);' + "`n            swapchainToPresent.erase(pCreateInfo->oldSwapchain);`n            swapchains.erase(pCreateInfo->oldSwapchain);")
        $s = $s.Replace('            framegenOwner = device;', '            swapchainRecipes[*pSwapchain] = { device, pCreateInfo->imageExtent, swapchainImages, pCreateInfo->imageFormat };' + "`n            framegenOwner = device;")
        $s = $s.Replace('    std::unordered_map<VkSwapchainKHR, VkPresentModeKHR> swapchainToPresent;', @'
    std::unordered_map<VkSwapchainKHR, VkPresentModeKHR> swapchainToPresent;
    struct SwapchainRecipe { VkDevice device; VkExtent2D extent; std::vector<VkImage> images; VkFormat format; };
    std::unordered_map<VkSwapchainKHR, SwapchainRecipe> swapchainRecipes;

    bool applyGenerationRequest() noexcept {
        DVLSFG::GenerationSettings next{};
        if (!DVLSFG::takeGenerationRequest(next)) return true;
        auto& conf=Config::activeConf;
        const DVLSFG::GenerationSettings previous{static_cast<int>(conf.multiplier),conf.performance,conf.flowScale};
        if (next.multiplier==previous.multiplier && next.performance==previous.performance && next.flowScale==previous.flowScale && !swapchains.empty()) {
            DVLSFG::generationApplied(previous,true); return true;
        }
        auto rebuild = [](const DVLSFG::GenerationSettings& settings) {
            // Context destruction waits both the game and private compute devices idle.
            swapchains.clear();
            DVLSFG::finalizeBackend();
            Config::activeConf.multiplier = settings.multiplier;
            Config::activeConf.performance = settings.performance;
            Config::activeConf.flowScale = settings.flowScale;
            for (const auto& [handle, recipe] : swapchainRecipes) {
                auto info = deviceToInfo.find(recipe.device);
                if (info == deviceToInfo.end()) throw std::runtime_error("Multiplier rebuild device missing");
                swapchains.emplace(handle, LsContext(info->second, handle, recipe.extent, recipe.images, recipe.format));
            }
        };
        DVLSFG::status("Rebuilding frame generation: " + std::to_string(next.multiplier) + "x, " +
            (next.performance ? "performance" : "standard") + ", flow=" + std::to_string(next.flowScale));
        try {
            rebuild(next);
            DVLSFG::generationApplied(next,true);
            DVLSFG::status("Live generation settings applied; history reset");
            return true;
        } catch (const std::exception& e) {
            const std::string failure=e.what();
            try { rebuild(previous); }
            catch (const std::exception& rollback) {
                swapchains.clear(); DVLSFG::finalizeBackend();
                Config::activeConf.multiplier=previous.multiplier;
                Config::activeConf.performance=previous.performance;
                Config::activeConf.flowScale=previous.flowScale;
                DVLSFG::status("Multiplier rebuild and recovery failed: " + failure + "; " + rollback.what());
                DVLSFG::generationApplied(previous,false);
                return false;
            }
            DVLSFG::generationApplied(previous,false);
            DVLSFG::status("Generation change failed; restored previous settings: " + failure);
            return true;
        }
    }
'@)
        $s = $s.Replace('        // find swapchain context', '        if (!applyGenerationRequest()) return Layer::ovkQueuePresentKHR(queue, pPresentInfo);' + "`n        // find swapchain context")
        # Constants link outside this TU for the DLL probe.
    }
    if ($file.FullName -match 'src\\extract\\extract.cpp$') {
        $s = $s.Replace('const std::unordered_map<std::string, uint32_t> nameIdxTable','extern const std::unordered_map<std::string, uint32_t> nameIdxTable')
    }
    [IO.File]::WriteAllText($file.FullName,$s,[Text.UTF8Encoding]::new($false))
}
# Use Windows data-only PE resource reads rather than executing Lossless.dll.
$extract = Join-Path $port 'src/extract/extract.cpp'
$s = [IO.File]::ReadAllText($extract).Replace('#include <pe-parse/parse.h>','#include <windows.h>')
$start = $s.IndexOf('    int on_resource(')
$end = $s.IndexOf("`n}", $start)
$s = $s.Substring(0,$start) + $s.Substring($end)
$start = $s.IndexOf('    // parse the dll')
$end = $s.IndexOf('    // ensure all shaders', $start)
$s = $s.Substring(0,$start) + @'
    const auto path = std::filesystem::u8path(Config::activeConf.dll);
    HMODULE dll = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
    if (!dll) throw std::runtime_error("Cannot read Lossless.dll resources");
    std::unordered_map<uint32_t, std::vector<uint8_t>> loaded;
    for (const auto& [name, idx] : nameIdxTable) {
        HRSRC res = FindResourceW(dll, MAKEINTRESOURCEW(idx), MAKEINTRESOURCEW(10));
        if (!res) { FreeLibrary(dll); throw std::runtime_error("Missing shader resource: " + name + "; requires compatible LSFG 3.1 DLL"); }
        DWORD size = SizeofResource(dll, res);
        const auto* data = static_cast<const uint8_t*>(LockResource(LoadResource(dll, res)));
        if (!data || !size) { FreeLibrary(dll); throw std::runtime_error("Empty shader resource: " + name); }
        loaded[idx] = std::vector<uint8_t>(data, data + size);
    }
    FreeLibrary(dll);
    shaders() = std::move(loaded);

'@ + $s.Substring($end)
[IO.File]::WriteAllText($extract,$s,[Text.UTF8Encoding]::new($false))

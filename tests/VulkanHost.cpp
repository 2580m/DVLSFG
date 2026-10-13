#define VK_USE_PLATFORM_WIN32_KHR
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <vulkan/vulkan.h>
#include <vector>
#include <stdexcept>
#include <iostream>
#include <string>
#include <cmath>
#include <chrono>
#include <thread>
#include <atomic>
#include <fstream>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
// Isolate this test executable from RTSS's DLL-injected overlay. Disabling
// implicit Vulkan layers alone does not stop that separate injection path.
// This opt-out is test-only; the game and shipped mod do not export it.
extern "C" { __declspec(dllexport) DWORD RTSSHooksCompatibility = 0; }
static unsigned errors=0;
static VKAPI_ATTR VkBool32 VKAPI_CALL debug(VkDebugUtilsMessageSeverityFlagBitsEXT level,VkDebugUtilsMessageTypeFlagsEXT,const VkDebugUtilsMessengerCallbackDataEXT* data,void*) {
    if(level>=VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) ++errors;
    std::cerr<<data->pMessage<<'\n'; return VK_FALSE;
}
static void check(VkResult r) { if(r!=VK_SUCCESS && r!=VK_SUBOPTIMAL_KHR) throw std::runtime_error("Vulkan result " + std::to_string(r)); }
int main(int argc,char** argv) {
    try {
        bool toggle=argc>1 && std::string(argv[1])=="--toggle";
        bool recreate=argc>1 && std::string(argv[1])=="--recreate";
        bool metadata=argc>1 && std::string(argv[1])=="--metadata";
        bool resizePresent=argc>1 && std::string(argv[1])=="--resize-present";
        if(resizePresent) metadata=true;
        bool capture=argc>1 && std::string(argv[1])=="--capture";
        bool secondary=argc>1 && std::string(argv[1])=="--secondary";
        unsigned frameCount=12;
        double inputLimit=0;
        bool visible=false;
        bool displayProof=false;
        for(int arg=1;arg<argc;++arg) {
            std::string value=argv[arg];
            if(value.starts_with("--frames=")) frameCount=std::stoul(value.substr(9));
            if(value.starts_with("--rate=")) inputLimit=std::stod(value.substr(7));
            if(value=="--visible") visible=true;
            if(value=="--display-proof") { displayProof=true; visible=true; }
        }
        HINSTANCE module=GetModuleHandleW(nullptr);
        WNDCLASSW cls{}; cls.lpfnWndProc=DefWindowProcW; cls.hInstance=module; cls.lpszClassName=L"DVLSFGTest";
        RegisterClassW(&cls);
        HWND window=CreateWindowW(cls.lpszClassName,L"DVLSFG hidden test",WS_OVERLAPPEDWINDOW,0,0,640,480,nullptr,nullptr,module,nullptr);
        if(!window) throw std::runtime_error("Hidden window creation failed");
        if(visible) {
            // STARTUPINFO may force the first ShowWindow call to remain hidden.
            ShowWindow(window,SW_SHOWNOACTIVATE); ShowWindow(window,SW_SHOWNOACTIVATE);
            SetWindowPos(window,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
        }
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO}; app.pApplicationName="DVLSFG Test"; app.apiVersion=VK_API_VERSION_1_3;
        const char* ext[]={"VK_KHR_surface","VK_KHR_win32_surface","VK_EXT_debug_utils","VK_KHR_get_surface_capabilities2","VK_EXT_surface_maintenance1"};
        VkInstanceCreateInfo inst{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO}; inst.pApplicationInfo=&app; inst.enabledExtensionCount=metadata?5:3; inst.ppEnabledExtensionNames=ext;
        VkInstance instance; check(vkCreateInstance(&inst,nullptr,&instance));
        VkDebugUtilsMessengerCreateInfoEXT dbg{VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT};
        dbg.messageSeverity=VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT|VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT;
        dbg.messageType=7; dbg.pfnUserCallback=debug;
        VkDebugUtilsMessengerEXT messenger;
        check(reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(vkGetInstanceProcAddr(instance,"vkCreateDebugUtilsMessengerEXT"))(instance,&dbg,nullptr,&messenger));
        VkWin32SurfaceCreateInfoKHR surfaceInfo{VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR}; surfaceInfo.hinstance=module; surfaceInfo.hwnd=window;
        VkSurfaceKHR surface; check(vkCreateWin32SurfaceKHR(instance,&surfaceInfo,nullptr,&surface));
        uint32_t n=0; check(vkEnumeratePhysicalDevices(instance,&n,nullptr)); std::vector<VkPhysicalDevice> physicals(n); check(vkEnumeratePhysicalDevices(instance,&n,physicals.data()));
        VkPhysicalDevice physical=physicals.at(0);
        VkPhysicalDeviceProperties props; vkGetPhysicalDeviceProperties(physical,&props); std::cout<<props.deviceName<<'\n';
        uint32_t count=0; vkGetPhysicalDeviceQueueFamilyProperties(physical,&count,nullptr); std::vector<VkQueueFamilyProperties> families(count); vkGetPhysicalDeviceQueueFamilyProperties(physical,&count,families.data());
        uint32_t family=0; for(;family<count;++family) { VkBool32 supported=0; check(vkGetPhysicalDeviceSurfaceSupportKHR(physical,family,surface,&supported)); if(supported && (families[family].queueFlags&VK_QUEUE_GRAPHICS_BIT)) break; }
        if(family==count) throw std::runtime_error("No graphics/present queue");
        float priority=1.f; VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO}; queueInfo.queueFamilyIndex=family; queueInfo.queueCount=1; queueInfo.pQueuePriorities=&priority;
        const char* deviceExt[]={"VK_KHR_swapchain","VK_EXT_swapchain_maintenance1","VK_KHR_present_id"}; VkDeviceCreateInfo devInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO}; devInfo.queueCreateInfoCount=1; devInfo.pQueueCreateInfos=&queueInfo; devInfo.enabledExtensionCount=metadata?3:1; devInfo.ppEnabledExtensionNames=deviceExt;
        VkPhysicalDevicePresentIdFeaturesKHR idFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR}; idFeature.presentId=VK_TRUE;
        VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT maintenanceFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_EXT}; maintenanceFeature.swapchainMaintenance1=VK_TRUE; maintenanceFeature.pNext=&idFeature;
        if(metadata) devInfo.pNext=&maintenanceFeature;
        VkDevice device; check(vkCreateDevice(physical,&devInfo,nullptr,&device)); VkQueue queue; vkGetDeviceQueue(device,family,0,&queue);
        VkSurfaceCapabilitiesKHR caps; check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical,surface,&caps));
        uint32_t formatCount=0; check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical,surface,&formatCount,nullptr)); std::vector<VkSurfaceFormatKHR> formats(formatCount); check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical,surface,&formatCount,formats.data()));
        auto format=formats.at(0); for(auto f:formats) if(f.format==VK_FORMAT_B8G8R8A8_UNORM) format=f;
        VkExtent2D size=caps.currentExtent; if(size.width==UINT32_MAX) size={640,480};
        VkSwapchainCreateInfoKHR swapInfo{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR}; swapInfo.surface=surface; swapInfo.minImageCount=caps.minImageCount+1; swapInfo.imageFormat=format.format; swapInfo.imageColorSpace=format.colorSpace; swapInfo.imageExtent=size; swapInfo.imageArrayLayers=1; swapInfo.imageUsage=VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT; swapInfo.imageSharingMode=VK_SHARING_MODE_EXCLUSIVE; swapInfo.preTransform=caps.currentTransform; swapInfo.compositeAlpha=VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR; swapInfo.presentMode=VK_PRESENT_MODE_FIFO_KHR; swapInfo.clipped=VK_TRUE;
        VkPresentModeKHR advertisedModes[]={VK_PRESENT_MODE_FIFO_KHR};
        if(displayProof) swapInfo.presentMode=VK_PRESENT_MODE_IMMEDIATE_KHR;
        VkSwapchainPresentModesCreateInfoEXT modesInfo{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT}; modesInfo.presentModeCount=1; modesInfo.pPresentModes=advertisedModes;
        if(metadata) swapInfo.pNext=&modesInfo;
        VkSwapchainKHR swapchain; check(vkCreateSwapchainKHR(device,&swapInfo,nullptr,&swapchain));
        uint32_t imagesCount=0; check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,nullptr)); std::vector<VkImage> images(imagesCount); check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,images.data()));
        VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO}; poolInfo.queueFamilyIndex=family; poolInfo.flags=VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        VkCommandPool pool; check(vkCreateCommandPool(device,&poolInfo,nullptr,&pool));
        VkCommandBufferAllocateInfo bufferInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO}; bufferInfo.commandPool=pool; bufferInfo.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY; bufferInfo.commandBufferCount=1;
        VkCommandBuffer buffer; check(vkAllocateCommandBuffers(device,&bufferInfo,&buffer));
        VkSemaphoreCreateInfo semInfo{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO}; VkSemaphore acquired,ready; check(vkCreateSemaphore(device,&semInfo,nullptr,&acquired)); check(vkCreateSemaphore(device,&semInfo,nullptr,&ready));
        auto testStart=std::chrono::steady_clock::now();
        auto nativeModule=GetModuleHandleW(L"DVLSFG.Native.dll");
        using SetLimit=void(*)(int);
        auto setLimit=nativeModule ? reinterpret_cast<SetLimit>(GetProcAddress(nativeModule,"DVLSFG_SetInputFrameLimit")) : nullptr;
        if(setLimit) setLimit(static_cast<int>(inputLimit));
        struct PixelSample { double milliseconds; unsigned red,green,blue; };
        std::vector<PixelSample> screenPixels;
        std::atomic<bool> sampling{displayProof};
        std::thread sampler;
        if(displayProof) {
            RECT client{}; GetClientRect(window,&client);
            POINT point{client.right/2,client.bottom/2}; ClientToScreen(window,&point);
            sampler=std::thread([&,point] {
                using Microsoft::WRL::ComPtr;
                auto checked=[](HRESULT hr) { if(FAILED(hr)) throw std::runtime_error("Desktop duplication HRESULT="+std::to_string(static_cast<unsigned>(hr))); };
                try {
                    ComPtr<IDXGIFactory1> factory; checked(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
                    ComPtr<IDXGIAdapter1> adapter; ComPtr<IDXGIOutput1> output; DXGI_OUTPUT_DESC outputDesc{};
                    bool found=false;
                    for(unsigned a=0;!found && factory->EnumAdapters1(a,&adapter)==S_OK;++a) {
                        ComPtr<IDXGIOutput> candidate;
                        for(unsigned o=0;adapter->EnumOutputs(o,&candidate)==S_OK;++o) {
                            checked(candidate->GetDesc(&outputDesc));
                            if(outputDesc.AttachedToDesktop && PtInRect(&outputDesc.DesktopCoordinates,point)) { checked(candidate.As(&output)); found=true; break; }
                        }
                    }
                    if(!found) throw std::runtime_error("Screen probe point has no desktop output");
                    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
                    checked(D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));
                    ComPtr<IDXGIOutputDuplication> duplication; checked(output->DuplicateOutput(device.Get(),&duplication));
                    ComPtr<ID3D11Texture2D> staging;
                    std::cout<<"Desktop probe point="<<point.x<<","<<point.y<<std::endl;
                    while(sampling) {
                        DXGI_OUTDUPL_FRAME_INFO frameInfo{}; ComPtr<IDXGIResource> resource;
                        HRESULT result=duplication->AcquireNextFrame(100,&frameInfo,&resource);
                        if(result==DXGI_ERROR_WAIT_TIMEOUT) continue;
                        checked(result);
                        ComPtr<ID3D11Texture2D> image; checked(resource.As(&image));
                        if(!staging) {
                            D3D11_TEXTURE2D_DESC desc{}; image->GetDesc(&desc); desc.Width=desc.Height=1; desc.MipLevels=desc.ArraySize=1; desc.SampleDesc={1,0}; desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=desc.MiscFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
                            if(desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM) throw std::runtime_error("Unsupported desktop pixel format");
                            checked(device->CreateTexture2D(&desc,nullptr,&staging));
                        }
                        unsigned x=point.x-outputDesc.DesktopCoordinates.left,y=point.y-outputDesc.DesktopCoordinates.top;
                        D3D11_BOX box{x,y,0,x+1,y+1,1}; context->CopySubresourceRegion(staging.Get(),0,0,0,0,image.Get(),0,&box);
                        D3D11_MAPPED_SUBRESOURCE data{}; checked(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&data));
                        auto* pixel=static_cast<const unsigned char*>(data.pData);
                        screenPixels.push_back({std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-testStart).count(),pixel[2],pixel[1],pixel[0]});
                        context->Unmap(staging.Get(),0); checked(duplication->ReleaseFrame());
                    }
                } catch(const std::exception& e) { std::cerr<<"DISPLAY PROBE UNAVAILABLE: "<<e.what()<<std::endl; }
            });
        }
        for(unsigned frame=0;frame<frameCount;++frame) {
            if(inputLimit>0) std::this_thread::sleep_until(testStart+std::chrono::duration_cast<std::chrono::steady_clock::duration>(std::chrono::duration<double>(frame/inputLimit)));
            MSG msg; while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
            if(secondary && frame==3) {
                VkDevice probeDevice; check(vkCreateDevice(physical,&devInfo,nullptr,&probeDevice));
                vkDestroyDevice(probeDevice,nullptr);
            }
            if(capture && frame==3) {
                using Request=void(*)();
                reinterpret_cast<Request>(GetProcAddress(GetModuleHandleW(L"DVLSFG.Native.dll"),"DVLSFG_RequestCapture"))();
            }
            if(toggle && (frame==3 || frame==5)) {
                using Enable=void(*)(int);
                auto native=GetModuleHandleW(L"DVLSFG.Native.dll");
                reinterpret_cast<Enable>(GetProcAddress(native,"DVLSFG_SetEnabled"))(frame==5 ? 1 : 0);
            }
            if(recreate && frame==6) {
                SetWindowPos(window,nullptr,0,0,800,600,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
                MSG resizeMessage; while(PeekMessageW(&resizeMessage,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&resizeMessage); DispatchMessageW(&resizeMessage); }
                check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical,surface,&caps));
                size=caps.currentExtent; swapInfo.imageExtent=size;
                auto old=swapchain; swapInfo.oldSwapchain=old;
                check(vkCreateSwapchainKHR(device,&swapInfo,nullptr,&swapchain));
                vkDestroySwapchainKHR(device,old,nullptr);
                check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,nullptr)); images.resize(imagesCount);
                check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,images.data()));
            }
            std::cout<<"Host frame "<<frame<<": acquire"<<std::endl;
            uint32_t index; check(vkAcquireNextImageKHR(device,swapchain,UINT64_MAX,acquired,VK_NULL_HANDLE,&index));
            check(vkResetCommandBuffer(buffer,0)); VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO}; check(vkBeginCommandBuffer(buffer,&begin));
            VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER}; barrier.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED; barrier.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL; barrier.dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT; barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED; barrier.image=images[index]; barrier.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
            vkCmdPipelineBarrier(buffer,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&barrier);
            VkClearColorValue color{}; color.float32[0]=frameCount==12 ? float(frame)/12 : .5f+.45f*std::sin(float(frame)*.1f); color.float32[1]=.2f; color.float32[2]=.5f; color.float32[3]=1;
            if(displayProof) color.float32[0]=float((frame%32)*8)/255.f;
            vkCmdClearColorImage(buffer,images[index],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&color,1,&barrier.subresourceRange);
            barrier.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL; barrier.newLayout=VK_IMAGE_LAYOUT_PRESENT_SRC_KHR; barrier.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT; barrier.dstAccessMask=VK_ACCESS_MEMORY_READ_BIT;
            vkCmdPipelineBarrier(buffer,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,0,0,nullptr,0,nullptr,1,&barrier); check(vkEndCommandBuffer(buffer));
            VkPipelineStageFlags stage=VK_PIPELINE_STAGE_ALL_COMMANDS_BIT; VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO}; submit.waitSemaphoreCount=1; submit.pWaitSemaphores=&acquired; submit.pWaitDstStageMask=&stage; submit.commandBufferCount=1; submit.pCommandBuffers=&buffer; submit.signalSemaphoreCount=1; submit.pSignalSemaphores=&ready;
            check(vkQueueSubmit(queue,1,&submit,VK_NULL_HANDLE));
            VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR}; present.waitSemaphoreCount=1; present.pWaitSemaphores=&ready; present.swapchainCount=1; present.pSwapchains=&swapchain; present.pImageIndices=&index;
            uint64_t id=frame+1; VkPresentIdKHR presentId{VK_STRUCTURE_TYPE_PRESENT_ID_KHR}; presentId.swapchainCount=1; presentId.pPresentIds=&id;
            VkPresentModeKHR requestedMode=VK_PRESENT_MODE_FIFO_KHR;
            VkSwapchainPresentModeInfoEXT presentMode{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODE_INFO_EXT}; presentMode.swapchainCount=1; presentMode.pPresentModes=&requestedMode; presentMode.pNext=&presentId;
            VkFence presentFence=VK_NULL_HANDLE; VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
            VkSwapchainPresentFenceInfoEXT presentFences{VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT}; presentFences.swapchainCount=1; presentFences.pFences=&presentFence;
            if(metadata) { check(vkCreateFence(device,&fenceInfo,nullptr,&presentFence)); presentId.pNext=&presentFences; present.pNext=&presentMode; }
            if(resizePresent && (frame==3 || frame==7)) {
                SetWindowPos(window,nullptr,0,0,frame==3?960:640,frame==3?720:480,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
                MSG resizeMessage; while(PeekMessageW(&resizeMessage,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&resizeMessage); DispatchMessage(&resizeMessage); }
            }
            auto presentResult=vkQueuePresentKHR(queue,&present);
            if(presentResult!=VK_ERROR_OUT_OF_DATE_KHR) check(presentResult);
            std::cout<<"Host frame "<<frame<<": presented, result="<<presentResult<<std::endl; check(vkQueueWaitIdle(queue));
            if(metadata) {
                check(vkWaitForFences(device,1,&presentFence,VK_TRUE,5000000000));
                vkDestroyFence(device,presentFence,nullptr);
                if(requestedMode!=VK_PRESENT_MODE_FIFO_KHR || presentMode.pNext!=&presentId || presentId.pNext!=&presentFences) throw std::runtime_error("Caller metadata was mutated");
            }
            if(resizePresent && (frame==3 || frame==7 || presentResult==VK_ERROR_OUT_OF_DATE_KHR || presentResult==VK_SUBOPTIMAL_KHR)) {
                check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical,surface,&caps)); size=caps.currentExtent; swapInfo.imageExtent=size;
                auto old=swapchain; swapInfo.oldSwapchain=old;
                check(vkCreateSwapchainKHR(device,&swapInfo,nullptr,&swapchain)); vkDestroySwapchainKHR(device,old,nullptr);
                check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,nullptr)); images.resize(imagesCount); check(vkGetSwapchainImagesKHR(device,swapchain,&imagesCount,images.data()));
            }
        }
        auto elapsed=std::chrono::duration<double>(std::chrono::steady_clock::now()-testStart).count();
        if(displayProof) {
            sampling=false; sampler.join();
            std::ofstream pixels("screen-pixels.csv"); pixels<<"Milliseconds,Red,Green,Blue\n";
            for(auto p:screenPixels) pixels<<p.milliseconds<<','<<p.red<<','<<p.green<<','<<p.blue<<'\n';
        }
        std::cout<<"Completed "<<frameCount<<" real frames; validation errors="<<errors<<"; input FPS="<<frameCount/elapsed<<std::endl;
        using GetStatus=int(*)(char*,unsigned,unsigned long long*,unsigned long long*);
        auto native=GetModuleHandleW(L"DVLSFG.Native.dll");
        if(native) {
            auto get=reinterpret_cast<GetStatus>(GetProcAddress(native,"DVLSFG_GetStatus"));
            char status[4096]{}; unsigned long long real=0,generated=0;
            get(status,sizeof(status),&real,&generated);
            std::cout<<"Native counters: real="<<real<<" generated="<<generated<<std::endl;
        }
        vkDestroySemaphore(device,ready,nullptr); vkDestroySemaphore(device,acquired,nullptr); vkDestroyCommandPool(device,pool,nullptr);
        std::cout<<"Destroy swapchain"<<std::endl;
        vkDestroySwapchainKHR(device,swapchain,nullptr);
        std::cout<<"Destroy device"<<std::endl;
        vkDestroyDevice(device,nullptr); vkDestroySurfaceKHR(instance,surface,nullptr);
        reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(vkGetInstanceProcAddr(instance,"vkDestroyDebugUtilsMessengerEXT"))(instance,messenger,nullptr);
        std::cout<<"Destroy instance"<<std::endl;
        vkDestroyInstance(instance,nullptr); DestroyWindow(window);
        std::cout<<"Cleanup complete"<<std::endl;
        return errors ? 1 : 0;
    } catch(const std::exception& e) { std::cerr<<e.what()<<'\n'; return 1; }
}

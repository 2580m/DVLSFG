#include "fsr_framegen.hpp"
#include <vulkan/vulkan.h>
#include <array>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <vector>
static void check(VkResult result) {if(result!=VK_SUCCESS) throw std::runtime_error("Vulkan error "+std::to_string(result));}
static void log(const std::string& text) {std::cout<<text<<std::endl;}
int main() {
    try {
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO}; app.apiVersion=VK_API_VERSION_1_3;
        VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO}; instanceInfo.pApplicationInfo=&app;
        VkInstance instance{}; check(vkCreateInstance(&instanceInfo,nullptr,&instance));
        uint32_t count{}; check(vkEnumeratePhysicalDevices(instance,&count,nullptr)); std::vector<VkPhysicalDevice> devices(count); check(vkEnumeratePhysicalDevices(instance,&count,devices.data()));
        auto physical=devices.at(0);
        uint32_t queueCount{}; vkGetPhysicalDeviceQueueFamilyProperties(physical,&queueCount,nullptr); std::vector<VkQueueFamilyProperties> queues(queueCount); vkGetPhysicalDeviceQueueFamilyProperties(physical,&queueCount,queues.data());
        uint32_t family{}; while(family<queueCount && !(queues[family].queueFlags&VK_QUEUE_COMPUTE_BIT)) ++family;
        float priority=1;
        VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO}; queueInfo.queueFamilyIndex=family; queueInfo.queueCount=1; queueInfo.pQueuePriorities=&priority;
        VkPhysicalDeviceVulkan13Features f13{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES};
        VkPhysicalDeviceVulkan12Features f12{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES}; f12.pNext=&f13;
        VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2}; features.pNext=&f12; vkGetPhysicalDeviceFeatures2(physical,&features);
        VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO}; deviceInfo.queueCreateInfoCount=1; deviceInfo.pQueueCreateInfos=&queueInfo; deviceInfo.pNext=&features;
        VkDevice device{}; check(vkCreateDevice(physical,&deviceInfo,nullptr,&device)); VkQueue queue{}; vkGetDeviceQueue(device,family,0,&queue);
        VkPhysicalDeviceMemoryProperties memory{}; vkGetPhysicalDeviceMemoryProperties(physical,&memory);
        auto memoryType=[&](uint32_t bits,VkMemoryPropertyFlags flags) {for(uint32_t i=0;i<memory.memoryTypeCount;++i) if((bits&(1u<<i)) && (memory.memoryTypes[i].propertyFlags&flags)==flags) return i; throw std::runtime_error("Memory type unavailable");};
        std::vector<VkDeviceMemory> allocations;
        std::array<VkImage,4> images{};
        const VkExtent2D size{256,128};
        for(size_t i=0;i<images.size();++i) {
            VkImageCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO}; info.imageType=VK_IMAGE_TYPE_2D; info.format=i<2 ? VK_FORMAT_R16G16B16A16_SFLOAT : i==2 ? VK_FORMAT_R32_SFLOAT : VK_FORMAT_R32G32_SFLOAT;
            info.extent={size.width,size.height,1}; info.mipLevels=1; info.arrayLayers=1; info.samples=VK_SAMPLE_COUNT_1_BIT; info.usage=VK_IMAGE_USAGE_STORAGE_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            check(vkCreateImage(device,&info,nullptr,&images[i])); VkMemoryRequirements req{}; vkGetImageMemoryRequirements(device,images[i],&req);
            VkMemoryAllocateInfo alloc{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO}; alloc.allocationSize=req.size; alloc.memoryTypeIndex=memoryType(req.memoryTypeBits,VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
            VkDeviceMemory value{}; check(vkAllocateMemory(device,&alloc,nullptr,&value)); allocations.push_back(value); check(vkBindImageMemory(device,images[i],value,0));
        }
        VkBufferCreateInfo bufferInfo{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO}; bufferInfo.size=size.width*size.height*8; bufferInfo.usage=VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        VkBuffer upload{},readback{}; VkDeviceMemory uploadMemory{},readbackMemory{};
        auto buffer=[&](VkBuffer& target,VkDeviceMemory& storage) {check(vkCreateBuffer(device,&bufferInfo,nullptr,&target)); VkMemoryRequirements req{}; vkGetBufferMemoryRequirements(device,target,&req); VkMemoryAllocateInfo alloc{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO}; alloc.allocationSize=req.size; alloc.memoryTypeIndex=memoryType(req.memoryTypeBits,VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT); check(vkAllocateMemory(device,&alloc,nullptr,&storage)); check(vkBindBufferMemory(device,target,storage,0));};
        buffer(upload,uploadMemory); buffer(readback,readbackMemory);
        void* uploadData{}; void* readData{}; check(vkMapMemory(device,uploadMemory,0,VK_WHOLE_SIZE,0,&uploadData)); check(vkMapMemory(device,readbackMemory,0,VK_WHOLE_SIZE,0,&readData));
        VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO}; poolInfo.queueFamilyIndex=family; poolInfo.flags=VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        VkCommandPool pool{}; check(vkCreateCommandPool(device,&poolInfo,nullptr,&pool));
        VkCommandBufferAllocateInfo cmdInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO}; cmdInfo.commandPool=pool; cmdInfo.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY; cmdInfo.commandBufferCount=1;
        VkCommandBuffer cmd{}; check(vkAllocateCommandBuffers(device,&cmdInfo,&cmd));
        DVLSFG::FsrFrameGeneration::configureInstance(instance,vkGetInstanceProcAddr);
        {
            DVLSFG::FsrFrameGeneration fg(device,physical,size,true,vkGetDeviceProcAddr,log);
            DVLSFG::SceneUiFrame frame{}; frame.renderExtent=size; frame.depth={images[2],VK_IMAGE_LAYOUT_GENERAL,VK_FORMAT_R32_SFLOAT}; frame.motion={images[3],VK_IMAGE_LAYOUT_GENERAL,VK_FORMAT_R32G32_SFLOAT};
            frame.deltaMs=16.6667f; frame.nearPlane=.1f; frame.farPlane=1000; frame.fov=1.0472f;
            auto barrier=[&](VkImage image,VkImageLayout oldLayout,VkImageLayout newLayout) {VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER}; b.oldLayout=oldLayout; b.newLayout=newLayout; b.srcAccessMask=VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT; b.dstAccessMask=b.srcAccessMask; b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED; b.image=image; b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1}; vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,0,0,nullptr,0,nullptr,1,&b);};
            for(unsigned index=0;index<5;++index) {
                unsigned barX=32+index*24;
                auto pixels=static_cast<uint16_t*>(uploadData);
                for(unsigned y=0;y<size.height;++y) for(unsigned x=0;x<size.width;++x) {auto offset=(y*size.width+x)*4; pixels[offset]=x>=barX && x<barX+32 ? 0x3c00 : 0; pixels[offset+1]=0; pixels[offset+2]=0x3000; pixels[offset+3]=0x3c00;}
                check(vkResetCommandBuffer(cmd,0)); VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO}; check(vkBeginCommandBuffer(cmd,&begin));
                for(auto image:images) barrier(image,index==0 ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL);
                VkBufferImageCopy copy{}; copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1}; copy.imageExtent={size.width,size.height,1};
                vkCmdCopyBufferToImage(cmd,upload,images[0],VK_IMAGE_LAYOUT_GENERAL,1,&copy);
                VkClearColorValue depth{}; depth.float32[0]=.5f; VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1}; vkCmdClearColorImage(cmd,images[2],VK_IMAGE_LAYOUT_GENERAL,&depth,1,&range);
                VkClearColorValue motion{}; motion.float32[0]=index==0 ? 0.f : 24.f/size.width; vkCmdClearColorImage(cmd,images[3],VK_IMAGE_LAYOUT_GENERAL,&motion,1,&range);
                for(auto image:images) barrier(image,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_GENERAL);
                fg.dispatch(cmd,images[0],images[1],frame,index==0);
                barrier(images[1],VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
                vkCmdCopyImageToBuffer(cmd,images[1],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,readback,1,&copy);
                barrier(images[1],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_GENERAL);
                check(vkEndCommandBuffer(cmd)); VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO}; submit.commandBufferCount=1; submit.pCommandBuffers=&cmd; check(vkQueueSubmit(queue,1,&submit,VK_NULL_HANDLE)); check(vkDeviceWaitIdle(device));
                if(index>=2) {
                    auto output=static_cast<uint16_t*>(readData); unsigned changed=0,redPixels=0; double sumX=0;
                    for(unsigned y=0;y<size.height;++y) for(unsigned x=0;x<size.width;++x) {auto offset=(y*size.width+x)*4; if(output[offset]!=pixels[offset]) changed++; if(output[offset]>0x3800 && output[offset]<0x7c00) {sumX+=x; redPixels++;}}
                    double center=redPixels ? sumX/redPixels : 0;
                    if(changed<100 || redPixels<100 || std::abs(center-(barX+3.5))>2) throw std::runtime_error("Interpolation proof failed: changed="+std::to_string(changed)+" centroid="+std::to_string(center));
                    std::cout<<"PASS interpolated frame "<<index<<": changed="<<changed<<", centroid="<<center<<", real="<<barX+15.5<<std::endl;
                }
            }
        }
        vkUnmapMemory(device,uploadMemory); vkUnmapMemory(device,readbackMemory); vkDestroyBuffer(device,upload,nullptr); vkDestroyBuffer(device,readback,nullptr); vkFreeMemory(device,uploadMemory,nullptr); vkFreeMemory(device,readbackMemory,nullptr);
        vkDestroyCommandPool(device,pool,nullptr); for(auto image:images) vkDestroyImage(device,image,nullptr); for(auto value:allocations) vkFreeMemory(device,value,nullptr);
        vkDestroyDevice(device,nullptr); vkDestroyInstance(instance,nullptr);
        std::cout<<"PASS official FSR frame generation without LSFG, debug-utils or promoted KHR extension names"<<std::endl; return 0;
    } catch(const std::exception& e) {std::cerr<<e.what()<<std::endl; return 1;}
}

#pragma once
#include "platform.hpp"
#include "layer.hpp"
#include <filesystem>
#include <fstream>
#include <vector>
#include <stdexcept>
namespace DVLSFG {
class Readback {
    VkDevice device{};
    VkBuffer buffer{};
    VkDeviceMemory memory{};
    VkDeviceSize bytes{};
    VkExtent2D extent{};
    bool coherent=false;
    template<class T> T function(const char* name) const {
        auto fn=reinterpret_cast<T>(Layer::ovkGetDeviceProcAddr(device,name));
        if(!fn) throw std::runtime_error(std::string("Readback API missing: ")+name);
        return fn;
    }
    static void check(VkResult value) { if(value!=VK_SUCCESS) throw std::runtime_error("Readback Vulkan result "+std::to_string(value)); }
public:
    Readback()=default;
    Readback(const Readback&)=delete;
    ~Readback() noexcept {
        try {
            if(buffer) function<PFN_vkDestroyBuffer>("vkDestroyBuffer")(device,buffer,nullptr);
            if(memory) Layer::ovkFreeMemory(device,memory,nullptr);
        } catch(...) {}
    }
    void create(VkDevice dev,VkPhysicalDevice physical,VkExtent2D size) {
        device=dev; extent=size; bytes=VkDeviceSize(size.width)*size.height*4;
        VkBufferCreateInfo info{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO}; info.size=bytes; info.usage=VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        check(function<PFN_vkCreateBuffer>("vkCreateBuffer")(device,&info,nullptr,&buffer));
        VkMemoryRequirements requirements{};
        function<PFN_vkGetBufferMemoryRequirements>("vkGetBufferMemoryRequirements")(device,buffer,&requirements);
        VkPhysicalDeviceMemoryProperties props{}; Layer::ovkGetPhysicalDeviceMemoryProperties(physical,&props);
        unsigned selected=UINT32_MAX;
        for(unsigned i=0;i<props.memoryTypeCount;++i) if((requirements.memoryTypeBits&(1u<<i)) && (props.memoryTypes[i].propertyFlags&VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT)) {
            selected=i;
            if(props.memoryTypes[i].propertyFlags&VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) break;
        }
        if(selected==UINT32_MAX) throw std::runtime_error("No host-visible readback memory");
        coherent=(props.memoryTypes[selected].propertyFlags&VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)!=0;
        VkMemoryAllocateInfo alloc{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO}; alloc.allocationSize=requirements.size; alloc.memoryTypeIndex=selected;
        check(Layer::ovkAllocateMemory(device,&alloc,nullptr,&memory));
        check(function<PFN_vkBindBufferMemory>("vkBindBufferMemory")(device,buffer,memory,0));
    }
    void record(VkCommandBuffer command,VkImage image) {
        VkBufferImageCopy region{}; region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1}; region.imageExtent={extent.width,extent.height,1};
        function<PFN_vkCmdCopyImageToBuffer>("vkCmdCopyImageToBuffer")(command,image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,buffer,1,&region);
        VkBufferMemoryBarrier barrier{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER};
        barrier.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT; barrier.dstAccessMask=VK_ACCESS_HOST_READ_BIT;
        barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;
        barrier.buffer=buffer; barrier.size=VK_WHOLE_SIZE;
        Layer::ovkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_HOST_BIT,0,0,nullptr,1,&barrier,0,nullptr);
    }
    void save(const std::filesystem::path& path,VkFormat format) {
        void* mapped=nullptr;
        check(function<PFN_vkMapMemory>("vkMapMemory")(device,memory,0,VK_WHOLE_SIZE,0,&mapped));
        try {
            if(!coherent) {
                VkMappedMemoryRange range{VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE}; range.memory=memory; range.size=VK_WHOLE_SIZE;
                check(function<PFN_vkInvalidateMappedMemoryRanges>("vkInvalidateMappedMemoryRanges")(device,1,&range));
            }
            std::vector<unsigned char> pixels(static_cast<size_t>(bytes)); memcpy(pixels.data(),mapped,pixels.size());
            if(format==VK_FORMAT_R8G8B8A8_UNORM || format==VK_FORMAT_R8G8B8A8_SRGB)
                for(size_t i=0;i<pixels.size();i+=4) std::swap(pixels[i],pixels[i+2]);
            BITMAPFILEHEADER file{}; file.bfType=0x4d42; file.bfOffBits=sizeof(file)+sizeof(BITMAPINFOHEADER); file.bfSize=file.bfOffBits+static_cast<DWORD>(bytes);
            BITMAPINFOHEADER info{}; info.biSize=sizeof(info); info.biWidth=extent.width; info.biHeight=-static_cast<LONG>(extent.height); info.biPlanes=1; info.biBitCount=32; info.biCompression=BI_RGB; info.biSizeImage=static_cast<DWORD>(bytes);
            std::ofstream out(path,std::ios::binary); out.write(reinterpret_cast<const char*>(&file),sizeof(file)); out.write(reinterpret_cast<const char*>(&info),sizeof(info)); out.write(reinterpret_cast<const char*>(pixels.data()),pixels.size());
            if(!out) throw std::runtime_error("Cannot save framebuffer capture");
        } catch(...) { function<PFN_vkUnmapMemory>("vkUnmapMemory")(device,memory); throw; }
        function<PFN_vkUnmapMemory>("vkUnmapMemory")(device,memory);
    }
};
}

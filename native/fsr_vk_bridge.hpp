#pragma once
#ifndef VK_USE_PLATFORM_WIN32_KHR
#define VK_USE_PLATFORM_WIN32_KHR
#endif
#include <windows.h>
#include <vulkan/vulkan.h>
// Instance commands must use the layer's downstream dispatch. The physical
// device inside a layer is not a handle for re-entry through the loader facade.
extern "C" {
VKAPI_ATTR VkResult VKAPI_CALL dvFsrEnumerateDeviceExtensionProperties(VkPhysicalDevice,const char*,uint32_t*,VkExtensionProperties*);
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceMemoryProperties(VkPhysicalDevice,VkPhysicalDeviceMemoryProperties*);
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceProperties(VkPhysicalDevice,VkPhysicalDeviceProperties*);
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceProperties2(VkPhysicalDevice,VkPhysicalDeviceProperties2*);
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceFeatures(VkPhysicalDevice,VkPhysicalDeviceFeatures*);
VKAPI_ATTR void VKAPI_CALL dvFsrGetPhysicalDeviceFeatures2(VkPhysicalDevice,VkPhysicalDeviceFeatures2*);
VKAPI_ATTR VkResult VKAPI_CALL dvFsrCreateBuffer(VkDevice,const VkBufferCreateInfo*,const VkAllocationCallbacks*,VkBuffer*);
}
#define vkEnumerateDeviceExtensionProperties dvFsrEnumerateDeviceExtensionProperties
#define vkGetPhysicalDeviceMemoryProperties dvFsrGetPhysicalDeviceMemoryProperties
#define vkGetPhysicalDeviceProperties dvFsrGetPhysicalDeviceProperties
#define vkGetPhysicalDeviceProperties2 dvFsrGetPhysicalDeviceProperties2
#define vkGetPhysicalDeviceFeatures dvFsrGetPhysicalDeviceFeatures
#define vkGetPhysicalDeviceFeatures2 dvFsrGetPhysicalDeviceFeatures2
#define vkCreateBuffer dvFsrCreateBuffer

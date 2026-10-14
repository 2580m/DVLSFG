#pragma once
#include <windows.h>
#include <vulkan/vulkan.h>
#include <string>
#include <filesystem>
namespace DVLSFG {
void initialize();
void status(const std::string& message);
void countFrame(unsigned generated);
bool isEnabled();
int generationTechnology();
unsigned long long epoch();
void finalizeBackend();
bool takeCapture();
std::filesystem::path captureDirectory();
void recordFrameTime(double milliseconds);
int inputFrameLimit();
struct GenerationSettings { int multiplier; bool performance; float flowScale; };
bool takeGenerationRequest(GenerationSettings& value);
void generationApplied(const GenerationSettings& value, bool success);
void externalBarrier(VkCommandBuffer buffer, VkImage image, unsigned family, bool acquire);
// The private backend must not re-enter the game's explicit Vulkan layer.
class BackendScope {
    std::wstring layers;
public:
    BackendScope() {
        wchar_t value[32768]{};
        GetEnvironmentVariableW(L"VK_INSTANCE_LAYERS", value, 32768);
        layers = value;
        std::wstring filtered;
        size_t start=0;
        while(start<layers.size()) {
            auto end=layers.find(L';',start);
            if(end==std::wstring::npos) end=layers.size();
            auto name=layers.substr(start,end-start);
            if(name!=L"VK_LAYER_DVLSFG_frame_generation" && !name.empty()) {
                if(!filtered.empty()) filtered+=L';'; filtered+=name;
            }
            start=end+1;
        }
        SetEnvironmentVariableW(L"VK_INSTANCE_LAYERS", filtered.empty() ? nullptr : filtered.c_str());
    }
    ~BackendScope() { SetEnvironmentVariableW(L"VK_INSTANCE_LAYERS", layers.empty() ? nullptr : layers.c_str()); }
};
}

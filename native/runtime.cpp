#include "platform.hpp"
#include <volk.h>
#include "config/config.hpp"
#include "extract/extract.hpp"
#include "extract/trans.hpp"
#include "utils/utils.hpp"
#include <lsfg_3_1.hpp>
#include <lsfg_3_1p.hpp>
#include <atomic>
#include <fstream>
#include <iostream>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include <chrono>
#include <iomanip>

Config::Configuration Config::activeConf{};
void Config::updateConfig(const std::string&) {} // Live multiplier requests use the present-boundary API.
Config::Configuration Config::getConfig(const std::pair<std::string,std::string>&) { return activeConf; }
extern const std::unordered_map<std::string, uint32_t> nameIdxTable;
namespace {
std::once_flag initialized;
std::mutex lock;
std::ofstream logFile;
std::string state = "Layer not initialized";
std::atomic<bool> enabled{false};
std::atomic<unsigned long long> realFrames{0}, generatedFrames{0};
std::atomic<unsigned long long> generationEpoch{1};
std::atomic<bool> capturePending{false};
std::atomic<int> gameFrameLimit{0};
std::mutex settingsLock;
DVLSFG::GenerationSettings requestedSettings{2,false,1.f}, appliedSettings{2,false,1.f};
bool settingsPending=false;
int settingsState=0;
std::filesystem::path configPath;
std::mutex metricsLock;
std::mutex toggleLock;
float inputRate=0,submittedRate=0,meanFrameMs=0,maxFrameMs=0;
double frameSum=0,frameMax=0;
unsigned frameSamples=0;
unsigned long long previousReal=0,previousGenerated=0;
std::chrono::steady_clock::time_point previousTime{};
}
namespace DVLSFG {
bool takeGenerationRequest(GenerationSettings& value) {
    std::lock_guard<std::mutex> guard(settingsLock);
    if(!settingsPending) return false;
    value=requestedSettings; settingsPending=false; return true;
}
void generationApplied(const GenerationSettings& value, bool success) {
    { std::lock_guard<std::mutex> guard(settingsLock); appliedSettings=value; settingsState=success ? 0 : -1; }
    ++generationEpoch;
    std::lock_guard<std::mutex> guard(metricsLock);
    inputRate=submittedRate=meanFrameMs=maxFrameMs=0;
    frameSum=frameMax=0; frameSamples=0; previousTime={};
}
int inputFrameLimit() { return gameFrameLimit.load(); }
void status(const std::string& message) {
    std::lock_guard<std::mutex> guard(lock);
    state = message;
    std::cerr << "DVLSFG: " << message << '\n';
}
bool isEnabled() { return enabled.load(); }
unsigned long long epoch() { return generationEpoch.load(); }
std::filesystem::path captureDirectory() { return configPath.parent_path()/"captures"; }
bool takeCapture() {
    if(capturePending.exchange(false)) return true;
    static ULONGLONG lastPoll=0;
    auto now=GetTickCount64(); if(now-lastPoll<1000) return false; lastPoll=now;
    try {
        auto request=configPath.parent_path()/"capture.request";
        if(std::filesystem::exists(request)) { std::filesystem::remove(request); return true; }
    } catch(const std::exception& e) { status(std::string("Capture request failed: ")+e.what()); }
    return false;
}
void finalizeBackend() {
    status("Finalizing private Vulkan backend");
    try {
        LSFG_3_1::finalize(); LSFG_3_1P::finalize();
        status("Private Vulkan backend finalized");
    } catch(const std::exception& e) { status(std::string("Backend cleanup failed: ") + e.what()); }
}
void recordFrameTime(double milliseconds) {
    std::lock_guard<std::mutex> guard(metricsLock);
    frameSum+=milliseconds; frameMax=std::max(frameMax,milliseconds); ++frameSamples;
}
void countFrame(unsigned generated) {
    auto real=++realFrames; generatedFrames+=generated;
    if(real==1 || real%60==0) std::cerr << "DVLSFG: real=" << real << " generated=" << generatedFrames.load() << '\n';
    std::lock_guard<std::mutex> guard(metricsLock);
    auto now=std::chrono::steady_clock::now();
    if(previousTime.time_since_epoch().count()==0) { previousTime=now; previousReal=real; previousGenerated=generatedFrames.load(); return; }
    double seconds=std::chrono::duration<double>(now-previousTime).count();
    if(seconds<1) return;
    inputRate=float((real-previousReal)/seconds);
    submittedRate=float((real-previousReal+generatedFrames.load()-previousGenerated)/seconds);
    meanFrameMs=frameSamples ? float(frameSum/frameSamples) : 0; maxFrameMs=float(frameMax);
    previousTime=now; previousReal=real; previousGenerated=generatedFrames.load(); frameSum=frameMax=0; frameSamples=0;
    try {
        auto file=configPath.parent_path()/"metrics.ini"; auto temp=configPath.parent_path()/"metrics.ini.tmp";
        { std::ofstream stream(temp); stream<<std::fixed<<std::setprecision(3)<<"InputFPS="<<inputRate<<"\nSubmittedFPS="<<submittedRate<<"\nMeanPresentMs="<<meanFrameMs<<"\nMaxPresentMs="<<maxFrameMs<<"\nRealFrames="<<real<<"\nGeneratedFrames="<<generatedFrames.load()<<'\n'; }
        MoveFileExW(temp.c_str(),file.c_str(),MOVEFILE_REPLACE_EXISTING);
    } catch(...) {} // Metrics must never interrupt rendering.
}
void externalBarrier(VkCommandBuffer buffer, VkImage image, unsigned family, bool acquire) {
    VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    barrier.srcAccessMask = acquire ? 0 : VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
    barrier.dstAccessMask = acquire ? VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT : 0;
    barrier.oldLayout = barrier.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    barrier.srcQueueFamilyIndex = acquire ? VK_QUEUE_FAMILY_EXTERNAL : family;
    barrier.dstQueueFamilyIndex = acquire ? family : VK_QUEUE_FAMILY_EXTERNAL;
    barrier.image = image;
    barrier.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
    vkCmdPipelineBarrier(buffer, acquire ? VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        acquire ? VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT : VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
        0,0,nullptr,0,nullptr,1,&barrier);
}
void initialize() {
    std::call_once(initialized, [] {
        try {
            wchar_t file[32768]{};
            if (!GetEnvironmentVariableW(L"DVLSFG_CONFIG", file, 32768)) return;
            configPath = file;
            logFile.open(configPath.parent_path() / "native.log", std::ios::trunc);
            if (logFile) std::cerr.rdbuf(logFile.rdbuf());
            wchar_t exe[32768]{}; GetModuleFileNameW(nullptr, exe, 32768);
            if (_wcsicmp(std::filesystem::path(exe).filename().c_str(), L"DerailValley.exe") != 0
                && !GetEnvironmentVariableW(L"DVLSFG_TEST_HOST", nullptr, 0)) {
                status("Ignored: host is not DerailValley.exe"); return;
            }
            std::ifstream input(configPath);
            if (!input) throw std::runtime_error("Cannot read config.ini");
            std::unordered_map<std::string,std::string> values;
            std::string line;
            while (std::getline(input,line)) {
                if (!line.empty() && line.back() == '\r') line.pop_back();
                auto eq = line.find('=');
                if (eq != std::string::npos) values[line.substr(0,eq)] = line.substr(eq+1);
            }
            auto& conf = Config::activeConf;
            conf.dll = values["DllPath"];
            conf.multiplier = static_cast<size_t>(std::stoi(values["Multiplier"]));
            if (conf.multiplier < 2 || conf.multiplier > 4) throw std::runtime_error("Multiplier must be 2..4");
            conf.performance = values["Performance"] == "1";
            conf.flowScale = std::stof(values["FlowScale"]);
            if (!(conf.flowScale >= 0.25f && conf.flowScale <= 1.f)) throw std::runtime_error("FlowScale must be 0.25..1");
            { std::lock_guard<std::mutex> guard(settingsLock); appliedSettings={static_cast<int>(conf.multiplier),conf.performance,conf.flowScale}; }
            conf.e_present = VK_PRESENT_MODE_IMMEDIATE_KHR;
            conf.hdr = false;
            if (values["Enabled"] != "1") { status("Disabled in config.ini"); return; }
            Extract::extractShaders();
            conf.enable = true;
            enabled = true;
            status("LSFG 3.1 resources loaded; waiting for Vulkan swapchain");
        } catch (const std::exception& e) { status(std::string("Disabled: ") + e.what()); }
    });
}
}
extern "C" __declspec(dllexport) int DVLSFG_GetStatus(char* out, unsigned capacity,
    unsigned long long* real, unsigned long long* generated) {
    if (real) *real = realFrames.load();
    if (generated) *generated = generatedFrames.load();
    std::lock_guard<std::mutex> guard(lock);
    if (out && capacity) { auto n = std::min(state.size(), size_t(capacity-1)); std::copy_n(state.data(),n,out); out[n] = 0; }
    return enabled.load() ? 1 : 0;
}
extern "C" __declspec(dllexport) void DVLSFG_SetEnabled(int value) {
    std::lock_guard<std::mutex> toggle(toggleLock);
    bool next=value!=0 && Config::activeConf.enable;
    bool previous=enabled.load();
    if(previous==next) return;
    if(!next) enabled=false;
    {
        std::lock_guard<std::mutex> metrics(metricsLock);
        inputRate=submittedRate=meanFrameMs=maxFrameMs=0; frameSum=frameMax=0; frameSamples=0; previousTime={};
    }
    if(next) { ++generationEpoch; enabled=true; }
}
extern "C" __declspec(dllexport) void DVLSFG_RequestCapture() { capturePending=true; }
// The UI only posts requests. Vulkan objects/configuration are changed at a present boundary.
extern "C" __declspec(dllexport) int DVLSFG_SetMultiplier(int value) {
    if(value<2 || value>4 || !Config::activeConf.enable) return 0;
    std::lock_guard<std::mutex> guard(settingsLock);
    if(!settingsPending) requestedSettings=appliedSettings;
    requestedSettings.multiplier=value; settingsPending=true; settingsState=1;
    return 1;
}
extern "C" __declspec(dllexport) int DVLSFG_GetMultiplier(int* value) {
    std::lock_guard<std::mutex> guard(settingsLock);
    if(value) *value=appliedSettings.multiplier;
    return settingsPending ? 1 : settingsState;
}
extern "C" __declspec(dllexport) int DVLSFG_SetGenerationSettings(int multiplier,int performance,float flowScale) {
    if(multiplier<2 || multiplier>4 || performance<0 || performance>1 ||
       !(flowScale>=.25f && flowScale<=1.f) || !Config::activeConf.enable) return 0;
    std::lock_guard<std::mutex> guard(settingsLock);
    requestedSettings={multiplier,performance!=0,flowScale}; settingsPending=true; settingsState=1; return 1;
}
extern "C" __declspec(dllexport) int DVLSFG_GetGenerationSettings(int* multiplier,int* performance,float* flowScale) {
    std::lock_guard<std::mutex> guard(settingsLock);
    if(multiplier) *multiplier=appliedSettings.multiplier;
    if(performance) *performance=appliedSettings.performance ? 1 : 0;
    if(flowScale) *flowScale=appliedSettings.flowScale;
    return settingsPending ? 1 : settingsState;
}
extern "C" __declspec(dllexport) void DVLSFG_SetInputFrameLimit(int value) { gameFrameLimit=value>0 ? value : 0; }
extern "C" __declspec(dllexport) void DVLSFG_GetMetrics(float* input,float* submitted,float* mean,float* peak) {
    std::lock_guard<std::mutex> guard(metricsLock);
    bool active=enabled.load();
    if(input) *input=active?inputRate:0; if(submitted) *submitted=active?submittedRate:0;
    if(mean) *mean=active?meanFrameMs:0; if(peak) *peak=active?maxFrameMs:0;
}
extern "C" __declspec(dllexport) int DVLSFG_Probe(const char* path, char* out, unsigned capacity) {
    try {
        Config::activeConf.dll = path;
        Extract::extractShaders();
        unsigned count = 0;
        for (const auto& [name, index] : nameIdxTable) {
            const auto spirv = Extract::translateShader(Extract::getShader(name));
            if (spirv.size() < 20 || spirv.size() % 4) throw std::runtime_error("Invalid SPIR-V size: " + name);
            uint32_t magic; memcpy(&magic,spirv.data(),4);
            if (magic != 0x07230203) throw std::runtime_error("Invalid SPIR-V magic: " + name);
            ++count;
        }
        DVLSFG::status("Probe passed: " + std::to_string(count) + " LSFG 3.1 shader mappings translated");
        DVLSFG_GetStatus(out,capacity,nullptr,nullptr); return 1;
    } catch(const std::exception& e) {
        DVLSFG::status(std::string("Probe failed: ") + e.what());
        DVLSFG_GetStatus(out,capacity,nullptr,nullptr); return 0;
    }
}

#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <regex>
#include <mutex>
#include <array>
#include "bootstrap_exports.hpp"
namespace {
std::once_flag once;
HMODULE self;
HMODULE loader;
std::vector<FARPROC> addresses;
std::wstring environment(const wchar_t* name) {
    DWORD size=GetEnvironmentVariableW(name,nullptr,0); if(!size) return {};
    std::wstring value(size,L'\0'); GetEnvironmentVariableW(name,value.data(),size); value.resize(size-1); return value;
}
std::string read(const std::filesystem::path& path) { std::ifstream file(path,std::ios::binary); return {std::istreambuf_iterator<char>(file),{}}; }
bool modEnabled(const std::string& xml,const char* id) {
    if(xml.empty()) return true;
    std::regex tags("<Mod\\s+([^>]*)>");
    std::regex identity(std::string("\\bId\\s*=\\s*['\"]")+id+"['\"]");
    std::regex disabled("\\bEnabled\\s*=\\s*['\"]false['\"]",std::regex::icase);
    for(auto it=std::sregex_iterator(xml.begin(),xml.end(),tags);it!=std::sregex_iterator();++it)
        if(std::regex_search((*it)[1].str(),identity)) return !std::regex_search((*it)[1].str(),disabled);
    return true;
}
bool sameFile(const std::filesystem::path& a,const std::filesystem::path& b) {
    if(!std::filesystem::exists(a) || !std::filesystem::exists(b) || std::filesystem::file_size(a)!=std::filesystem::file_size(b)) return false;
    std::ifstream x(a,std::ios::binary),y(b,std::ios::binary);
    std::array<char,65536> bx{},by{};
    while(x) { x.read(bx.data(),bx.size()); y.read(by.data(),by.size()); if(x.gcount()!=y.gcount() || !std::equal(bx.begin(),bx.begin()+x.gcount(),by.begin())) return false; }
    return !x.bad() && !y.bad();
}
void addEnvironment(const wchar_t* variable,const std::wstring& item,bool prepend=false) {
    auto current=environment(variable);
    size_t pos=0;
    while(pos<current.size()) { auto end=current.find(L';',pos); if(end==std::wstring::npos) end=current.size(); if(current.substr(pos,end-pos)==item) return; pos=end+1; }
    auto value=current.empty()?item:prepend?item+L";"+current:current+L";"+item;
    SetEnvironmentVariableW(variable,value.c_str());
}
void initialize() {
    wchar_t file[32768]{}; GetModuleFileNameW(self,file,32768);
    auto root=std::filesystem::path(file).parent_path(); auto mod=root/L"Mods"/L"DVLSFG";
    try {
        std::filesystem::path log=std::filesystem::exists(mod)?mod/L"bootstrap.log":root/L"DVLSFG-bootstrap.log";
        std::ofstream output(log,std::ios::trunc);
        wchar_t host[32768]{}; GetModuleFileNameW(nullptr,host,32768);
        bool game=_wcsicmp(std::filesystem::path(host).filename().c_str(),L"DerailValley.exe")==0;
        bool test=!environment(L"DVLSFG_BOOTSTRAP_TEST").empty();
        auto config=mod/L"config.ini";
        auto xml=read(root/L"DerailValley_Data"/L"Managed"/L"UnityModManager"/L"Params.xml");
        bool installed=std::filesystem::exists(mod/L"Info.json") && std::filesystem::exists(mod/L"VkLayer_DVLSFG.json") && std::filesystem::exists(mod/L"DVLSFG.Native.dll");
        bool requested=std::regex_search(read(config),std::regex("(^|\n)Enabled=1(\r?\n|$)"));
        bool active=(game||test) && installed && requested && environment(L"DVLSFG_NO_FRAMEGEN").empty() && modEnabled(xml,"DVLSFG") && modEnabled(xml,"DVVulkan") && !std::filesystem::exists(mod/L"bootstrap.disabled");
        if(active) for(const auto* name:{L"d3d11.dll",L"dxgi.dll"}) active=active && sameFile(root/name,root/L"Mods"/L"DVVulkan"/L"dxvk"/name);
        if(active) {
            SetEnvironmentVariableW(L"DVLSFG_CONFIG",config.c_str());
            addEnvironment(L"VK_ADD_LAYER_PATH",mod.wstring());
            addEnvironment(L"VK_INSTANCE_LAYERS",L"VK_LAYER_DVLSFG_frame_generation",true);
            output<<"Automatic LSFG startup enabled before Vulkan initialization\n";
        } else output<<"Automatic LSFG startup inactive; forwarding to system Vulkan\n";
    } catch(...) { /* Configuration failure must leave ordinary Vulkan usable. */ }
    wchar_t system[32768]{}; if(!GetSystemDirectoryW(system,32768)) RaiseFailFastException(nullptr,nullptr,0);
    auto path=std::filesystem::path(system)/L"vulkan-1.dll";
    loader=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!loader || loader==self) RaiseFailFastException(nullptr,nullptr,0);
    for(const auto* name:bootstrapExports) addresses.push_back(GetProcAddress(loader,name));
}
}
extern "C" FARPROC DVLSFG_Resolve(unsigned index) {
    std::call_once(once,initialize);
    if(index>=addresses.size() || !addresses[index]) RaiseFailFastException(nullptr,nullptr,0);
    return addresses[index];
}
extern "C" __declspec(dllexport) unsigned DVLSFG_BootstrapVersion() { return 0x00030000; }
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH) { self=module; DisableThreadLibraryCalls(module); }
    return TRUE;
}

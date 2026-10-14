cmake_minimum_required(VERSION 3.24)
project(DVLSFG LANGUAGES C CXX)
enable_language(ASM_MASM)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
find_package(Vulkan REQUIRED)
add_compile_definitions(VK_USE_PLATFORM_WIN32_KHR WIN32_LEAN_AND_MEAN NOMINMAX _CRT_SECURE_NO_WARNINGS)
add_compile_options(/utf-8 /wd4068)
set(UP "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/lsfg-vk")
set(PORT "${CMAKE_CURRENT_SOURCE_DIR}/../build/port")
add_subdirectory("${UP}/thirdparty/volk" volk)
target_include_directories(volk PUBLIC ${Vulkan_INCLUDE_DIRS})
file(GLOB DXBC "${PORT}/dxbc/src/dxbc/*.cpp" "${PORT}/dxbc/src/spirv/*.cpp" "${PORT}/dxbc/src/util/*.cpp")
add_library(dxbc STATIC ${DXBC})
target_include_directories(dxbc PUBLIC "${PORT}/dxbc/include/dxbc" "${PORT}/dxbc/include/spirv" "${PORT}/dxbc/include/util" "${PORT}/dxbc/include/dxvk")
target_include_directories(dxbc PRIVATE ${Vulkan_INCLUDE_DIRS})
target_compile_options(dxbc PRIVATE /FIarray)
file(GLOB_RECURSE BACKEND "${PORT}/framegen/src/*.cpp" "${PORT}/framegen/v3.1_src/*.cpp" "${PORT}/framegen/v3.1p_src/*.cpp")
file(GLOB MINI "${PORT}/src/mini/*.cpp")
add_library(DVLSFG.Native SHARED ${BACKEND} ${MINI}
  "${PORT}/src/layer.cpp" "${PORT}/src/hooks.cpp" context_win.cpp
  "${PORT}/src/utils/utils.cpp" "${PORT}/src/extract/extract.cpp" "${PORT}/src/extract/trans.cpp"
  runtime.cpp exports.def)
target_include_directories(DVLSFG.Native PRIVATE . "${PORT}/include" "${PORT}/framegen/include" "${PORT}/framegen/public" "${PORT}/framegen/v3.1_include" "${PORT}/framegen/v3.1p_include" ${Vulkan_INCLUDE_DIRS})
target_link_libraries(DVLSFG.Native PRIVATE dxbc volk)
target_compile_definitions(DVLSFG.Native PRIVATE VK_NO_PROTOTYPES)
target_compile_options(DVLSFG.Native PRIVATE /FI"${CMAKE_CURRENT_SOURCE_DIR}/platform.hpp")
add_executable(DVLSFG.VulkanHost ../tests/VulkanHost.cpp)
target_link_libraries(DVLSFG.VulkanHost PRIVATE Vulkan::Vulkan d3d11 dxgi delayimp)
target_link_options(DVLSFG.VulkanHost PRIVATE /DELAYLOAD:d3d11.dll /DELAYLOAD:dxgi.dll)
add_executable(DVLSFG.PresentChainTests ../tests/PresentChain.cpp)
target_include_directories(DVLSFG.PresentChainTests PRIVATE . ${Vulkan_INCLUDE_DIRS})
find_program(POWERSHELL_EXECUTABLE NAMES pwsh powershell REQUIRED)
set(BOOTSTRAP "${CMAKE_CURRENT_BINARY_DIR}/bootstrap")
execute_process(COMMAND "${POWERSHELL_EXECUTABLE}" -NoProfile -File "${CMAKE_CURRENT_SOURCE_DIR}/generate-bootstrap.ps1" -Output "${BOOTSTRAP}" COMMAND_ERROR_IS_FATAL ANY)
add_custom_command(OUTPUT "${BOOTSTRAP}/bootstrap.obj"
    COMMAND "${CMAKE_ASM_MASM_COMPILER}" /nologo /c "/Fo${BOOTSTRAP}/bootstrap.obj" "${BOOTSTRAP}/bootstrap.asm"
    DEPENDS "${BOOTSTRAP}/bootstrap.asm" VERBATIM)
set_source_files_properties("${BOOTSTRAP}/bootstrap.obj" PROPERTIES EXTERNAL_OBJECT TRUE GENERATED TRUE)
add_library(DVLSFG.Bootstrap SHARED bootstrap.cpp "${BOOTSTRAP}/bootstrap.obj" "${BOOTSTRAP}/bootstrap.def")
target_include_directories(DVLSFG.Bootstrap PRIVATE "${BOOTSTRAP}")

# Compile AMD's MIT-licensed FSR1 shaders offline: no Unity editor, runtime compiler,
# Vulkan-device sharing, or additional D3D11 DLL shim is needed for the upscaler.
get_filename_component(WINDOWS_KITS "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows Kits\\Installed Roots;KitsRoot10]" ABSOLUTE)
find_program(FXC_EXECUTABLE fxc HINTS "${WINDOWS_KITS}/bin/${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}/x64" REQUIRED)
set(FSR_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/fsr1")
foreach(PASS easu rcas)
    if(PASS STREQUAL "easu")
        set(EASU_DEFINE 1)
    else()
        set(EASU_DEFINE 0)
    endif()
    add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/fsr_${PASS}.h"
        COMMAND "${FXC_EXECUTABLE}" /nologo /O3 /T cs_5_0 /E main /D "FSR_EASU=${EASU_DEFINE}"
          /I "${FSR_SOURCE}" /Vn "g_fsr_${PASS}" /Fh "${CMAKE_CURRENT_BINARY_DIR}/fsr_${PASS}.h"
          "${CMAKE_CURRENT_SOURCE_DIR}/fsr1.hlsl"
        DEPENDS fsr1.hlsl "${FSR_SOURCE}/ffx_a.h" "${FSR_SOURCE}/ffx_fsr1.h" VERBATIM)
endforeach()
add_library(DVLSFG.FSR SHARED fsr_unity.cpp "${CMAKE_CURRENT_BINARY_DIR}/fsr_easu.h" "${CMAKE_CURRENT_BINARY_DIR}/fsr_rcas.h")
target_include_directories(DVLSFG.FSR PRIVATE "${CMAKE_CURRENT_BINARY_DIR}" "${FSR_SOURCE}")
add_executable(DVLSFG.FsrGpuTests ../tests/FsrGpu.cpp)
target_link_libraries(DVLSFG.FsrGpuTests PRIVATE DVLSFG.FSR)

# dv-scaling

Independent Derail Valley / Unity Mod Manager mod for experimental LSFG 3.1 frame
generation through a Windows Vulkan layer. Requires DVVulkan with deployed DXVK
and DV Language Helper (`DVLangHelper`).
Version 0.5.1 preserves independent UI MSAA on real and generated frames; FSR 3.1
disables only scene AA. Version 0.5.0 separates screen-space Canvas UI from LSFG inputs. Scene-only frames
are interpolated, then current UI is composited onto every generated frame.
Version 0.4.15 adds real AMD FSR 3.1 temporal upscaling alongside FSR 1.0 spatial upscaling. Selecting FSR 3.1 immediately disables native AA controls; switching away restores native AA. Native hover descriptions and live LSFG multiplier/model/flow changes remain supported.
FSR reduces scene-camera rendering resolution, preserves screen output resolution,
and runs before the existing LSFG presentation path. Both controls use native Apply/Discard.
UMM automatically installs a game-local Vulkan bootstrap. After the first restart,
normal Steam/game-shortcut launches load frame generation without a launch script.

User guide: [Chinese](package/README.zh-CN.md) / [English](package/README.en.md).
Source provenance and licenses: [UPSTREAM.md](UPSTREAM.md).
Verification and remaining limits: [VALIDATION.md](VALIDATION.md).

## Build

The source project directory is `dv-scaling`, matching the installation/package
directory. Run the following commands from that project root. After relocating
an existing checkout, regenerate the native CMake build directory because its
cache records absolute source paths.

Requires Visual Studio 2022 C++ tools, CMake, Vulkan SDK, .NET Framework compiler,
and the game's Unity / UMM / I2 managed references. Download pinned dependencies:

```powershell
.\fetch-upstream.ps1
.\build.ps1 -GameDirectory 'W:\SteamLibrary\steamapps\common\Derail Valley'
```

`port.ps1` generates the Win32 port under `build/port`; edit the transformation or
native sources, not generated files. The archive is `dist/dv-scaling-0.5.1.zip`.
The bootstrap generator reads the local system loader's export names/ordinals and
builds x64 argument-preserving forwarding stubs; no system loader binary is bundled.
No Lossless.dll, extracted LSFG shaders or user model paths are distributed.
FSR1's MIT-licensed shaders are compiled with Windows SDK FXC at build time.
FSR3 additionally needs a Unity 2019.4.40f1 editor to build its resource bundle:
run `Build-Fsr3.ps1 -UnityEditor <path-to-Unity.exe>` before packaging. The shader
variants target non-VR SDR D3D11/DXVK. Runtime C# is compiled with VS 2022 Roslyn.
The installed mod requires no editor or shader compiler.

## Translations

Interface strings live in `package/translations.csv`, copied to the installed mod
directory. Keep the two header rows from Language Helper's template and preserve
the `dv_scaling/` keys. Edit the relevant language column to add translations;
quoted multiline cells hold the native footer descriptions. English and Simplified
Chinese are provided. Other language columns currently use English, except
Traditional Chinese which retains the earlier Simplified Chinese text.

The mod registers the local CSV with `TranslationInjector("dv_scaling")` and reads
terms through the game's `LocalizationAPI.L`. Language Helper supports user
overrides under source ID `dv_scaling`. No online CSV or network request is used.
Build references use the installed DVLangHelper Runtime/Data assemblies; these
third-party binaries are not bundled with the mod.

FSR GPU tests: `build/native/Release/DVLSFG.FsrGpuTests.exe`, optionally followed by
the absolute path to the game's DXVK `d3d11.dll`. `tests/Install-GameFixture.ps1`
installs temporary Synthetic, World, or Ui UMM fixtures; remove/move those fixtures
after testing. World tests use the existing Continue save, not a new game.

## GPU verification

```powershell
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Performance -Multiplier 4
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Disabled
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Toggle
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Recreate
```

Tests use a hidden window and validate precise generated frame counts, Vulkan
diagnostics and normal destruction. Artifacts are isolated in `build/smoke-*`.
Implicit external layers are suppressed only within test processes; add
`-WithOverlays` to include them. No system layer settings are changed.

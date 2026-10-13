# DVLSFG

Independent Derail Valley / Unity Mod Manager mod for experimental LSFG 3.1 frame
generation through a Windows Vulkan layer. Requires DVVulkan with deployed DXVK.
UMM automatically installs a game-local Vulkan bootstrap. After the first restart,
normal Steam/game-shortcut launches load frame generation without a launch script.

User guide: [Chinese](package/README.zh-CN.md) / [English](package/README.en.md).
Source provenance and licenses: [UPSTREAM.md](UPSTREAM.md).
Verification and remaining limits: [VALIDATION.md](VALIDATION.md).

## Build

Requires Visual Studio 2022 C++ tools, CMake, Vulkan SDK, .NET Framework compiler,
and the game's Unity / UMM / I2 managed references. Download pinned dependencies:

```powershell
.\fetch-upstream.ps1
.\build.ps1 -GameDirectory 'W:\SteamLibrary\steamapps\common\Derail Valley'
```

`port.ps1` generates the Win32 port under `build/port`; edit the transformation or
native sources, not generated files. The archive is `dist/DVLSFG-0.3.2.zip`.
The bootstrap generator reads the local system loader's export names/ordinals and
builds x64 argument-preserving forwarding stubs; no system loader binary is bundled.
No vendor binaries, extracted shaders or user model paths are distributed.

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

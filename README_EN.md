<div align="center">

# DV Scaling

**Frame generation and upscaling in Derail Valley's native graphics settings.**

<a href="package/Info.json"><img src="https://img.shields.io/badge/version-0.6.3-6366f1?style=flat-square" alt="Version 0.6.3"></a>
<img src="https://img.shields.io/badge/status-Beta-f59e0b?style=flat-square" alt="Beta">
<img src="https://img.shields.io/badge/platform-Windows_x64-0078d4?style=flat-square" alt="Windows x64">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=flat-square" alt="MIT license"></a>

[简体中文](README.md) · **English**

[User Guide](package/README.en.md) · [Validation Records](VALIDATION.md) · [Upstream & Licenses](UPSTREAM.md)

</div>

---

## ✨ Features

DV Scaling is a standalone Unity Mod Manager mod. Its controls live in **Settings → Graphics**, using the game's native Apply, Discard, hover descriptions, and scrolling layout.

| Feature | Support |
| --- | --- |
| **LSFG 3.1 frame generation** | 2× / 3× / 4× multipliers, Standard and Performance models, adjustable optical flow quality |
| **AMD FSR FG 3.1 frame generation** | Fixed 2×; combines game depth and motion vectors with AMD optical flow; no Lossless.dll required |
| **FSR 1.0 upscaling** | Spatial reconstruction with separate quality presets and 0%–100% sharpening |
| **FSR 3.1 upscaling** | Temporal reconstruction using depth, motion vectors, projection jitter, and frame history |
| **Separate UI composition** | Screen overlay UI is excluded from interpolation; real and generated frames retain separate UI MSAA |
| **Live adjustments** | Once LSFG is loaded, its multiplier, model, and optical flow quality can change without restarting; upscaling changes take effect when applied |
| **Normal game launches** | After initial deployment and a restart, launch through Steam or your usual desktop shortcut |

Upscaling quality increases from left to right: **Ultra Performance → Performance → Balanced → Quality → Ultra Quality**. The game's screen resolution remains the output resolution; the selected preset lowers scene rendering resolution.

> [!NOTE]
> Currently supports **Windows x64, non-VR, SDR**. FSR 3.1 upscaling replaces scene antialiasing while preserving separate UI antialiasing. UI updates at the native frame rate. Ordinary presentation is retained while UMM is open or camera-space UI cannot be separated.

## 📦 Installation

### Requirements

| Component | Purpose |
| --- | --- |
| **Unity Mod Manager** | Installs and manages the mod; version 0.27.3 or later |
| **DVVulkan** | Deploys DXVK and provides the D3D11 → Vulkan rendering path |
| **DV Language Helper** (`DVLangHelper`) | Loads local translations and follows the game's language selection |
| **Lossless Scaling's `Lossless.dll`** | Supplies LSFG model resources; required only for LSFG frame generation. FSR upscaling and frame generation do not require it |

### Getting started

1. Exit the game. Install and enable the dependencies, and make sure DVVulkan has finished deploying DXVK.
2. Drag `dv-scaling-0.6.3.zip` into UMM. Use a release package or build one using the development instructions below.
3. Launch the game and enable **DV Scaling (Beta)** in UMM. For LSFG, enter the full path to your own `Lossless.dll`, save, and run the DLL compatibility check.
4. Open **Settings → Graphics**, choose a frame generation or upscaling technology, adjust quality and sharpening, and select **Apply**.
5. After first enabling frame generation, exit the game normally, wait for automatic deployment to finish, then launch through Steam or your existing shortcut.

> [!IMPORTANT]
> The package does not include `Lossless.dll`, extracted LSFG shaders, or personal configuration files. First enabling frame generation, switching frame generation technologies, and changing the DLL path require a restart. After initial deployment, no special launch script is needed.

<details>
<summary>Upgrading from the old directory</summary>

The installation directory is `Mods/dv-scaling`. When upgrading, you can move the entire old `Mods/DVLSFG` directory to the new location to preserve your configuration. Avoid keeping both installations.

The internal UMM ID remains `DVLSFG` to preserve enablement and dependency relationships.

</details>

## 🎮 Settings & Usage

| Setting | Options | When changes take effect |
| --- | --- | --- |
| Frame generation | Off / LSFG 3.1 / AMD FSR FG 3.1 | Initial loading and technology changes require a restart; the loaded technology can be toggled immediately |
| Frame generation multiplier | LSFG: 2× / 3× / 4×; FSR: fixed 2× | LSFG changes take effect live after Apply |
| Frame generation model | LSFG: Standard / Performance | LSFG changes take effect live after Apply |
| Optical flow quality | LSFG: 25% / 50% / 75% / 100% | LSFG changes take effect live after Apply |
| Upscaling technology | Off / FidelityFX™ Super Resolution / AMD FSR 3.1 | After Apply |
| Upscaling quality | Five presets | After Apply |
| Sharpening strength | 0%–100% | After Apply |

Turning a technology off immediately disables its dependent controls and preserves their selected values. Selecting FSR 3.1 upscaling disables the native scene antialiasing control; switching to FSR 1.0 or disabling upscaling restores it.

Start with the **Quality preset, 20% sharpening, and 2× frame generation**, then adjust for image quality and GPU load. Upscaling and frame generation can be enabled independently. Actual results depend on the scene, GPU, and display setup.

See the [User Guide](package/README.en.md) for detailed status and startup troubleshooting. The [Validation Records](VALIDATION.md) describe tested behavior and remaining coverage limits.

## 🛠️ Development

<details>
<summary>Build requirements and a full build</summary>

Run commands from the `dv-scaling` project root. Building requires:

- Visual Studio 2022 C++ tools, the Roslyn compiler, and Windows SDK with FXC.
- CMake and Vulkan SDK with `glslangValidator`.
- The game's Unity, UMM, I2, and post-processing assemblies, plus the installed DVLangHelper Runtime/Data assemblies.
- Unity **2019.4.40f1** to build the FSR 3.1 asset bundle. Full builds use `build/UnityEditor/Editor/Unity.exe` by default.

```powershell
.\fetch-upstream.ps1
.\build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Derail Valley'
```

Replace the game path with your installation location. Pass `-VulkanSdk` if your Vulkan SDK is installed elsewhere.

Build output goes to `dist/dv-scaling`; the current package is `dist/dv-scaling-0.6.3.zip`. The installed mod does not require the Unity editor or shader compilers.

`port.ps1` generates the Windows port from pinned upstream revisions into `build/port`. Edit the port rules or native sources rather than generated files. After moving the project directory, regenerate the native CMake build directory to avoid stale absolute paths.

</details>

<details>
<summary>Incremental builds and the FSR asset bundle</summary>

When native DLLs and the FSR asset bundle are already available, rebuild only managed code and the package:

```powershell
.\build.ps1 -SkipNative -GameDirectory 'D:\SteamLibrary\steamapps\common\Derail Valley'
```

To rebuild the FSR 3.1 asset bundle separately, specify a Unity editor:

```powershell
.\Build-Fsr3.ps1 -UnityEditor 'C:\path\to\Unity.exe'
```

FSR 1.0 EASU/RCAS shaders are compiled with FXC. The FSR 3.1 bundle uses fixed SDR, reversed-depth, and low-resolution motion-vector variants.

The game's local bootstrap reads export information from the system Vulkan loader to generate forwarding code. It does not bundle the system loader binary or modify the system Vulkan registry.

</details>

<details>
<summary>GPU validation and in-game tests</summary>

```powershell
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Performance -Multiplier 4
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Disabled
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Toggle
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Recreate
```

Vulkan tests use hidden windows to check generated-frame counts, validation messages, and resource destruction. Results are written to `build/smoke-*`. External implicit graphics layers are suppressed only within the test process. Pass `-WithOverlays` to include them; system graphics-layer settings are unchanged.

The native FSR GPU test is `build/native/Release/DVLSFG.FsrGpuTests.exe`. It can optionally receive the full path to the game's DXVK `d3d11.dll`.

`tests/Install-GameFixture.ps1` provides temporary UMM fixtures, including `Synthetic`, `World`, `Ui`, `Pacing`, `Fsr3`, `Fsr3World`, and `SceneUi`. Save-based tests use the existing Continue Game save. Remove temporary fixtures and restore configuration after testing.

</details>

## 🌐 Localization

UI text lives in [`package/translations.csv`](package/translations.csv). Installation copies it into the mod directory, where DV Language Helper loads it locally. Translations are not fetched over the network.

<details>
<summary>Editing or adding translations</summary>

- Preserve the Language Helper template's first two header rows and the `dv_scaling/` keys.
- Edit the appropriate language column. Footer descriptions use quoted CSV cells that can contain multiple lines.
- English and Simplified Chinese are currently provided. Other languages fall back to English; Traditional Chinese currently uses the Simplified Chinese text.
- The translation source ID is `dv_scaling` and supports Language Helper user overrides.

The code registers the local CSV through `TranslationInjector("dv_scaling")` and reads text through the game's `LocalizationAPI.L`. Builds reference the installed DVLangHelper assemblies; the package does not include those third-party binaries.

</details>

## 📚 Documentation & Licenses

| Document | Contents |
| --- | --- |
| [User Guide](package/README.en.md) | Installation, settings, startup, and compatibility troubleshooting |
| [中文使用指南](package/README.zh-CN.md) | Chinese installation and usage instructions |
| [Validation Records](VALIDATION.md) | Test environments, verified behavior, and practical limitations |
| [Upstream & Licenses](UPSTREAM.md) | Versions, sources, and licenses for lsfg-vk, FSR, and other dependencies |
| [MIT License](LICENSE) | License for this project's code; third-party components and model resources retain their respective licenses |

---

<div align="center">

**by BG4JEC** · lsfg-vk by PancakeTAS and contributors

</div>

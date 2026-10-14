# Nexus Mods publication draft

## Page fields

- Game: Derail Valley
- Name: DV Scaling - Frame Generation and Upscaling
- Version: 0.5.1
- Author: BG4JEC
- Main file title: DV Scaling 0.5.1 (Beta)
- Upload file: dist/dv-scaling-0.5.1.zip
- Suggested category: performance/utilities, subject to available Nexus categories.

## Summary

Adds LSFG 3.1 frame generation and AMD FSR 1.0/3.1 upscaling to Derail Valley's native graphics settings, with separate antialiased UI. Requires DVVulkan and DV Language Helper. Experimental, non-VR SDR.

## Description (BBCode)

```bbcode
[size=5][b]DV Scaling[/b][/size]
Frame generation and upscaling integrated into Derail Valley's native graphics settings.

[b]Features[/b]
[list]
[*]LSFG 3.1 frame generation with 2x, 3x and 4x multipliers, Standard/Performance models and optical-flow quality controls.
[*]AMD FSR 1.0 spatial upscaling and FSR 3.1 temporal upscaling.
[*]Five quality levels and a 0-100% sharpening slider.
[*]Separate screen-space UI composition: UI does not enter LSFG's optical-flow input, and both real and generated frames retain independent UI antialiasing.
[*]Native Apply/Discard controls, hover descriptions and English/Simplified Chinese localization.
[*]Normal Steam/desktop-shortcut launches after the first frame-generation deployment and restart.
[/list]

[b]Requirements[/b]
[list]
[*]Windows x64 and Unity Mod Manager 0.27.3 or newer.
[*]DVVulkan with DXVK deployed.
[*]DV Language Helper (DVLangHelper).
[*]Your own Lossless Scaling installation and Lossless.dll for LSFG frame generation. FSR does not need Lossless.dll.
[/list]
DV Language Helper source: [url=https://github.com/derail-valley-modding/language-helper]GitHub[/url]

[b]Installation[/b]
1. Exit the game. Install and enable DVVulkan and DV Language Helper; complete DXVK deployment.
2. Install the DV Scaling ZIP through Unity Mod Manager. Its folder is Mods/dv-scaling; the internal UMM ID remains DVLSFG.
3. For frame generation, enter your own Lossless.dll path in UMM, save and run the DLL compatibility check.
4. Open Settings > Graphics Quality, choose your frame-generation/upscaling technology and Apply.
5. For the first frame-generation activation, exit normally, wait for deployment, then launch from Steam or your usual shortcut.

[b]Settings[/b]
LSFG multiplier, model and optical-flow quality changes apply live once the generation layer is loaded. DLL path changes require restart. Upscaling and sharpening apply through the game's Apply button.
FSR 3.1 replaces scene antialiasing; screen UI keeps its own MSAA. UI still updates at the game's original frame rate. UMM's IMGUI window and unsupported camera-space canvases keep ordinary presentation.

[b]Compatibility and current limits[/b]
Experimental, non-VR SDR only. Tested on an RTX 3070 Ti with UMM 0.32.4 and DVVulkan 1.0.1 / DXVK 3.1.1. The tested Lossless.dll file version 3.2.2.0 contains the required LSFG 3.1 resources; run the resource check rather than relying only on a version number.
FSR 3.1 currently runs after scene postprocessing. Transparent effects, animated foliage, other GPUs and external overlay combinations need broader validation. Extra UI rendering and frame generation have GPU/memory cost; performance and end-to-end latency depend on the system and scene.
This release does not include DLSS.

[b]Recovery and removal[/b]
Disable frame generation and restart, or use the included Launch-Without-LSFG.cmd for recovery. To uninstall, disable the mod in UMM, exit normally so its owned bootstrap can be removed, then remove the mod. DVVulkan manages DXVK separately.

[b]Credits and source[/b]
Mod: BG4JEC.
LSFG backend: lsfg-vk v1.0.0 by PancakeTAS and contributors, using its MIT-licensed implementation.
FSR 1.0: AMD FidelityFX. FSR 3.1 Unity integration: Nico de Poel / FSR3Unity and AMD.
[url=https://github.com/2580m/DVLSFG]Source repository[/url]
The archive includes project and third-party license notices. It does not include Lossless.dll, extracted LSFG model shaders, user configuration, logs or save files.
```

## File description

DV Scaling 0.5.1 Beta. Install with Unity Mod Manager. Requires DVVulkan and DV Language Helper. This version preserves independent UI MSAA on both real and generated frames while FSR 3.1 disables scene AA only. Lossless.dll is not included.

## Permissions notes

Project code is MIT licensed; retain the copyright/license notice. Third-party components use their own packaged notices. Lossless Scaling model resources are not distributed by this mod.

## Release status

Prepared locally. Not uploaded or published: browser automation initialization failed before the Nexus page could be opened. Dependency page links, the logged-in account, available category, images and site-specific publication fields must be checked in the actual upload form.

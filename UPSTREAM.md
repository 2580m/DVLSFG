# DVLSFG upstream provenance

## Scene/UI separation (0.5.0)

`src/SceneUi.cs` captures the final scene before screen-space Canvas UI. The live
GraphicRegistry supplies UI roots without per-frame global object searches.
Screen-space canvases are rendered over black and white in an isolated camera;
all canvas/camera/transform/layer changes are restored in a finally block. The
two captures preserve color and per-channel transmission, including transparent
and additive UI. World-space canvases remain part of the scene.

`native/scene_ui.cpp` uses DXVK's public IDXGIVkInteropSurface/Device ABI to access
these textures on the existing game device. The declarations follow
https://github.com/doitsujin/dxvk/blob/v3.1.1/src/dxgi/dxgi_interfaces.h . D3D commands
are flushed from Unity's render-thread callback; no D3D calls occur in the present
hook, where DXVK already owns its submission lock. Capture resources are retained
through GPU completion. The sampled views explicitly use ARGBHalf float format,
even when DXVK's typeless base image uses UNORM. GLSL compute copies the scene
with the correct Y orientation, then composites UI after LSFG. The original game
swapchain frame remains intact. Missing/stale/size-mismatched scene input bypasses
frame generation instead of interpolating UI as a fallback.
The UNORM swapchain path converts the captured linear scene to display-encoded
RGB for LSFG, then decodes generated RGB for linear UI blending and encodes the
final result. Native SRGB swapchains retain linear values. UMM's IMGUI window
pauses scene capture; it cannot be reconstructed by the Canvas capture camera.

The Windows backend is generated from lsfg-vk **v1.0.0**, MIT license:

- Repository: https://git.lsfg-vk.dev/lsfg-vk-archive.git
- Commit: `7113d7d02da9fc9df5cb3b03230d1f7de86f7056`
- DXBC translator: `78ab59a8aaeb43cd1b0a5e91ba86722433a10b78`
- Volk: `be3dbd49bf77052665e96b6c7484af855e7e5f67`

`fetch-upstream.ps1` pins and checks the source revision. `port.ps1` generates
Windows sources under `build/port`, preserving the downloaded upstream source.
`native/context_win.cpp` supplies a separate Windows presentation implementation.
The upstream shaders are loaded as data from the user's Lossless.dll and translated
at runtime. The mod archive contains no Lossless.dll or extracted model shaders.

This project does not incorporate the newer CC BY-NC-ND upstream rewrite.
The packaged notices preserve lsfg-vk's MIT license, DXBC's license, and Volk's MIT license.

The current prototype uses one game instance and one rendering device. It presents
generated frames synchronously and waits for completion before recycling resources.
This prioritizes a verifiable prototype; throughput and latency require measurement.

## AMD FSR 1.0

- Official repository: https://github.com/GPUOpen-Effects/FidelityFX-FSR
- Pinned commit: `a21ffb8f6c13233ba336352bdff293894c706575`
- Unmodified `ffx_a.h`, `ffx_fsr1.h`, and `license.txt` are in `vendor/fsr1`.
- AMD MIT notice is distributed as `FSR1-LICENSE.txt`.

`native/fsr1.hlsl` supplies D3D11 full-precision EASU/RCAS callbacks; Windows SDK
FXC compiles them offline into `DVLSFG.FSR.dll`. `src/FsrUpscaling.cs` owns smaller
Unity scene targets and composites to the original screen resolution after scene
postprocessing and before Screen Space Overlay UI. The native plugin executes on
Unity's render thread on Unity's existing D3D11 device, including DXVK translation.
It does not replace DVVulkan or create a second FSR rendering device. FSR needs no
Lossless.dll, extracted LSFG assets, depth, motion vectors, or frame history.

## AMD FSR 3.1 through FSR3Unity

- Repository: https://github.com/ndepoel/FSR3Unity
- Pinned commit: `71879e71aa72465a79bdf009d7d71f9f7ab83066`
- Nico de Poel's MIT notice: `FSR3UNITY-LICENSE.txt` in the release.
- AMD FidelityFX MIT notice: `FSR3-LICENSE.md` in the release.

`Build-Fsr3.ps1` derives Unity 2019-compatible C# from the unmodified source under
vendor/FSR3Unity. It replaces unavailable compute constant-buffer APIs with global
constant-buffer bindings, uses immediate buffer uploads supported by this engine,
and builds fixed SDR/reversed-depth/low-resolution-motion shader permutations.
Sharpening selects a separate accumulation kernel. `src/Fsr3Compatibility.cs`
owns persistent per-context scratch textures and supplies missing UAV bindings
for luminance, shading change, reactivity and accumulation passes.

`native/Fsr3BundleBuilder.cs` builds the compute-only resource bundle with Unity
2019.4.40f1, enabling the built-in AssetBundle package. The editor is a build tool
and is not distributed. `src/Fsr3Upscaling.cs` provides jittered scene rendering,
depth/motion inputs, temporal history/reset, full-resolution composition and AA
restoration. The integration currently runs after the game's scene postprocessing.
Automatic reactive masks and material mip-bias adjustments are not enabled;
transparent effects and animated foliage need further visual evaluation.

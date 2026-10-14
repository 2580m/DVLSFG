# DVLSFG upstream provenance

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

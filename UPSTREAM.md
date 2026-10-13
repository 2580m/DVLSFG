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

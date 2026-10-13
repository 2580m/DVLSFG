# DV LSFG 0.3.2 (experimental)

Version 0.3.2 fixes resolution-change hangs with frame generation enabled by
recovering outdated swapchains, draining generated work and completing a rejected
present's application fence when needed. Resolution changes require no extra
restart; resource reconstruction can briefly pause rendering.

Independent Windows x64 Unity Mod Manager mod requiring DVVulkan with deployed DXVK.
DVVulkan provides D3D11-to-Vulkan; DVLSFG provides an LSFG 3.1 Vulkan layer.

Install through UMM. Enable DVVulkan and complete its deployment first. Start the
game normally, enable DVLSFG, enter the full path to your installed Lossless.dll,
save and run the compatibility check. In the game's Settings / Graphics panel,
enable LSFG frame generation and press the native Apply button.
After `Probe passed` in probe.log, exit once for automatic deployment, then start
normally through Steam or your existing game shortcut.

No special launch script is required. UMM deploys a game-local vulkan-1.dll
bootstrap that reads UMM/config state before the first Vulkan call, enables the
layer only for this game process, and forwards to the system Vulkan loader.
First installation needs one restart. Existing foreign vulkan-1.dll files are
never overwritten. DVVulkan's d3d11.dll/dxgi.dll stay unchanged and are verified
against the installed dependency. No global registry, system DLL or Steam launch
option changes are made.

Supports 2x/3x/4x, standard/performance models and flow scale 0.25–1. Restart after
changing those settings or the model path. The UMM toggle pauses/resumes an already
initialized backend. The UI reports actual native loading and processed input /
generated frame counts, rather than treating UMM enablement as successful synthesis.
Processed input counts cover successful frame generation presents, excluding pauses.
Version 0.2.0 integrates LSFG enablement, multiplier, model and flow quality into
the native Graphics settings panel, including Apply/Discard and unsaved-change
confirmation. A scroll area keeps all original graphics options accessible.
The fixed preset area is separated using the game's Advanced settings divider style.
The UMM mod toggle is separate from the saved frame-generation option.
Both controls and actual frame generation work on ordinary launches. Enabling
after an off startup or changing model/multiplier/quality requires a normal
restart; initialized generation can pause/resume immediately. DLL configuration
remains in UMM.
The UI and metrics.ini report input/submitted rates and presentation processing
time including synchronization waits. These are not monitor refresh or input
latency measurements. The comparison capture button writes input/generated BMP
pairs to captures; use it for diagnostics because readback adds a temporary stall.

Version 0.3.2 uses IMMEDIATE presentation instead of forcing FIFO. Surfaces without
IMMEDIATE support pass through with an unavailable diagnostic. Unity's frame limit
continues to control input; generation attempts 2/3/4x submissions. High-resolution
pacing spreads output calls, excluding its own waits from uncapped cost estimation.
GPU/CPU/synchronization costs can lower input FPS. IMMEDIATE permits submissions
above refresh and can tear; displays cannot show every such frame in full.
Local RTSS still reports the game's original Vulkan call rate. Displayed cadence
and driving smoothness remain unverified; submission FPS is not displayed FPS.

Prototype scope: non-VR, SDR, one game Vulkan instance/rendering device, single-layer
8-bit RGBA/BGRA swapchains. Unsupported multi-swapchain presents, formats and some
extension combinations pass through. No HDR, VR, scaling or dual-GPU offload.
This implementation waits for GPU completion before resource recycling. Game FPS,
visual artifacts, latency and displayed refresh rate during driving still need
testing; synthesis counts do not establish a performance gain.

Validated on RTX 3070 Ti with a hidden Vulkan window: 12 inputs / 12 synthesized
frames at standard 2x, and 12 / 36 at performance 4x, zero Vulkan validation errors
and clean shutdown. Disabled pass-through and standard 2x alongside installed
automatic overlay layers also passed. These checks do not verify Unity gameplay.
Pause/resume and swapchain recreation tests passed with exact counts and zero validation errors.
Combined present mode/ID/fence metadata and secondary device teardown also passed.
Readback proved intermediate pixels: previous/current input red values 43/64
produced red 53 in the standard 2x generated image.
Actual Derail Valley menu testing at 1920x1080 with UMM 0.32.4 and DVVulkan 1.0.1
completed roughly 690,000 real and 690,000 generated frames with normal shutdown.
The last one-second input/submitted rates were about 82.5/165 FPS; static menu
captures showed no obvious corruption. Vulkan validation used version 1.4.363;
the older 1.4.309 validator does not support the deployed DXVK's newer extensions.
Driving, VR, HDR and end-to-end input latency were not validated.
The final synchronization fix completed another 4,872 menu frame pairs with
full synchronization/thread validation, no reported errors and clean shutdown.
See VALIDATION.md for detailed scope and test-environment isolation notes.
A tested Lossless.dll 3.2.2.0 translated all 52 LSFG 3.1 shader mappings; the tested
3.0.0.1 file was rejected for missing resources. Always run the resource probe.

Recovery: disable frame generation and restart, or use Launch-Without-LSFG.cmd
to bypass generation for that process, including with a damaged config. DXVK
remains under DVVulkan's control. Inspect bootstrap.log, deployment.log, probe.log
and native.log. To uninstall, disable DVLSFG in UMM, exit for automatic removal of
its owned bootstrap, then remove the mod. If its folder is deleted directly,
the remaining bootstrap forwards ordinary Vulkan without loading LSFG.

No Lossless.dll or extracted model shaders are bundled. Based on MIT-licensed
lsfg-vk v1.0.0, with Win32 resource sharing and Windows presentation changes.
See UPSTREAM.md and the packaged lsfg-vk, DXBC and Volk licenses.

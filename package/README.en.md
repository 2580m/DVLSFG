# DV LSFG 0.5.0 (experimental)

0.5.0 excludes screen-space Canvas UI from LSFG input. The scene is interpolated,
then current opaque/translucent UI is composited at native resolution on each
generated frame. UI updates at the game's original frame rate. World objects,
including locomotive instruments, remain part of the generated scene. Works with
upscaling Off, FSR 1.0 and FSR 3.1; missing scene input keeps normal presentation.
Opening UMM's IMGUI window pauses generation; closing it resumes generation.
Native HUD uses overlay canvases. Camera-space canvases supplied by other mods
also keep ordinary presentation, avoiding interpolation or duplication of baked-in UI.

0.4.15 adds FSR 3.1 temporal upscaling with depth, motion vectors, projection
jitter and frame history. Selecting it immediately disables native AA controls;
Apply replaces native AA, and switching back/off restores its previous settings.
Five quality modes and sharpening remain available. This integration runs after
scene postprocessing; transparent effects and animated foliage need more visual testing.

0.4.14 moves interface text to a bundled local `translations.csv` through DV Language
Helper. Install and enable DVLangHelper. Language follows the game and helper
translation overrides are supported. No online translation download is used.

0.4.13 changes the installation directory and archive root to `Mods/dv-scaling`.
Move the entire old `Mods/DVLSFG` directory to retain configuration when migrating;
avoid keeping duplicate installations.

0.4.12 adds Ultra Performance (one third of output width and height). Quality
increases left-to-right: Ultra Performance, Performance, Balanced, Quality, Ultra Quality.
Existing scaling ratios and configuration values remain unchanged.

0.4.11 immediately grays out and disables dependent controls when their technology
is Off, retaining values on re-enable. The generation option reads LSFG 3.1.
Blank viewport space accepts wheel scrolling; section dividers retain native clearance.

0.4.8 separates technology and quality: Frame Generation selects Off/LSFG,
Upscaling Technology selects Off/FSR 1.0, with Upscaling Quality beneath and a
reserved empty slot to its right. Sharpening has a generic label. A native-style
divider separates these controls from the game's settings. Quality is retained when off.

0.4.7 fixes world picking offsets with FSR enabled. Scene-camera mouse rays use
display coordinates instead of the smaller render-target dimensions. UI input
coordinates remain unchanged.

0.4.6 orders the FSR selector left-to-right as Off, Performance, Balanced, Quality,
Ultra Quality. Persisted mode values and rendering scales remain compatible.

0.4.5 applies generation model and optical-flow quality changes live, alongside
multiplier changes. Use Graphics Apply or UMM Save on an initialized layer. DLL
path changes still require restart; rebuilding resources can briefly pause rendering.

0.4.4 moves LSFG status, model/flow restart requirements and FSR rendering/output
information into native hover descriptions. Read-only status controls and the FSR
apply-timing notice have been removed.

0.4.3 applies 2x/3x/4x multiplier changes live on an initialized frame-generation
layer, with a brief pause. Model, flow quality and DLL changes still need restart.

0.4.2 replaces the FSR sharpening selector with a native 0–100% slider, 1% steps
and a live percentage readout. Native Apply/Discard behavior is retained.

0.4.1 removes the half-second global Unity object scan responsible for periodic
long frames with FSR enabled. Cameras register on creation; hidden Graphics
panels no longer perform periodic refreshes. Existing FSR/LSFG settings are retained.

## FSR 1.0 upscaling

Select FSR 1.0 under Upscaling technology in the native Graphics menu and Apply. Quality modes are
Ultra Performance, Performance, Balanced, Quality and Ultra Quality, with 0–100% sharpening. Zero
disables RCAS. FSR settings apply without restarting; start with Quality/Ultra
Quality and 20% sharpening. FSR is disabled by default and independent of LSFG.

Screen resolution remains output resolution. Scene cameras render into smaller
targets, then EASU/RCAS upscale after scene AA/postprocessing. Screen Space Overlay
HUD/menu/hotbar retain output resolution. The existing LSFG path receives the
completed full-resolution image. FSR itself needs no Lossless.dll and does not
replace game AA. CPU-limited scenes may gain little; LSFG still runs at output size.
Support is non-VR, primary display and full-screen scene cameras. Forward/deferred
rendering have integration paths; rain and all postprocessing combinations still
need broader scene testing. GPU setup errors restore the original camera target;
change mode/resolution to retry. Disabling the mod or FSR restores camera ownership.

Version 0.4.2 fixes resolution-change hangs with frame generation enabled by
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

Supports live 2x/3x/4x, standard/performance models and flow scale 0.25–1. Restart after
changing the DLL path. The UMM toggle pauses/resumes an already
initialized backend. The UI reports actual native loading and processed input /
generated frame counts, rather than treating UMM enablement as successful synthesis.
Processed input counts cover successful frame generation presents, excluding pauses.
Version 0.2.0 integrates LSFG enablement, multiplier, model and flow quality into
the native Graphics settings panel, including Apply/Discard and unsaved-change
confirmation. A scroll area keeps all original graphics options accessible.
The fixed preset area is separated using the game's Advanced settings divider style.
The UMM mod toggle is separate from the saved frame-generation option.
Both controls and actual frame generation work on ordinary launches. Enabling
after an off startup requires a normal
restart; initialized generation can pause/resume immediately. Multiplier/model/flow changes
apply through Graphics Apply or UMM Save, briefly waiting for the GPU and rebuilding
generation resources with fresh history. DLL configuration
remains in UMM.
The UI and metrics.ini report input/submitted rates and presentation processing
time including synchronization waits. These are not monitor refresh or input
latency measurements. The comparison capture button writes input/generated BMP
pairs to captures; use it for diagnostics because readback adds a temporary stall.

Version 0.4.2 uses IMMEDIATE presentation instead of forcing FIFO. Surfaces without
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

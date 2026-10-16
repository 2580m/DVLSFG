# DVLSFG validation record

## 0.5.1 preserve UI antialiasing

Removes FSR3's global MSAA override; owned scene targets remain single-sample,
scene allowMSAA remains false, and PPV2 AA remains None. UI rasterizes into an
independent multisample ARGBHalf/depth-stencil target, then resolves to the
single-sample interop textures. Requested UI samples are max(4, native quality),
limited by hardware support. The same resolved UI is composited on the real
swapchain image as well as generated images, preventing alternating edge quality.

The GPU fixture uses rotated opaque/translucent panels at fractional positions.
All FSR Off/1/3 paths retained native global MSAA 8 and resolved UI 8x. UI edges
had 596 partially covered pixels; presented real/generated edges both had 596,
with maximum RGB difference 0 in the opaque panel region. Interior translucent
colors remained within one channel unit. Live 2x/3x standard and 4x performance,
resize, UMM pause and camera-space fallback/multiplier rebuild passed.

A combined pressure-test/world load timed out without a ready scene camera.
GPU memory was 4771/8192 MiB; no allocation error established a cause. A separate
restart entered the Continue save and passed all five quality modes, forward/
deferred rendering, picking, resize and FSR1/3 transitions. Native world quality
was MSAA 0, while independent UI MSAA stayed 4x and scene/post AA stayed disabled.
This does not establish the cause of the earlier loading interruption.

The baseline Vulkan host capture regression passed with validation 1.4.363;
as above, it does not provide validation coverage of the Unity interop path.
Local evidence: build/ui-aa-first.log and fixture, build/ui-aa-load-interrupted.log,
build/ui-aa-final-fixture/scene-ui.log, build/ui-aa-world.log and fixture,
and build/smoke-50ee041d0ddb4851b5d3a34f7afa3726. The extra UI multisample target
and real-frame composition have memory/GPU cost; no performance gain is claimed.

## 0.5.0 scene-only frame generation

Unity's postprocessed scene is captured before overlay UI, with FSR Off/1.0/3.1
paths. The game's live GraphicRegistry supplies Canvas roots. A temporary camera
renders UI over black/white; a finally block restores mode, camera, transform and
layer state. DXVK interop exposes capture images to the same Vulkan device. The
compute path binds float views of typeless ARGBHalf images, corrects Y orientation
and converts linear/display RGB as appropriate for the swapchain. LSFG receives
the scene only; current UI is blended after generation. The original swapchain
frame is retained. Missing/stale captures are invalidated and bypass generation.
UMM IMGUI and camera-space Canvas UI keep ordinary presentation.

Actual GPU pixel checks passed opaque and 50%-transparent overlay UI with two
distinct colors, across FSR Off/1.0/3.1. Captured scene samples excluded UI, and
generated UI differed from original by at most one 8-bit channel unit. Live
2x/3x standard and 4x performance paths passed exact generated counter ratios;
resize to 1280x720 rebuilt captures. Canvas/layer restoration and UMM pause were
checked. The real Continue save passed five FSR quality modes, forward/deferred,
mouse rays, resize and FSR1/3 switches while scene/UI capture remained active.
A static world original/generated pair differed by mean 0.206/255 over sampled
RGB channels and retained correct orientation. This is not a motion-artifact test.

The existing Vulkan host capture and swapchain recreation regressions passed;
the latter ran with synchronization/thread validation 1.4.363. These host cases
do not exercise the new Unity interop/compute path under validation. The new path
is verified by real GPU readbacks and game runs, not a claim of complete Vulkan
validation coverage. Additional UI rendering/composition has CPU/GPU cost; no
cross-GPU performance benefit or broad custom IMGUI compatibility is established.

Local evidence: build/scene-ui-pixels.log and fixture; build/scene-ui-world.log
and build/scene-ui-world-fixture; build/scene-ui-multipliers.log and fixture; build/scene-ui-final.log and fixture;
build/scene-ui-startup.log and build/scene-ui-startup-fixture. Production defaults
to scene-only before UMM initializes. Camera-space UI also bypasses generation
during multiplier reconstruction. Descriptor caches become reusable only after
all allocations/bindings succeed. The final native build passed the host capture
regression with validation under build/smoke-9fa9235828a14823999fb2ded5767325.
Vulkan host evidence: build/smoke-3a4e334bface4d49b14eab1ad8f17ddd and
build/smoke-a3cc1c5ab48349baaa5783630f7a6513.

## 0.4.15 FSR 3.1 temporal upscaling

FSR3Unity's actual FSR 3.1 compute backend is ported to Unity 2019.4; it is distinct
from the FSR1 EASU/RCAS plugin. Shader permutations are fixed for SDR D3D11/DXVK,
reversed depth and low-resolution motion vectors. GPU checks exposed missing
scratch UAV bindings in the reference backend, which are supplied by the port.
The five scratch targets persist per context; shader binding metadata is cached.
Scene cameras provide jitter, depth/motion, history resets and a full-resolution
compositor. Components are disabled when unused, avoiding an extra image-effect
copy in FSR1/off mode. Existing FSR1 configuration retains its mode meanings.

Synthetic GPU checks passed all five quality sizes, nonblack matching color
readback, sharpening and MSAA/target restoration on Off. UI checks passed immediate
native AA disabling when FSR3 is selected, Discard restoration, language switching
and the existing Apply/Discard/live-setting regressions. Real game-world checks
passed five quality modes, finite nonblack GPU output, enabled depth/motion inputs,
disabled native MSAA and PPV2 AA in both forward/deferred rendering,
display-coordinate picking, resizing to 1280x720 and FSR1/3
transitions. Config is restored after test runs. These checks do not establish
artifact-free transparent effects or animated foliage; reactive masks/mip bias
and placement before selected postprocessing remain future improvements.

Evidence: build/fsr3-gpu.log, build/fsr3-gpu-fixture/fsr3-smoke.log,
build/fsr3-ui.log, build/fsr3-ui-fixture/passed.txt, build/fsr3-world-first-fixture,
build/fsr3-world-supplement.log and build/fsr3-world-supplement-fixture.
Final resource-release regression: build/fsr3-final-gpu.log and
build/fsr3-final-gpu-fixture/passed.txt. The synthetic test releases any FSR3
active at startup before capturing native AA, avoiding an overridden baseline.

## 0.4.14 local CSV localization

Moves 62 bilingual UI/status/description terms into the bundled translations.csv.
DVLangHelper is a declared UMM dependency. The source ID is dv_scaling, keys use
the dv_scaling/ prefix, and runtime lookup uses DV.Localization.LocalizationAPI.L.
An early-startup English fallback is loaded from the same CSV. English/Simplified
Chinese text is retained, other languages use English, and Traditional Chinese
retains the previous Simplified Chinese text until a separate translation is added.
No helper binaries or online translation requests are bundled.

CSV checks passed unique keys, English/Chinese contents and every source reference.
The installed game registered the source through Language Helper. In-game checks
passed English/Chinese switching, German English text, translated quality values
and quoted multiline descriptions. The original language was restored. Existing
layout, blank scrolling, dependent controls, Apply/Discard, sharpening and live
model/flow regression checks passed. Configuration was restored byte-for-byte.
Evidence: build/localization-ui.log and build/localization-ui-fixture/passed.txt.

## 0.4.13 installation directory

The package root and installed directory are dv-scaling. The bootstrap selects
this directory, retaining a legacy DVLSFG fallback. Deployment validation accepts
both known paths; UMM identity remains DVLSFG to preserve enablement and dependency
references. Launcher and UI fixture paths now use dv-scaling.
Deployment safety checks, automatic bootstrap activation/guards/recovery and
launcher checks passed against the new installation path. Existing configuration
is retained when migrating the whole directory.
The installed game launched through the desktop shortcut with automatic LSFG
enabled from dv-scaling. Graphics UI and live settings checks passed.
Evidence: build/directory-ui.log and build/directory-ui-fixture/passed.txt.

## 0.4.12 Ultra Performance upscaling

Adds mode 5 at one third of output width and height; modes 1..4 keep their original
ratios and persisted meanings. Chinese Ultra Quality is renamed to 超级质量.
Quality increases left-to-right from Ultra Performance to Ultra Quality.
Settings checks cover mode 5 persistence, quality retention while Off, legacy
configuration and invalid values. The in-game UI fixture covers all five mappings,
Apply/Discard, technology availability and existing scrolling/layout regressions.
The synthetic GPU fixture covers all five modes, explicitly checking mode 5's
one-third render dimensions, output dimensions, native dispatch and color values.
Evidence: build/ultra-performance-ui.log, build/ultra-performance-ui-fixture,
and build/ultra-performance-gpu-fixture/fsr-smoke.log.

## 0.4.11 dependent control availability

The pending technology selection immediately updates native interactability:
Off disables generation multiplier/model/flow, or upscaling quality/sharpening.
Selectors and SliderDV use their native ToggleInteractable behavior. Technology
switches stay enabled and dependent values are retained. In-game checks passed
Off/On updates before Apply, value retention and Discard restoration, followed by
the existing blank-space scrolling, layout, Apply/Discard and live settings checks.
Evidence: build/dependent-controls-ui.log and build/dependent-controls-ui-fixture/passed.txt.

## 0.4.10 scrolling over blank space

The scrolling viewport has a transparent raycastable Image so empty cells,
button gaps and section dividers can forward wheel events to the ScrollRect.
Its bounds exclude the fixed preset area. In-game EventSystem raycasts at the
reserved empty cell and technology divider hit this viewport, and dispatching
wheel events through those hits moved the content. Existing layout, descriptions,
Apply/Discard, sharpening and live model/flow checks passed.
Evidence: build/blank-scroll-ui.log and build/blank-scroll-ui-fixture/passed.txt.
No Computer Use was required; launch used the desktop Steam shortcut.

## 0.4.9 section divider clearance

The technology/native section divider now uses the Advanced divider's row height
and surrounding layout spacing instead of stretching the graphic to fill its row.
In-game geometry checks measured a 35-unit row and 20-unit centered graphic,
leaving 7.5 units above and below, matching the native row's clearance.
The existing UI fixture also passed section ordering, the reserved quality gap,
descriptions, Apply/Discard, sharpening and live model/flow checks.
Evidence: build/divider-ui.log and build/divider-ui-fixture/passed.txt.
The game was launched using the desktop Steam shortcut; no Computer Use was used.

## 0.4.8 technology selectors and section layout

Frame Generation selects Off/LSFG. Upscaling Technology selects Off/FSR 1.0,
while Upscaling Quality independently selects Performance→Balanced→Quality→Ultra
Quality and Sharpening uses a generic label. Persisted FsrMode remains compatible;
FsrQuality retains the quality selection while mode is Off. Existing enabled modes
initialize the quality field without changing their rendering scale.

The scroll content uses a vertical stack of two native-style two-column grids,
separated by the Advanced settings divider graphic. The technology grid contains
seven controls plus an empty right-hand cell below sharpening. Native controls
start in the separate grid, so they cannot occupy the reserved gap. Disabling the
mod hides the whole custom section and its divider. The existing fixed-preset
divider is retained. Configuration roundtrip/migration/invalid-value checks passed.
The game was launched through the user's desktop Steam shortcut. Automated UI
checks passed the technology choices, quality's position below the technology row,
reserved cell, full-width section divider, all four quality mappings, quality retention
while Off and reuse on enable. Apply/Discard, 0/37/100% sharpening and live model/flow
regressions passed. Native controls stay under their settings provider during reparenting.
Reset notifications for still-inactive sliders whose Awake cache is uninitialized
are deferred until their normal OnEnable synchronization, fixing an Apply/Discard
exception exposed by the reorganized hierarchy. No Computer Use was required.
Evidence: build/technology-ui.log and build/technology-ui-fixture/passed.txt.

## 0.4.7 FSR world picking

FSR sets the scene camera's targetTexture to a smaller render target. Unity's
ScreenPointToRay interprets pixel coordinates against that target, whereas mouse
input remains in display pixels. Cab grabbing, junction selection, hover detection
and external camera interactions use that API, explaining their visible offset.

A Harmony prefix on the managed Camera.ScreenPointToRay(Vector2, eye) wrapper
shared by the public overloads converts display pixels to viewport coordinates and
uses ViewportPointToRay, only for Mono rays from a camera still owning its FSR target.
The cached camera registry avoids global scans. Input.mousePosition, overlay UI,
unowned render targets and stereo rays are untouched. Ownership rather than the
requested setting also covers the frame between disabling FSR and releasing its target.

Actual scene tests compare both public ray overloads at six screen locations with
viewport rays and hit a small physical collider independently positioned with
ViewportToWorldPoint. A separate render-target camera verifies native pixel semantics.
The picking-only fixture needs no mouse input or foreground automation. It skips the
unrelated live-generation stress sequence while retaining view/mode/resize coverage.
The real-save run passed at 2560x1440 in first-person and ExternalCam views, all
four FSR modes, a 1280x720 resize, FSR Off, and restoration of the original Balanced
mode and resolution. Each of the nine stages passed six physical-target probes,
both public Mono ray overloads and the unaffected-camera control. All user mods
remained installed/enabled; no physical clicks on locomotive controls were performed.
Evidence: build/fsr-picking-world-fixture/world.log and build/fsr-picking-player.log.

## 0.4.6 FSR selector order

The selector now displays Off→Performance→Balanced→Quality→Ultra Quality.
Nonzero selector indexes map to persisted modes with 5-index, and Refresh uses
the inverse mapping. Mode 0 remains Off; existing saved modes and FSR rendering
scales are unchanged. The existing UI smoke assertion follows the new mapping.
Managed compilation passed; no additional game/GPU run was needed for this UI-only change.

## 0.4.5 live model and flow quality

Model and flow scale join multiplier in one synchronized request/snapshot API. The
present-thread rebuild finalizes both backend variants after context destruction,
then initializes the selected model with its new flow scale. Partial backend
initialization is also cleaned up during finalize. Managed settings retain actual
applied values until native completion. Legacy multiplier exports remain compatible.
DLL paths and loading the layer after an off startup still require restart.

GPU validation passed Standard↔Performance and flow 25/50/75/100%, combined
multiplier/model/flow requests, disabled-period changes, coalesced requests, and
invalid arguments including NaN. The mixed sequence produced 10 generated input
presents / 15 generated frames, with an independent trace of 27 actual WSI presents.
After the final switch, previous/current red values 212/234 produced generated red
223. The interleaved resize/fence test and legacy multiplier regression were clean.
A small valid swapchain makes flow 25% unsupported; the recovery test verified
failure state, restoration of the previous flow and continued rendering, followed
by successful switches to Performance/50% and Standard/75%, with clean validation.

Game Graphics Apply verified model and flow take effect without restarting and
restore correctly. Labels are now “帧生成倍率” and “帧生成模型”; the multiplier
restart/pause paragraph and model/flow restart notes are removed. Native footer,
Apply/Discard and sharpening regressions passed. No Computer Use was used.
Evidence: build/live-settings-smoke.log, live-settings-resize.log,
live-settings-recovery.log, live-settings-multiplier-regression.log,
live-settings-ui.log and live-settings-ui-native.log.

With FSR active in the existing game save, all eight combinations completed:

| Multiplier | Model | Flow | Input | Generated |
| --- | --- | ---: | ---: | ---: |
| 2x | Standard | 25% | 163 | 163 |
| 3x | Performance | 50% | 240 | 480 |
| 4x | Standard | 75% | 236 | 708 |
| 2x | Performance | 100% | 248 | 248 |
| 3x | Standard | 25% | 245 | 490 |
| 4x | Performance | 50% | 236 | 708 |
| 2x | Standard | 75% | 248 | 248 |
| 4x | Performance | 100% | 237 | 711 |

The fixture now waits for at least ten completed FSR GPU frames and valid target
dimensions, after an initial fixed-delay check raced scene startup. These counts
verify generation execution, not perceived smoothness or overlay counting.
First-person/external views, all FSR modes and a 1280x720 resize at Performance/4x
passed. Original Standard/2x, flow 75%, FSR Balanced and sharpening 75% settings
were restored; the game was closed and temporary fixtures removed. World evidence:
build/live-settings-world-fixture/world.log and build/live-settings-world-native.log.
Automatic bootstrap's seven scenarios and the launcher's environment, disabled,
recovery and DLL-conflict guards passed (build/live-settings-bootstrap.log and
build/live-settings-launcher.log); the DLL probe translated all 52 shader mappings.

## 0.4.4 native descriptions

Graphics now contains five editable selectors and one sharpening slider. Removed
the four read-only LSFG status/apply and FSR render/output/apply rows. Each control
uses the game's UIElementTooltip and a dynamic custom description through the
native TooltipHandler footer. LSFG enable help includes current status; model help
has separate Standard/Performance lines and its restart requirement; flow quality
also explains restart. FSR mode/sharpening help includes runtime rendering/output
and omits any apply-timing notice. Visible panel refresh updates changing descriptions;
hidden panels still perform no periodic object scans.

Actual game fixture verified all six controls route their descriptions to the native
footer, model help contains three lines, and the four status/prompt controls are absent.
Apply/Discard, LSFG toggles, FSR modes and 0/37/100% sharpening regressions passed.

## 0.4.3 live multiplier

The managed UI posts a validated 2x/3x/4x request. The next intercepted present
serializes it with swapchain creation/destruction, destroys contexts after both
devices finish, finalizes the backend whose generation count is fixed at initialization,
and recreates contexts from retained swapchain/image metadata. It resets history and
pacing without replacing the game's WSI swapchain. Failure attempts to restore the
previous multiplier; if recovery also fails, original presents continue without synthesis.
Model, flow scale and DLL changes remain restart-only. An off startup still requires
restart to load the generation layer. Allocation/device-loss recovery is implemented,
but deliberate GPU OOM/device-loss injection was not tested.

Standard and performance GPU smoke tests passed 2→3→4→2, a disabled-period change,
invalid values, and coalesced requests, with 11 synthesized input presents / 20 generated
frames. An independent API dump counted the expected 32 actual vkQueuePresentKHR
calls (12 original plus 20 generated). Vulkan synchronization and lifetime validation was clean, including interleaved
resize with application present metadata/fences. After switching back to 2x, framebuffer
samples were previous red=191, generated=202 (standard)/201 (performance), current=212.
This verifies interpolation, not perceived motion smoothness or RTSS counting.

Actual game UI Apply/Discard, enable toggle and sharpening regression passed. In the
existing save with FSR active, successive sampling windows recorded:

| Multiplier | Input frames | Generated frames |
| --- | ---: | ---: |
| 3x | 240 | 480 |
| 4x | 225 | 675 |
| 2x | 247 | 247 |
| 4x | 234 | 702 |

First-person/external views and all FSR modes passed. At 4x, resizing to 1280x720
and restoring 1920x1080 completed without hanging. The original 2x, flow 75%, FSR
Balanced, sharpening 75% configuration was restored; temporary fixtures were removed
from the game and the test game was closed. Automatic bootstrap (seven paths), launcher
guards and 52-shader DLL compatibility checks passed.
Evidence: build/live-smoke.log, live-performance.log, live-resize.log, live-ui.log,
live-world-fixture/world.log, live-world-native.log, live-bootstrap.log, live-launcher.log.

## 0.4.2 FSR sharpening slider

FSR sharpening now uses an inactive clone of the game's native SliderDV settings
control, with original preference/localization bindings removed before activation.
The slider ranges from 0 to 100 with whole-number steps and a live percentage
readout; values remain pending until the original Apply action. Graphics contains
nine selectors plus one slider, with the existing scroll/preset divider preserved.
The game fixture verifies 0%, 37%, 100%, percentage text, Apply/Discard and saved
values. Actual mouse dragging changes 70% to 36% and enables Apply/Discard; clicking
Discard restores 70%. Native layout was visually inspected. Evidence: build/slider-ui.log.

## 0.4.1 periodic FSR hitch

The user's Quality FSR (sharpness 0.7), standard LSFG 2x/flow 0.75 configuration
reproduces a half-second hitch in an existing save at 1920x1080. A temporary UMM
fixture instruments FsrRuntime.Tick and GraphicsSettingsPanel.Refresh with Harmony,
buffers per-frame timings/GC counts in memory, and writes CSV only after sampling.
The 0.4.0 baseline contains 3101 frames over 40 seconds: median 11.9473 ms,
p99 57.9456 ms, maximum 64.9613 ms. All 80 periodic global scans correspond to
the next frame's long interval (mean 57.8455 ms); other intervals average 11.7289 ms
and peak at 12.896 ms. Individual scans take about 52 ms. GC0 remains 106 and
hidden UI refresh is 0 ms throughout this trace.

0.4.1 discovers pre-existing cameras once when FSR is enabled, then registers new
game cameras through CameraGraphicsUpdater.Awake. FsrSceneCamera maintains a small
lifetime-bound registry used for status queries; no global scan occurs during
steady gameplay. Hidden Graphics panels no longer periodically rebuild their rows.
The LSFG/native rendering binaries and synchronization behavior are unchanged.
Evidence: build/pacing-baseline.csv and pacing-baseline.log; follow-up data is
recorded in build/pacing-fixed.csv and pacing-fixed.log.
The same-config 0.4.1 trace contains 3298 frames: median 12.1007 ms, p99
12.7350 ms, maximum 57.9208 ms. FSR Tick peaks at 0.0124 ms with zero calls
over 0.3 ms; frames over 30 ms fall from 80 to one. That one isolated long
frame is not a periodic FSR scan (Tick 0.0008 ms, no GC transition). The fixed
half-second hitch is resolved; this is not a claim of zero long frames or a
universal performance uplift. User configuration is preserved.
The native Graphics preview, LSFG and FSR Apply/Discard, and toggle/status regression
all pass with 0.4.1 (build/pacing-ui-regression.log).

## 0.4.0 FSR 1.0 integration

FSR uses the official pinned AMD EASU/RCAS implementation on Unity's D3D11 device,
translated by the installed DXVK 3.1.1. Both system D3D11 and game DXVK GPU tests
passed display-encoded constant colors, odd-size borders, and compute-state
restoration. A 67x39 diagonal-edge input upscaled to 127x73 differs from analytic
bilinear interpolation in 331 pixels by more than 3/255 (maximum difference 42/255).
Zero sharpening uses EASU without RCAS and passes the color/border test.

Game tests use Unity 2019.4.40f1, installed DVVulkan 1.0.1, standard LSFG 4x and
flow scale 0.75. A synthetic forward camera passes all four modes, MSAA 8x,
enable/disable, camera disable/re-enable, and 2560x1440 <-> 1280x720 resizing.
At Quality the actual input is 1707x960 for 2560x1440 output. Readbacks preserve
RGBA(128,64,191,255). Early hidden-window runs did not render scene cameras;
those are failed tests, not GPU or display evidence. Early command-buffer output
also produced a black screenshot despite valid FSR texture data; final compositing
uses the OnRenderImage display destination and avoids Camera.main's low-resolution
target fallback. The final asynchronous PNG screen readback matches the FSR texture
at RGBA(128,64,191,255); the overlay marker remains RGBA(255,255,255,255).
A synchronous texture readback at WaitForEndOfFrame hung the test fixture; that
diagnostic is not used by the mod or counted as passing evidence. The asynchronous
screen-capture path passes without the end-of-frame GPU readback.

An existing Continue save passes actual deferred first-person and ExternalCam
selection, all quality modes, and live resize with LSFG 4x. Camera ownership and
pre-test resolution/FSR mode are restored. Actual game screenshots were inspected;
the HUD, hotbar, interaction info and auxiliary canvases remain Screen Space Overlay.
Native Graphics has ten selectors, and LSFG/FSR Apply/Discard/persistence tests pass.
Ordinary bootstrap's seven Enabled/Disabled/UMMDisabled/DependencyDisabled/MissingMod/
DXVKConflict/Recovery cases also pass. Launcher testing must run after the real game
exits; its initial rejection of a simultaneously running game is expected.

Evidence: build/fsr-final-gpu-dxvk.log, fsr-world-clean-final.log,
fsr-world-pass/world-quality.png, fsr-world-pass/external-quality.png,
fsr-ui-world-final.log, fsr-bootstrap-regression.log and fsr-screen-final-pass.log.
The current game-scene test is a stationary daytime scene. Driving motion, rain,
night, every postprocessing combination, and broad GPU compatibility are not fully
validated. No FSR performance uplift or input-latency figure is claimed. The full
postprocess stack currently precedes FSR, including any enabled grain; LSFG continues
to process output-resolution images. No game frame limit or present mode is changed
by FSR. Original DLL path/model/multiplier/flow settings and DVVulkan files are preserved.

## 0.3.2 resolution-change recovery

The user reported a hang while applying a resolution change with standard 4x,
flow scale 0.75. The installed log ended with generated present OUT_OF_DATE,
which 0.3.1 converted to INITIALIZATION_FAILED before the original present.
Initial recovery reproduced a second failure: the original resize present's
application fence remained unsignaled on the local WSI path, while DXVK marks
normal swapchain errors as completing queue operations and waits on that fence.

The fix bounds generated-image acquisition to 100 ms, interrupts added presents
on normal surface errors, consumes all remaining compute-output signals, and
forwards the original present metadata. It returns the actual surface error.
After device completion, an unsignaled fence associated with the rejected
original present is reset and completed by an empty queue submission. Successful
present fences are untouched. Metrics count actual successful real/generated
presents rather than assuming the configured multiplier on interrupted batches.

The host now tests actual size changes, including resizing after acquiring and
rendering the original frame. Standard/performance 4x error recovery passes with
core, synchronization and thread validation enabled, including bounded waits for
the application's present fences. Standard 2x size recreation and intermediate
pixel capture also pass. No validation errors in the final cases.

Actual game standard 4x/0.75 successfully switched through 1280x720, 2560x1440,
1920x1080, 1024x768 and back to 2560x1440, with frame generation resuming after
each swapchain reconstruction. The first failed reproduction had left the window
at 1280x720; cleanup separately restores the pre-test 1920x1080 Windowed state.
The temporary UMM fixture changes Screen resolution only; no saved gameplay was
entered. Model/multiplier/flow settings and DVVulkan binaries were preserved.

Evidence: build/resize-game-pass.log, resize-game-native-pass.log,
resize-fence4.log, resize-performance4-final.log and resize-size2-final.log.

## 0.3.1 uncapped presentation and output scheduling

Removed forced FIFO both at swapchain creation and dynamic per-present metadata.
IMMEDIATE support is queried; unavailable surfaces retain the original swapchain
without frame generation. Dynamic mode restrictions are cloned through the
NVIDIA latency prefix, preserving application memory. Input limits are read from
Unity Application.targetFrameRate on update. Output scheduling uses high-resolution
waitable timers and excludes its own waits from uncapped work estimates. Two
redundant device-idle waits were replaced by explicit semaphore dependencies;
the final resource-recycling wait remains. Real-present errors no longer increment
the successful batch counters.

Actual game, RTX 3070 Ti, 1920x1080, standard 2x, flow scale 0.75, RTSS running:
the temporary UMM fixture changed only transient Unity input limits and restored
both the original limit and runInBackground after the test. Final samples:

| Input setting | Input FPS | Submitted FPS |
| --- | ---: | ---: |
| 30 | 28.978 | 57.956 |
| 60 | 56.165 | 112.330 |
| 120 | 106.974 | 213.948 |
| Unlimited | 349.909 | 699.818 |

A later unlimited sample was 326.925 / 653.850 FPS. This removes the former
82.5 / 165 refresh-dependent ceiling. RTSS shared memory still reported the
original input rate (331.693 in another unlimited sample), with API flags 65546
(Vulkan). Explicitly requesting RTSS after DVLSFG did not change that statistic.
No RTSS counters or user RTSS settings were modified.

Two additional monitoring experiments (public-dispatch re-entry and an outer
implicit registration) passed isolated synchronization/pixel tests but did not
make RTSS report the generated-frame rate. They were removed before packaging;
0.3.1 retains the original explicit-layer ordinary-launch architecture. The
release DLL is the same tested binary retained in the final capture fixture,
not either experimental dispatch build. Test environment variables are restored
after Vulkan-Smoke.ps1 so they cannot leak into later ordinary launches.

Independent LUNARG API dump below DVLSFG measured 180 inputs -> 360 / 540 / 720
successful vkQueuePresentKHR calls at 2x / 3x / 4x, and 120 disabled inputs ->
120 presents. Timestamped traces show median output-call gaps 17.53 ms for
30-input 2x, 6.53 ms for 60-input 3x, and 2.95 ms for 120-input 4x. API dumping
adds substantial overhead; these are call intervals, not scanout intervals.
All final core/synchronization/thread validation cases passed: captured intermediate
pixels, 3x combined metadata, 4x performance swapchain recreation, toggle and
secondary-device teardown. Standard 2x still produced red 53 between 43 / 64.

The initial adaptive pacing attempt fed its waits back into input estimation,
progressively lowering uncapped FPS. It was fixed before release; the initial
failed rate fixture is retained separately. Standalone PresentMon produced no
usable CSV and lost ETW events; the installed API loader lacked a middleware
path. Early GDI probes returned background/white instead of the scene, and a
captured screen-coordinate lifetime bug was fixed in the test harness. A corrected
D3D11 Desktop Duplication probe then captured the actual visible color-ramp scene:
29.024 input FPS yielded 58.13 visible color changes/second, with 112 original
colors and 112 intermediate colors after warm-up. Inputs have red values divisible
by 8; generated half-step values have remainder 4. These values were read from
the desktop output rather than the application's framebuffer or mod counters.
This establishes visible 2x temporal interpolation in this isolated test. The
disabled probe did not capture usable scene pixels, so no successful disabled
display comparison is claimed. Real-game displayed cadence, RTSS output-rate
reporting, dynamic spatial motion quality, driving and input latency remain
unverified. Evidence: build/no-fifo-desktop-proof-summary.json and
build/smoke-85d4cb9bfd43479fac974bbc76799a3b/screen-pixels.csv.

Evidence: build/no-fifo-rate-fixture/rates.csv, no-fifo-final-player.log,
no-fifo-trace-summary.json, no-fifo-trace-*.log and no-fifo-final-*.log.
The temporary game mod was moved out of Mods after testing. User model settings,
DVVulkan binaries and the normal-start bootstrap were preserved.

## 0.3.0 automatic ordinary-launch integration

UMM deploys an owned game-local vulkan-1.dll bootstrap and removal worker. The
bootstrap initializes on the first forwarded Vulkan call, outside DllMain, sets
process-only layer configuration and loads the original system loader by its
absolute System32 path. It forwards all 265 exports of the build machine's loader
with x64 integer/SSE/stack arguments preserved. No system loader binary is bundled.

Final hidden-window fixtures passed Enabled, Disabled, UMMDisabled,
DependencyDisabled, MissingMod, DXVKConflict and Recovery cases with core,
synchronization and thread validation enabled. The Enabled case generated 12
frames from 12 inputs; captured red values 43/64 produced intermediate red 53.
All other cases forwarded ordinary Vulkan without loading the LSFG layer.
The host keeps its original test executable name and RTSS opt-out; renaming it
to the real game name triggered external profile hooks and an initial timeout.
No global overlay settings were changed to isolate the fixture.

Deployment checks covered initial/repeated installation, owned upgrade/removal,
foreign DLL preservation, conflicting DXVK and protection of a replaced DLL.
Evidence: `build/bootstrap-tests-final.log`, `build/bootstrap-*` and the deployment
fixtures. The game-root d3d11.dll/dxgi.dll remain controlled by DVVulkan.

Actual game: the first ordinary launch installed the bootstrap through UMM and
needed one restart. The next launch used DerailValley.exe with no graphics command
arguments and no preconfigured LSFG/Vulkan layer environment. It loaded both the
game-local bootstrap and the genuine System32 Vulkan loader, automatically loaded
DVLSFG.Native.dll, completed at least 26,460 real/generated frame pairs and exited
normally. The last interval reported 82.497 input / 164.994 submitted FPS and
11.939 ms mean presentation processing time; these are not monitor-refresh or
input-latency measurements. An ordinary off-state launch forwarded system Vulkan
and kept the saved LSFG setting off.

Evidence: `build/bootstrap-first-install-player.log`,
`build/bootstrap-normal-enabled-*`, `build/bootstrap-normal-disabled-*`.
The old launch scripts are optional recovery/compatibility tools. Recovery sets
a process-only bypass that takes precedence over automatic startup and can run
with a damaged mod config. Settings still require restart when enabling from a
startup that did not load the layer, or changing model/multiplier/quality.

## 0.2.0 native graphics settings update

The managed mod now installs four editable LSFG options and two status/help rows
into the game's native Graphics category. It uses native arrow selectors,
Apply/Discard buttons and unsaved-change detection. The panel scrolls to preserve
original graphics options and reserves space for the native preset controls.
The scroll area and fixed preset are separated by a clone of the existing
Advanced settings divider graphic, preserving its sliced sprite and rendering
properties. The extra top/bottom border rules have been removed after an actual
Advanced/Graphics screenshot comparison showed they did not match the native style.
The UMM activation toggle no longer overwrites the saved LSFG on/off setting.

Actual-menu tests verified the six rows, pending-change detection, Apply saving
a multiplier and Discard restoring it without modifying config. The test restored
the original settings afterwards and did not enter saved gameplay. Tests passed
both with the LSFG layer loaded and on an ordinary launch without that layer.
Evidence: `build/graphics-ui-player.log`, `build/graphics-ui-ordinary-player.log`
and `build/graphics-settings-preview.png`. A temporary UMM test module exercises
the actual native buttons; it is excluded from the release and removed afterwards.

Version 0.2.0 verified ordinary-launch visibility but still required a special
launch for generation; version 0.3.0 above removes that requirement. Model/multiplier/
quality changes require restart. The frame-generation backend is unchanged from the final
0.1.0 synchronization fix described below.

Local verification on 2026-10-13. Experimental Windows x64 Vulkan port of
MIT-licensed lsfg-vk v1.0.0, packaged as an independent Unity Mod Manager mod.

## Environment

- NVIDIA GeForce RTX 3070 Ti, NVIDIA 616.92.
- Derail Valley, Unity Mod Manager 0.32.4, installed DVVulkan 1.0.1 / DXVK 3.1.1.
- Lossless.dll file version 3.2.2.0: all 52 required LSFG 3.1 shader mappings
  translated. Tested 3.0.0.1 file lacked required resources and was rejected.
- Visual Studio 2022, Vulkan headers 1.4.309, runtime validation layer 1.4.363.

## Automated checks

Hidden-window Vulkan host tests verified successful presentation, exact counters,
clean teardown and no validation errors:

| Case | Processed real | Generated |
|---|---:|---:|
| Standard 2x | 12 | 12 |
| Performance 4x | 12 | 36 |
| Disabled, 12 ordinary presents | 0 | 0 |
| Pause for two frames, resume | 10 | 10 |
| Swapchain recreation | 12 | 12 |
| Existing implicit overlay layers | 12 | 12 |
| Combined mode, ID and present fence metadata | 12 | 12 |
| Secondary Vulkan device creation/destruction | 12 | 12 |

Readback confirmed intermediate pixels, rather than repeating an input image:
previous/current input red values 43/64 yielded generated red 53 for standard 2x
and 47 for the first performance 4x generated frame.

Additional tests covered immutable caller present metadata, settings UTF-8 and
locale independence, atomic replacement, invalid values and config injection,
launcher environment preservation, disabled guard, recovery launch and deployed
DXVK conflicts. Launcher tests use a fake game fixture.

## Actual game

1920x1080 main menu, standard 2x, standard model, flow scale 1. A long run
completed approximately 690,840 processed real and generated frames and exited
normally. The last saved one-second interval measured 82.499 input FPS and
164.998 submitted FPS, with mean presentation processing time 11.842 ms.

These are submission counters and processing time including waits, not measured
monitor refresh, pure GPU model cost, input latency or proof of a performance gain.
Static-menu input/generated readbacks showed no obvious visual corruption.

Evidence remains local under `build/game-validation-363` and
`build/game-captures`; earlier runs are under `build/game-stability-*`.
The newer validator DLL was confirmed loaded in the game process.
The final rebuilt DLL completed 4,872 real / 4,872 generated frames with normal
shutdown and no reported validation errors or warnings. This final run places
validation below DVLSFG, enabling inspection of injected commands; evidence is
under `build/game-validation-final`. The game's existing RTSSHooks64.dll remained
loaded. Validation overhead affects frame rates, so this run is not a benchmark.

The original validator 1.4.309 crashed while validating DXVK's newer graphics
features before LSFG swapchain initialization; this is not an LSFG pass or fail.
The newer official 1.4.363 SDK was extracted locally without running its installer.
Use the `khronos_validation` settings-file namespace; the JSON `vvl` prefix is
not the settings-file namespace. The corrected run explicitly reported core,
synchronization, parameter, object lifetime and thread safety checks enabled.

Final regression runs put DVLSFG above validation in the layer chain so injected
commands are checked too. Synchronization validation exposed a missing explicit
semaphore dependency before presenting the real image; this was fixed. The host
also now waits at all commands before its acquired-image layout transition.
Final capture, 4x performance, disabled, toggle, recreation, combined metadata
and secondary-device cases passed with synchronization and thread checks enabled.

RTSS's separate DLL-injected overlay produced a present-after-write report even
in the host with DVLSFG entirely removed. API dump identified the additional
render pass and semaphore-free submission. A test-executable-only RTSS opt-out
isolates the final host checks; neither the game nor mod uses this opt-out.
Implicit overlay compatibility tests do not establish that all external hooks
are free of Vulkan synchronization issues.

## Limits

The original LSFG baseline did not enter a driving session or saved gameplay.
Later FSR/scene-UI sections document their Continue-save checks. Dynamic artifacts,
HUD ghosting, pacing on different monitors, end-to-end latency and compatibility
with other GPUs/mods remain unverified. Non-VR SDR only. The conservative backend
waits for device completion before reusing images; asynchronous optimization is
future work. Test-process layer variables do not modify global Vulkan settings.
The release archive excludes model DLLs, extracted shaders, logs, captures and
personal configuration.

## FSR 3.1 frame generation 0.6.0 (2026-10-15)

Official FidelityFX-SDK v1.1.3 optical-flow and frame-interpolation Vulkan
components are integrated, independently of LSFG and the selected upscaler.
`FsrFrameGenerationGpuTests` renders a moving bar and checks three generated
centroids at the exact midpoint of previous/current native positions. With no
Lossless.dll, no debug-utils extension and no promoted KHR extension names,
it passed (3072 changed pixels; centroids 83.5/107.5/131.5 versus native
95.5/119.5/143.5). Vulkan validation 1.4.363 reported no warnings or errors after
fixing duplicate image registration and the SDK's missing storage-buffer pool
entry. Original LSFG 4x present-metadata regression also passed: 12 native /
36 generated frames, zero validation messages.

The standalone game fixture passed with an empty Lossless.dll path, native
resolution and FSR1/FSR3 upscaling. Every path demonstrated changed moving-scene
pixels, finite nonzero engine motion vectors and nonconstant raw reversed depth.
At each upscaler setting, submitted generated/native counters stayed 1:1.
Opaque and semitransparent UI matched the native frame, with identical 596 MSAA
edge pixels and maximum opaque-edge RGB error zero. It passed 1280x720 resize,
UMM IMGUI pause and camera-space UI bypass. GPU readback in the fixture causes
long CPU frames, so interpolation proof waits for the intended history reset to
settle; production does not perform these readbacks.
The saved driving-world fixture passed five FSR3 upscaling quality levels,
Forward and DeferredShading, 1280x720 resize, and FSR3 -> FSR1 -> FSR3 transitions.
At quality checks, temporal input sizes matched scene targets, output/UI stayed
at display resolution, scene AA was replaced only by the upscaler, and screen
picking rays matched viewport rays. Native/generated totals remained 1:1
(e.g. 147/147, 1129/1129, 1458/1458, 2356/2356 and 2930/2930). Existing unrelated
mods remained enabled. The test now filters for cameras still owning their
scene targets, excluding stale components during camera/quality transitions.
User save data, preferences and mod configuration were backed up and restored.
Tests do not establish visual quality in every weather/transparent-effect
scenario or support across other GPUs; this run used the RTX 3070 Ti.
Native Graphics UI checks passed the Off/LSFG 3.1/FSR 3.1 selector, fixed FSR
2x display, immediate disabling of LSFG-only controls, AA preservation with
upscaling disabled, Discard restoration, localization, blank-area scrolling,
Apply/Discard persistence and original LSFG live model/flow switching. The
launcher fixture also confirmed FSR starts with an empty Lossless.dll path;
LSFG probing and existing recovery/conflict checks remained valid.
## Upscaling technology names and bounded value marquee

Inspected the native `DV.UI.Selector` implementation and live TMP settings. The
native value uses fixed 22-point text with Ellipsis, without scrolling. Its
preferred-width layout compressed the Chinese title to "超分辨..." when the new
FidelityFX name required 291 units in a 240-unit value area. The custom upscaling
selector now reserves the complete localized title and keeps native arrow padding.
Long values use two copies inside a RectMask2D viewport, with an initial pause
and continuous leftward motion; short values retain native right alignment.

The game fixture verified the exact FidelityFX™ Super Resolution / AMD FSR 3.1
names, complete title, bounded scrolling, 18.22-unit title gap, 8-unit arrow gap
and a 200.02-unit value viewport in the tested layout. Movement and short-name
reset assertions passed. Screenshots confirmed that text stays inside the value
area and the title/arrows remain clear. Test choices were not applied.
# External OSD and selector rendering (2026-10-15)

The scene/UI bridge now also exports Unity's complete frame before external
present hooks. The compositor compares it with a saved application swapchain
image and preserves late overlays on both real and generated frames, without
feeding them into optical flow. Temporary UI rendering restores CanvasRenderer
culling flags after returning canvases and layers to their original state.

The actual-game FSR frame-generation fixture passed FSR upscaling off/1/3,
opaque/translucent UI pixel equality, resolved UI edges, moving-scene differences,
depth/motion inputs, resize, UMM bypass and camera-space UI bypass. A captured
generated frame visibly includes the user's RTSS OSD. The 600x90 OSD region
matches its real-frame counterpart with maximum RGB byte error 0.

`GraphicsArrows` opens the game's actual settings, temporarily tests their native
selectors as overlay UI over a synthetic FSR scene, checks generation advances,
and checks six native arrow icons (114–126 bright pixels each in the tested capture). Test screenshots and logs are
archived locally under `build/overlay-arrow-validation`; temporary fixture mods
are removed after testing. User configuration and game preferences are restored.

## Real-world interpolation regression (2026-10-15)

The previous external-OSD fix incorrectly flipped the complete screenshot like
the camera/UI render textures. Those captures have different origins. A real
world camera pan exposed 563,275 scene pixels (19.890% of the checked region)
being misclassified as external overlays and copied back from the real frame.
The synthetic scene had not exposed this failure across its flat background.

The screenshot is now sampled in WSI coordinates without a second Y flip or
color decode. A same-snapshot four-way origin/color diagnostic identified this
interpretation with zero scene false positives. The final real-world capture
at 30 FPS source passed both independent checks:

- 2,832,000 checked scene pixels; zero wrongly preserved pixels; 480,462 final
  generated-versus-real scene differences above the RGB threshold.
- 10,256 visible OSD pixels in the 600x90 test region; maximum RGB byte error 0.

Capture requests now also save an overlay preservation mask for regression
checks. The world fixture backs up user state and briefly rotates the camera;
its quality-mode, rendering-path and resize runs also reported submitted rates
above 200 FPS (including 209.209 and 218.722), with no 165 FPS output ceiling.

The game's MainMenu.Start separately replaces non-VSync preferences, including
Unlimited (999), with the monitor refresh rate. The mod reapplies the selected
preference, including when the gameplay FrameLimiter has not been instantiated.
Menu and world logs both confirmed targetFrameRate=0 and vSyncCount=0 for
Unlimited on the 165 Hz monitor.

A destroyed native settings slider retained in a provider callback could abort
ApplyChanges. Its callback is now safely skipped; a destroyed-clone regression
passed. Inactive startup also clears old native counters/logs so evidence from
a previous enabled run cannot be mistaken for the current process.

## In-game arrows and 2x presentation cadence (2026-10-15)

Reproduced the missing arrows by applying FSR FG in the actual in-world pause
menu. Its icon meshes extend several units in front of their buttons: the UI
capture canvas plane at distance 1 clipped them behind the camera's near plane.
Moving the plane to 100, with a 1000-unit far plane, preserves those meshes.
Actual in-game graphics and gameplay selectors, including preset arrows, were
visually checked with FSR FG active. Opening a settings controller synchronizes
its pending mod settings; Apply ignores inactive, stale menu instances.

The synchronous presentation path delayed the next Unity render while spacing
the generated and real presents. Its uncapped alternating interval medians were
3.986 / 6.555 ms despite genuinely intermediate scene motion. For 2x on devices
with a spare queue in the same graphics family, the real present is now paced on
that reserved queue while Unity starts rendering the next input. The worker
retains input imports, semaphores and a deep copy of present metadata. It drains
before resource reuse, disabling, resize, queue/device idle, and application
fence/ID waits. Finite and zero timeouts remain supported. The normal 2x path no
longer performs a device-wide idle wait every frame. Higher LSFG multipliers and
devices without a spare graphics queue retain the synchronous path.

Final 1920x1080 real-world FSR 3.1 Balanced captures:

- Camera-motion interpolation position medians: 0.502 at a 30 FPS source limit
  and 0.501 uncapped (previous=0, exact midpoint=0.5, current=1).
- Uncapped generated-to-real / real-to-generated request interval medians:
  5.273 / 5.238 ms. Respective p10/p90 intervals: 5.100/5.468 and 4.327/6.313 ms.
  The first capped capture still contains loading outliers; it is not evidence
  that loading stutters have been eliminated.
- 1,444,800 checked scene pixels, zero false overlay-preservation pixels;
  88,052 generated-versus-current scene differences. 11,059 visible RTSS OSD
  pixels retained with maximum RGB byte error 0.

`Analyze-FrameInterpolation.py --check` checks scene displacement and alternating
interval medians, independently of the generation counters. These timings are
CPU present-request timestamps, not measured physical display intervals.
PresentMon's existing API could not open a service session (status 14); physical
display cadence and perceived smoothness are therefore not independently proven.

With the same stationary cab view and Balanced SR, repeated 8-second source
benchmarks measured FG off/on/off/on at 149.26/101.03/184.98/103.49 FPS. Loading
and scene warmup materially affect this comparison. A fixed 147 FPS ceiling was
not reproduced; this does not establish performance parity with an older build.
Scene/UI capture is also disabled when the selected generation backend differs
from the backend loaded at startup, avoiding capture work while restart is due.

The real-world fixture passed all five SR qualities, Forward/Deferred rendering,
resolution change and FSR1/FSR3 transitions. Vulkan synchronization and threading
validation passed present metadata, immediate fence polling and zero-timeout
waits, present-ID waits, resize during present, and live 2x/3x/4x transitions.
An initial asynchronous fence race was caught and fixed before the final build.
Temporary fixtures and added autosaves are archived under the local ignored
`build/framegen-cadence-validation` directory; original save/preferences files
are restored and hash-checked after testing.

## LSFG / FSR FG comparison using displayed events (2026-10-15)

The earlier present-request timing checks did not establish display cadence.
This follow-up uses the already installed PresentMon 1.10 CLI, tracking actual
display events for the game PID in separate eight-second captures. Each sample
discards its first and last second. PresentMon 2.3 long recordings lost ETW
events and were excluded from the final comparison. The API loader's status 14
means `PM_STATUS_MIDDLEWARE_MISSING_PATH`; it did not prove that the installed
PresentMon service was unavailable.

Both backends used the same game save, controlled camera pan, Balanced FSR 3.1
SR, LSFG standard model and 75% LSFG flow quality. The fixture sets the game's
actual `GamePreferences.Set<int>(Preferences.FrameLimit, ...)` and invokes its
`FrameLimiter.FrameLimitUpdated` entry, rather than only modifying Unity's
target directly. Native QPC/UTC acknowledgments record the applied target and
VSync state. All sampled fixed limits had `vSyncCount=0`; Unlimited applied
`targetFrameRate=0`.

Measured **displayed FPS**, not generated-frame counters:

| Game source limit | LSFG 2x | FSR FG 2x | LSFG 4x |
| --- | ---: | ---: | ---: |
| 30 | 59.77 | 59.87 | 119.54 |
| 60 | 117.48 | 117.30 | 148.83 |
| 90 | 141.66 | 142.55 | 146.01 |
| Unlimited | 154.09 | 154.16 | 142.97 |

At a 60 FPS source limit, LSFG 2x and FSR FG 2x display-gap p10/median/p90 were
6.042/6.073/12.140 ms and 6.043/6.071/12.139 ms respectively. Thus this sampled
scene has essentially matching 2x display cadence; this does not prove identical
motion reconstruction or absence of interpolation artifacts.

The player's saved LSFG configuration before this comparison was 4x; FSR FG is
fixed at 2x. At a 30 FPS source limit those configurations actually displayed
approximately 120 versus 60 FPS. They are materially different comparisons.

RTSS's mapped per-application counter continued reporting the source rate. For
FSR FG it reported 30.04 with 59.87 actually displayed, and 59.06 with 117.30
actually displayed. It therefore cannot be treated as the final generated
output counter on this setup, despite labeling the API Vulkan. The game source
limit does influence the source rate and hence its n-times target; it did not
limit these final outputs to the same numeric value as the source setting.

Every sampled event used `Composed: Copy with GPU GDI`, with tearing unavailable
in this windowed display path. Above the 165 Hz display cadence, presents were
dropped: FSR FG Unlimited requested 222.35 FPS but displayed 154.16, while LSFG
4x Unlimited requested 359.26 but displayed 142.97. More submitted frames do not
guarantee more displayed frames or smoother motion in this compositor path.

`Capture-BackendComparison.ps1` and `Analyze-BackendComparison.py` reproduce the
limit/multiplier and independent display-event checks using the world fixture's
optional live-comparison mode. Raw traces, RTSS samples and configuration
backups are archived locally in ignored `build/backend-comparison`. This
investigation changes test tooling and records; it does not claim a new backend
fix based on matching display counts.

## GPU-ready presentation pacing (2026-10-15)

Repeated the actual Display settings slider / Apply Changes path, without the
comparison fixture, at the player's 2560x1440 resolution with FSR 3.1 Balanced
SR and FSR FG 2x. Restarted after selecting the FG backend, then selected and
applied the native 60 FPS limit and closed the settings menu. PresentMon 1.10
eight-second captures discard their first and last second.

| FSR FG configuration | Requests/s | Displayed/s | Dropped |
| --- | ---: | ---: | ---: |
| Before, native limit 60 | 117.88 | 101.30 | 14.06% |
| GPU-ready pacing, native limit 60 | 118.96 | 118.96 | 0% |
| GPU-ready pacing, native Unlimited | 187.54 | 164.72 | 12.16% |

The original generated-frame present was queued while interpolation still had
about 4.5 ms of GPU work pending. The real-frame worker counted its half-frame
interval from that CPU submission, allowing the two ready images to bunch up
and the window compositor to discard generated frames. The asynchronous 2x
path now waits for the generated image's queue work with a fence before
starting its presentation interval. This waits for that queue's submitted work,
not a device-wide idle, and preserves the next game render / real-present overlap.
Both 2x backends share this readiness rule; higher multipliers are unchanged.

Unlimited displayed gap p10/median/p90 was 6.043/6.061/6.078 ms on the 165 Hz
display. Present mode remained `Composed: Copy with GPU GDI`, with no tearing;
requests beyond the physical refresh cadence can still be dropped. RTSS still
reported the source rate (about 59 FPS at the 60 FPS game limit).

The exact reported hard ceiling of 60 final FPS was not reproduced in this run;
the measured and corrected defect was loss of generated frames before display.
These static-view event counts do not establish motion-reconstruction quality
or resolve every reported subjective smoothness difference. Also, changing FG
technology requires restarting: applying a different backend disables the old
one until the selected backend loads on restart.

Vulkan 1.4.363 synchronization/thread validation passed 36 real + 36 generated
frames with combined present metadata, deferred application fence waits and
present-ID waits. The older 1.4.309 validator reported WRITE_AFTER_PRESENT;
the repeated run with 1.4.363 reported zero messages. Present metadata unit
checks also passed. Traces and fresh user-data backups are kept locally in
ignored `build/ui-framelimit-regression`.

## Settings Apply / Revert destroyed callbacks (2026-10-15)

The player's log showed repeated `NullReferenceException` failures in
`SettingChangeSourceCheckbox.OnResetOrApplied`, reached from
`ASettingsProvider.ApplyChanges`. The provider retained a callback to a destroyed
Unity component. That exception interrupted the notification chain and the mod's
Apply postfix, leaving pending settings and the unsaved-changes prompt stuck.
The existing destroyed-slider protection did not cover checkboxes or selectors.

Apply/Revert notification methods on concrete native `SettingChangeSource`
types now skip destroyed component instances. Live controls still run their
normal callbacks; the slider's separate uninitialized-cache protection remains.
Open generic types are excluded from Harmony targets.

`Install-GameFixture.ps1 -Kind SettingsApply` installs the focused in-game
regression. It activates clones of native checkbox, selector and slider
bindings so they subscribe to the real provider, destroys them, and invokes
their stale callbacks. It then changes mod sharpening through the real settings
panel, invokes the native Apply button, and verifies persistence, mod Dirty=false
and native HasChanges=false. Native Revert and restoration of the original
sharpening value also passed. No Computer Use was needed. The temporary fixture
and logs are archived under ignored `build/settings-apply-regression`; original
mod configuration and game preferences are restored after the run.

## Autosave thumbnail render-target restoration (2026-10-15)

Reproduced white native autosave thumbnails with FSR FG enabled. The game's
`SaveGameManager.GetThumbnail` calls `CaptureScreenshot(true)` when there is no
stashed image; this captures the current render surface. The separate pause-menu
stash path manually renders the active scene camera and was unaffected.

At the end of the frame, scene/UI separation renders UI over black and white
backgrounds. Its final `Graphics.Blit` left `RenderTexture.active` bound to
`DV Scaling UI over white`. That target leaked into the next frame and native
autosave screenshot capture. The baseline automatic thumbnail had mean pixel
intensity 255.00 and deviation 0.22, while the stashed thumbnail contained scene
detail. Disabling frame generation avoided the white capture.

`SceneUiCapture` now restores the render target that was active before its
end-of-frame work, including bypass and exception paths. This shared capture
code is used by both frame generation backends.

The world fixture's optional `thumbnail-only.txt` mode invokes the game's real
automatic and stashed thumbnail paths and writes PNGs plus pixel statistics.
For FSR FG, all 12 combinations of upscaling off / FSR1 / FSR3, frame generation
off / on, and automatic / stashed capture passed. Generated counters continued
advancing in enabled cases. With FSR3 SR and FG on, automatic capture returned
to the screen surface, with pixel deviation 28.20, and visual inspection showed
the game scene. Evidence and original user-data backups are archived locally in
ignored `build/save-thumbnail-regression`.

The same 12-case matrix passed with LSFG 3.1 (24 corrected captures across both
backends). Its FSR3 SR + FG automatic thumbnail had pixel deviation 28.86 and
the active target was the screen. An initial LSFG world-load attempt timed out;
the repeated run from the original backed-up save completed. Tests restore and
hash-check the original user files and mod configuration, archive added test
saves, and remove the temporary fixture from the game's Mods directory.

## Native right-button zoom measurements (2026-10-15)

Measured the existing FSR FG 3.1 + FSR 3.1 Balanced configuration at 1920x1080,
with the game's selected 90 FPS source limit and a 165 Hz display. The optional
`zoom-live.txt` world-fixture mode uses a temporary Harmony substitution limited
to the current Rewired player's Zoom action. The game's native `CameraZoom`
still executes its normal press/release and SmoothDamp code. Repeated zoom
cycles hold/release every 1.2 seconds; native FOV ranges from 50 to 20 degrees.
No production camera or reset code was changed.

Each stage warms up for three seconds, then records eight seconds with the
independent PresentMon 1.10 CLI. Analysis trims the first and last second and
correlates the display events with per-frame QPC, Unity frame time, FOV, and
history-reset counters from the fixture. PNG/BMP readbacks occur only after
performance sampling, since capture itself stalls rendering.

| Configuration | Source FPS | Source p99 ms | Displayed FPS | SR reset frames / sampled 6 s |
| --- | ---: | ---: | ---: | ---: |
| FG + SR, normal view | 87.80 | 12.850 | 161.30 | 0 |
| FG + SR, held zoom | 88.37 | 13.064 | 159.28 | 0 |
| FG + SR, cycling zoom | 87.15 | 12.949 | 159.19 | 169 |
| Same cycles, suppress FOV reset in test only | 86.96 | 13.441 | 160.36 | 0 |
| SR, FG off, cycling zoom | 88.84 | 12.089 | 88.91 | 173 |
| SR and FG off, cycling zoom | 88.72 | 12.111 | 88.74 | 0 |
| FG, SR off, cycling zoom | 87.03 | 13.470 | 157.26 | 0 (FG FOV changes: 168) |
| Repeat FG + SR cycles | 84.37 | 14.216 | 158.91 | 166 |
| Repeat cycles, suppress FOV reset | 87.79 | 12.981 | 161.10 | 0 |
| Repeat normal view | 88.06 | 12.891 | 161.78 | 0 |

No sampled source frame exceeded 20 ms; the largest was 17.244 ms. Thus this
scene shows a small source-rate variation, not a reproduced large rendering
stall. Suppressing resets did not consistently improve source FPS across both
runs, so reset-related GPU cost is not established as a major FPS-drop cause.

There is, however, a confirmed interpolation discontinuity. Both managed FSR
paths reset history when adjacent FOV values differ by more than 0.1 degrees.
AMD's frame-interpolation setup resets the frame-index counter, and its shader
explicitly copies `LoadCurrentBackbuffer` rather than interpolating when that
counter is zero (`ffx_frameinterpolation_setup.h` and
`ffx_frameinterpolation.h`). During the zoom transitions, generated presents
therefore need not contain new intermediate scene positions.

A captured original-reset zoom frame confirms this: previous/current scene
RGB mean absolute difference was 4.2179, but generated/current-real difference
was only 0.0044 (central 70% crop, 8-bit RGB; ratio 0.001). The test-only reset
suppression produced different intermediate images; a moving sample had scene
difference 8.2061 and generated/current difference 6.1391. This establishes
non-duplication for that sample, not image-quality correctness or suitability
of permanently removing FOV resets. More than one cause can contribute in
other scenes, especially with a different GPU load or camera position.

`ZoomWorldProbe.cs`, `Capture-ZoomComparison.ps1`, and
`Analyze-ZoomComparison.py` reproduce these measurements. Raw traces, per-frame
logs, comparison images, and original user-data backups are kept locally under
ignored `build/zoom-regression`. Temporary input/reset patches are removed and
original save/preferences/configuration are restored after testing.

## Preserve temporal history during smooth zoom (2026-10-15)

Replaced the fixed 0.1-degree FOV reset in FSR SR and FG with a shared optical
magnification continuity test. It compares the absolute log ratio of
`tan(previousFov/2)` and `tan(currentFov/2)` against `0.2 + 2*min(dt, 0.1)`.
The allowance tolerates native smooth zoom at lower frame rates while retaining
a reset for abrupt projection jumps. Invalid/uninitialized FOVs reset history.
Existing first-frame/resource, teleport, rotation, render-path and long-frame
reset conditions are unchanged. The per-frame test allocates no objects.

`TemporalHistoryTests` replays the previously measured native CameraZoom trace
at 30/60/90/144 FPS. All smooth trajectories had zero projection resets;
instant 50-to-20 / 20-to-50 degree cuts and invalid inputs retained protection.

The updated game probe records actual explicit FG reset flags as well as SR
resets. With its reset-suppression option disabled, measured results were:

| Stage | Changing FOV frames | Explicit SR / FG resets | Source FPS | Displayed FPS |
| --- | ---: | ---: | ---: | ---: |
| Normal view, SR + FG | 0 | 0 / 0 | 83.04 | 147.02 |
| Native smooth zoom, 90 FPS target | 158 | 0 / 0 | 79.86 | 143.98 |
| Native smooth zoom, 30 FPS target | 71 | 0 / 0 | 29.86 | 59.77 |
| Smooth zoom, FG without SR | 126 | 0 / 0 | 62.66 | 124.52 |
| Forced abrupt FOV cuts | 5 | 5 / 5 | 81.71 | 144.08 |

Before this change, smooth-zoom samples triggered about 169 explicit resets per
six seconds. This improvement concerns interpolation continuity, not a promise
of higher source FPS. Generating real intermediate frames also executes passes
the SDK skips on reset; performance depends on GPU headroom. Cross-run FPS
numbers are not a controlled performance improvement comparison.

After performance sampling, three captures were requested specifically while
FOV was changing, at a 30 FPS source target. Scene-motion registration measured
median interpolation positions **0.503, 0.495 and 0.496** (previous=0,
midpoint=0.5, current=1). Generated frames no longer duplicated the current
source. Comparison sheets showed coherent intermediate zoom positions without
obvious double edges in the sampled view; these stills do not prove freedom
from artifacts in all scenes, transparency, or disocclusions.

Evidence, per-frame logs, independent PresentMon traces and original user-data
backups are kept locally in ignored `build/zoom-history-optimization`. The
temporary fixture is archived and original user files/configuration restored
after validation. Only the tested reset policy and FG diagnostic flag ship.

## Fixed-view frame generation cost attribution (2026-10-15)

Added an opt-in `throughput-live.txt` world fixture and independent PresentMon
collection/analysis tools. The probe updates the game's actual FrameLimit
preference and invokes FrameLimiter, rather than relying on an application
target that the native limiter can overwrite. Source samples use Windows QPC,
matching PresentMon; Mono Stopwatch's process-relative clock is unsuitable for
that correlation. CPU durations still use Stopwatch.

RTX 3070 Ti, 1920x1080, FSR 3.1 SR balanced, FSR FG 2x, fixed camera, vSync=0:

| Stage | Source FPS | Submitted FPS | Displayed FPS | UI CPU ms/source |
| --- | ---: | ---: | ---: | ---: |
| FG off, settled | 134.67 | 134.64 | 133.46 | 0 |
| FG on, settled | 84.66 | 169.31 | 157.07 | 2.028 |
| FG off, repeat | 127.33 | 127.36 | 126.19 | 0 |
| FG on, repeat | 84.55 | 169.11 | 157.38 | 2.016 |
| FG on, reuse existing UI capture (diagnostic only) | 109.03 | 216.88 | 163.96 | 0.015 |
| FG on, native 60 FPS source limit | 58.91 | 117.83 | 117.49 | 2.106 |

The frozen-UI ablation skips RenderUi only: current scene, depth/motion, frame
interpolation, screenshot reference, interop, composition and presentation keep
running. It is not a shipping optimization and would retain stale UI during
real interaction. Two live root canvases and 75 traversed transforms were
recorded in the stable view; this did not establish a large hidden-menu scan.
Managed capture CPU submission took about 0.003 ms, which does not measure its
GPU cost. RTSS reported 78.82 source FPS in an uncapped FG interval with
166.55 submitted / 160.35 displayed FPS: its VULKAN label does not establish
that it counts generated presents in this configuration.

Optional GPU timestamps measure FSR prepare, optical flow and interpolation.
Create `profile.request` beside `DVLSFG.FSRFG.dll` before launching; averages
over 120 frames appear in `native.log`. Remove the marker and restart to turn
it off. Timestamp reads do not wait for the GPU, and normal runs allocate no
query pool. These timings exclude Unity rendering, UI capture, compositing,
image copies and presentation. Observed GPU times were approximately 0.033 ms
prepare, 0.60–0.84 ms optical flow and 0.49–0.63 ms interpolation; typical
combined cost was about 1.2–1.4 ms. Native present CPU duration was about
3.9 ms, including GPU-readiness waits; it overlaps these GPU timings and must
not be added to them as a separate GPU cost.

This reproduces a substantial source-FPS penalty from the integration, while
actual displayed throughput improves in this scene. It does not reproduce
the user's exact 180–200 versus 100–110 scene or prove a fix for all overhead.
No pacing waits or UI correctness safeguards were removed. Raw evidence and
fresh user backups are retained locally under ignored `build/fg-throughput`.

## Reuse unchanged screen UI captures (2026-10-16)

The player's high-throughput view was also measured directly, without
restarting or intentionally moving the camera: FG off submitted about
182–184 FPS (RTSS sampled 193); FSR FG submitted 176–178 FPS from 88–89 source
FPS. Actual displayed rates were about 161–162 versus 159–161 FPS. This
confirmed a substantial integration penalty beyond the counter-label issue.

Screen UI capture now tracks Graphic dirtiness and compares geometry,
renderer tint/culling/opacity, Canvas ordering/scaling, material CRCs and
texture contents/sampling state. Unchanged black/white UI surfaces are reused;
scene, depth/motion, full-frame overlay reference and frame generation still
update every source frame. Subscriptions and material/texture state are
released when captures are cleared. The capture camera uses forward rendering
without scene occlusion culling and visits active children.

Reuse is limited to built-in Image/RawImage/Text and TMP UI components with
explicitly recognized UI shaders. RenderTextures, custom/procedural Graphics,
unknown shaders, scrolling TMP materials and masked/clipped UI retain per-frame
capture. Removing clipping or activating a Graphic invalidates the cached
surfaces. Existing MSAA surfaces, scene-only interpolation and presentation
synchronization are retained.

The opt-in throughput probe can force the previous per-frame UI path with
`legacy` commands. In a single process at 1080p with FSR 3.1 SR balanced and
FSR FG 2x, the repeated `final2-comparison` measurements were:

| UI path | Source FPS | Submitted FPS | Displayed FPS | UI CPU ms/source |
| --- | ---: | ---: | ---: | ---: |
| Previous capture | 91.60 | 183.09 | 163.96 | 1.980 |
| Unchanged UI reuse | 132.10 | 264.02 | 164.91 | 0.137 |
| Previous capture, repeat | 100.77 | 201.36 | 164.71 | 1.969 |
| Unchanged UI reuse, repeat | 131.56 | 262.99 | 165.06 | 0.136 |
| Reuse, native 60 FPS source limit | 59.44 | 119.01 | 119.01 | 0.140 |

Source throughput improved about 31–44% in these paired samples and UI CPU
cost fell about 93%. The source p99 changed from 11.48–13.07 ms to 8.65–8.70 ms.
The final display remains constrained by the 165 Hz monitor/compositor; the
263–264 submitted frames are not 263–264 physically displayed frames. These
are measured scene results, not a guaranteed percentage for every view or UI.

A final shipping-code run (`shipping-comparison`) measured 137.15–141.49
source FPS and 273.79–280.57 submitted FPS with reuse, versus 95.13 source
FPS / 190.32 submitted FPS in its stable previous-path sample. UI CPU time
was 0.130–0.133 ms versus 1.921 ms. Its first previous-path sample overlapped
loading/UI initialization (changing Canvas counts and large frame spikes)
and is retained in the raw evidence but excluded from this comparison.
The 60 FPS source-limit check produced 118.99 submitted/displayed FPS.

`UiCaptureCacheSmoke` covers stable reuse, text edits, geometry/color/tint,
CanvasGroup opacity, nested Canvas sorting, in-place material changes, texture
uploads/filtering/addressing, RenderTexture and clipping fallback, activation,
removal and callback cleanup. A world run also passed twelve automatic/stashed
thumbnail checks across SR off/FSR1/FSR3 and FG off/on; no blank image was found.
The shipping build also passed the LSFG 2x/3x/4x scene/UI pixel matrix with
SR off/FSR1/FSR3: changed opaque/translucent colors, live CanvasGroup fades,
MSAA edge coverage, capture resize, UMM pause and camera-space UI bypass.
Presented UI edge differences between real and generated captures were at
most one 8-bit RGB step; the actual UI textures updated on color/fade changes.
Raw traces, probe logs, regression images and original user backups are kept
locally under ignored `build/fg-performance-optimization`.

## Preserve SR history through native menu snapshots (2026-10-16)

Opening the native pause menu stashes a save screenshot. The game's
`ScreenshotTaker.TakeScreenshotWithoutUI` temporarily assigns a snapshot
texture to the active scene camera, renders it and then assigns null instead
of restoring the previous target. With FSR enabled this loses the owned SR
target. The following FSR 3.1 LateUpdate releases its context and recreates
the targets, unnecessarily discarding accumulated scene detail.

The pre-fix world trace recorded `target=none` at Release from LateUpdate,
with the camera still enabled, active and using the same culling mask. The
output texture identity changed on menu opening. This also reproduced with
frame generation off, locating the resource loss before frame generation.

A scoped screenshot patch now saves an FSR-owned scene target and restores
it after the native screenshot returns (including exception cleanup). It
applies to FSR 1 and FSR 3, preserves the native snapshot rendering, and does
not disable genuine camera-cut history resets or steal targets from other
renderers.

In the first fixed world run at 2560x1440, native snapshot target identity
was preserved. Across two pause/unpause cycles with FG off and two with FSR
FG on, the same FSR output remained allocated and no context creation or
release occurred. All 340 sampled paused frames had zero SR history resets.
Twelve consecutive 256x256 scene-output crops per segment showed mean
adjacent-frame RGB changes of about 0.079–0.082 out of 255 in both paused and
running segments. This measures the sampled scene region, not every pixel
or every camera configuration.

The opt-in `pause-test.txt` world fixture records resets, target identity and
release call stacks; it asserts native screenshot target restoration and
unchanged SR targets/context across menu transitions. Evidence and fresh
user-data backups are retained under ignored `build/pause-history`.
The final run passed all target/context assertions over 857 sampled frames
(355 paused) with zero SR resets, creations or releases. Twelve native
automatic/stashed thumbnail checks across SR off/FSR1/FSR3 and FG off/on also
passed the nonblank-pixel and unchanged-camera-target checks.

## 1440p frame generation throughput diagnosis (2026-10-16)

The reported 180–200 versus approximately 100 FPS was reproduced at 2560x1440
with FSR 3.1 SR balanced. In a fixed-view, single-process comparison, FG off
measured 188.04–189.06 source/submitted FPS; FSR FG 2x measured
106.32–106.57 source FPS and 212.49–213.16 submitted FPS. Displayed throughput
was approximately 164–165 FPS in both cases on the 165 Hz desktop. The source
penalty is real even though the final FG submission rate exceeds the original
game's submission rate.

The UI cache remained effective (zero redraws in these samples, about
0.116–0.118 ms CPU). Skipping its checks yielded 106.72 source FPS, and changing
Unity's maximum queued frames from 1 to 3 yielded 102.36–105.80 FPS, neither
explaining the penalty. Optional GPU timestamps measured roughly 1.9–2.0 ms
for FSR prepare/optical flow/interpolation, and 0.32 ms for the three UI
compositor dispatches. These GPU intervals overlap the roughly 3.8 ms native
present CPU interval and must not be added to it as independent costs.

Raw evidence, candidate comparisons and fresh user backups are retained under
ignored `build/fg-performance-1440`.

The shipping 2x path submits all scene/interpolation/UI work on the graphics
queue, observes generated-image readiness on a worker using a device event,
and presents in order on the reserved queue. It permits at most two source
frames in flight and rotates command/semaphore resources. The swapchain
reserves additional images for the application's originals and generated
frames. Acquire and present access to a swapchain share a host mutex; acquire
uses nonblocking attempts so it cannot hold that mutex while a pending present
needs to free an image. Per-fence/ID completion retires only the required jobs.
Unsupported paths retain the synchronous fallback.

Uncapped pacing includes 2% headroom. With exactly half the observed source
period between both output frames, pacing can otherwise treat its own waits
as rendering cost and preserve a low rate after a slow frame. Explicit game
source limits retain the exact multiplier interval. The opt-in probe exposes
`serial` and `exact` controls to reproduce the previous wait and feedback
behavior without changing rendering quality or restarting the process.

Final alternating measurements (`final-comparison`) at the same 1440p view:

| Path | Source FPS | Submitted FPS | Displayed FPS | Source p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Previous waits | 108.21 | 216.45 | 164.98 | 10.099 |
| Bounded pipeline | 119.95 | 240.33 | 164.79 | 9.540 |
| Previous waits, repeat | 107.08 | 214.23 | 164.86 | 10.358 |
| Bounded pipeline, repeat | 119.41 | 238.84 | 165.04 | 8.950 |

The improvement was about 11–12%. An earlier shipping-path check measured
120.34–120.78 source / 240.81–241.57 submitted FPS, and a 60 FPS source limit
produced 59.50 source / 118.89 submitted and displayed FPS. Native present
CPU duration fell from approximately 3.8 ms to 0.6–0.8 ms; some waiting now
occurs outside that interval, so this is not a 3 ms reduction in total frame
time. GPU utilization sampled around 97% after the change. FSR computation,
scene rendering, copies and composition still consume GPU time; the change
does not preserve the FG-off source rate at zero cost. Submitted rates above
165 are not physically displayed rates on the 165 Hz desktop.

Final Vulkan synchronization/thread validation passed a 48-source-frame test
with two application frames in flight. Present fence/ID handling, resize
recovery and live 2x/3x/4x model/flow settings also passed. The FSR FG pixel
fixture passed moving-scene interpolation (distinct generated pixels with
nonzero engine motion vectors/depth), SR off/FSR1/FSR3, opaque/translucent UI,
UI edge AA, resize, UMM pause and camera-space UI bypass. These pixel checks
ran with profiling disabled. No FG shader or reconstruction quality change
is part of the shipping optimization.

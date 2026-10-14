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

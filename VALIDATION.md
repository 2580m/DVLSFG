# DVLSFG validation record

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

No driving session or saved gameplay was entered. Dynamic scene artifacts,
HUD ghosting, pacing on different monitors, end-to-end latency and compatibility
with other GPUs/mods remain unverified. Non-VR SDR only. The conservative backend
waits for device completion before reusing images; asynchronous optimization is
future work. Test-process layer variables do not modify global Vulkan settings.
The release archive excludes model DLLs, extracted shaders, logs, captures and
personal configuration.

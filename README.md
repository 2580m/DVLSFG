<div align="center">

# DV Scaling

**让 Derail Valley 的原生图形设置拥有帧生成与超分辨率。**

<a href="package/Info.json"><img src="https://img.shields.io/badge/version-0.6.1-6366f1?style=flat-square" alt="Version 0.6.1"></a>
<img src="https://img.shields.io/badge/status-Beta-f59e0b?style=flat-square" alt="Beta">
<img src="https://img.shields.io/badge/platform-Windows_x64-0078d4?style=flat-square" alt="Windows x64">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=flat-square" alt="MIT license"></a>

[中文使用指南](package/README.zh-CN.md) · [English Guide](package/README.en.md) · [验证记录](VALIDATION.md) · [源码与许可](UPSTREAM.md)

</div>

---

## ✨ 功能

DV Scaling 是独立的 Unity Mod Manager 模组。所有设置集成在游戏的**设置 → 图形质量**中，沿用原生的应用、放弃、悬浮简介和滚动布局。

| 功能 | 支持内容 |
| --- | --- |
| **LSFG 3.1 帧生成** | 2× / 3× / 4× 倍率，标准与性能模型，光流质量调整 |
| **AMD FSR FG 3.1 帧生成** | 固定 2×，游戏深度与运动矢量配合 AMD 光流；无需 Lossless.dll |
| **FSR 1.0 超分辨率** | 空间重建，独立质量档位与 0%–100% 锐化 |
| **FSR 3.1 超分辨率** | 深度、运动矢量、投影抖动与历史帧驱动的时域重建 |
| **独立 UI 合成** | 屏幕叠加 UI 不参与帧生成，原生帧与生成帧保留独立 UI MSAA |
| **实时调整** | 已加载的 LSFG 支持实时切换倍率、模型和光流质量；超分辨率应用后生效 |
| **原生启动方式** | 首次部署并重启后，从 Steam 或桌面快捷方式启动即可使用 |

超分辨率质量从左到右为：**超级性能 → 性能 → 平衡 → 质量 → 超级质量**。屏幕分辨率保持为输出分辨率，场景根据质量档位降低渲染分辨率。

> [!NOTE]
> 当前支持 **Windows x64、非 VR、SDR**。FSR 3.1 超分辨率替代场景抗锯齿，UI 保留独立抗锯齿。UI 按原生帧率更新；打开 UMM 窗口或遇到无法分离的相机空间 UI 时，保留普通呈现。

## 📦 安装

### 前置依赖

| 组件 | 用途 |
| --- | --- |
| **Unity Mod Manager** | 安装与管理模组，最低版本 0.27.3 |
| **DVVulkan** | 部署 DXVK，提供 D3D11 → Vulkan 渲染路径 |
| **DV Language Helper**（`DVLangHelper`） | 加载本地翻译，跟随游戏语言切换 |
| **Lossless Scaling 的 `Lossless.dll`** | 提供 LSFG 模型资源；仅 LSFG 帧生成需要；FSR 超分辨率和帧生成均不需要 |

### 开始使用

1. 退出游戏，安装并启用前置依赖，确保 DVVulkan 已完成 DXVK 部署。
2. 将 `dv-scaling-0.6.1.zip` 拖入 UMM 安装。安装包可使用发布版本，或按下方开发说明自行构建。
3. 启动游戏，在 UMM 中启用 **DV Scaling (Beta)**。使用 LSFG 帧生成时，填写自己的 `Lossless.dll` 完整路径并保存，执行“检查 DLL 兼容性”。
4. 在 **设置 → 图形质量**选择帧生成或超分辨率技术，调整质量与锐化，点击**应用**。
5. 首次启用帧生成后，正常退出游戏，等待自动部署完成，再从 Steam 或原有快捷方式启动。

> [!IMPORTANT]
> 模组包不包含 `Lossless.dll`、提取出的 LSFG 着色器或个人配置。首次启用帧生成、切换帧生成技术以及修改 DLL 路径需要重启；完成首次部署后，无需专用启动脚本。

<details>
<summary>从旧目录升级</summary>

安装目录为 `Mods/dv-scaling`。从旧版本升级时，可将整个 `Mods/DVLSFG` 目录迁移到新目录以保留配置，避免同时保留两份安装。

UMM 内部 ID 仍为 `DVLSFG`，用于保留启用状态和依赖关系。

</details>

## 🎮 设置与使用

| 设置项 | 调整内容 | 生效方式 |
| --- | --- | --- |
| 帧生成 | 关闭 / LSFG 3.1 / AMD FSR FG 3.1 | 首次加载及切换技术需要重启；同一技术可即时开关 |
| 帧生成倍率 | LSFG：2× / 3× / 4×；FSR：固定 2× | LSFG 应用后实时生效 |
| 帧生成模型 | LSFG：标准 / 性能 | LSFG 应用后实时生效 |
| 光流质量 | LSFG：25% / 50% / 75% / 100% | LSFG 应用后实时生效 |
| 超分辨率技术 | 关闭 / FidelityFX™ Super Resolution / AMD FSR 3.1 | 应用后生效 |
| 超分辨率质量 | 五档质量 | 应用后生效 |
| 锐化强度 | 0%–100% | 应用后生效 |

关闭技术时，其从属设置会立即置灰，重新开启保留原值。选择 FSR 3.1 超分辨率时，原生场景抗锯齿选项不可交互；切回 FSR 1.0 或关闭超分辨率后恢复。

从 **质量档位、20% 锐化、2× 帧生成**开始尝试，再按画质和 GPU 负载调整。超分辨率与帧生成可以分别启用，实际表现受场景、GPU 和显示环境影响。

详细状态与启动排查见[中文使用指南](package/README.zh-CN.md)。实机验证范围及尚未覆盖的场景见[验证记录](VALIDATION.md)。

## 🛠️ 开发

<details>
<summary>构建环境与完整构建</summary>

在 `dv-scaling` 项目根目录执行命令。构建需要：

- Visual Studio 2022 C++ 工具、Roslyn 编译器与 Windows SDK（含 FXC）。
- CMake、Vulkan SDK（含 `glslangValidator`）。
- 游戏的 Unity / UMM / I2 / 后处理程序集，以及已安装的 DVLangHelper Runtime/Data 程序集。
- Unity **2019.4.40f1** 编辑器，用于构建 FSR 3.1 资源包。完整构建默认使用 `build/UnityEditor/Editor/Unity.exe`。

```powershell
.\fetch-upstream.ps1
.\build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Derail Valley'
```

将游戏路径替换为自己的安装位置；Vulkan SDK 路径不同时，可传入 `-VulkanSdk`。

输出目录为 `dist/dv-scaling`，当前安装包为 `dist/dv-scaling-0.6.1.zip`。安装后的模组不需要编辑器或着色器编译器。

`port.ps1` 从固定版本的上游生成 Windows 移植代码，输出到 `build/port`。修改移植规则或原生源码，不要直接编辑生成文件。迁移项目目录后，需要重新生成原生 CMake 构建目录，避免沿用旧的绝对路径缓存。

</details>

<details>
<summary>日常增量构建与 FSR 资源包</summary>

已有原生 DLL 和 FSR 资源包时，只重编译托管代码并重新打包：

```powershell
.\build.ps1 -SkipNative -GameDirectory 'D:\SteamLibrary\steamapps\common\Derail Valley'
```

独立重建 FSR 3.1 资源包时，可指定 Unity 编辑器：

```powershell
.\Build-Fsr3.ps1 -UnityEditor 'C:\path\to\Unity.exe'
```

FSR 1.0 的 EASU/RCAS 着色器由 FXC 编译。FSR 3.1 的资源包使用固定的 SDR、反向深度和低分辨率运动矢量变体。

游戏本地引导器只读取系统 Vulkan loader 的导出信息来生成转发代码，不打包系统 loader 二进制，也不修改系统 Vulkan 注册表。

</details>

<details>
<summary>GPU 验证与游戏内测试</summary>

```powershell
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Performance -Multiplier 4
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Disabled
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Toggle
.\tests\Vulkan-Smoke.ps1 -LosslessDll 'C:\path\Lossless.dll' -Validation -Recreate
```

Vulkan 测试使用隐藏窗口，检查生成帧计数、验证层信息与资源销毁，结果写入 `build/smoke-*`。隐式外部图形层仅在测试进程中被抑制；传入 `-WithOverlays` 可测试包含外部图形层的情况，不改变系统图形层设置。

FSR 原生 GPU 测试程序为 `build/native/Release/DVLSFG.FsrGpuTests.exe`，可额外传入游戏 DXVK `d3d11.dll` 的完整路径。

`tests/Install-GameFixture.ps1` 支持 `Synthetic`、`World`、`Ui`、`Pacing`、`Fsr3`、`Fsr3World` 和 `SceneUi` 临时 UMM 测试模组。存档测试使用已有的“继续游戏”存档，测试结束后移除临时模组并恢复配置。

</details>

## 🌐 本地化

界面文本位于 [`package/translations.csv`](package/translations.csv)，安装时复制到模组目录，由 DV Language Helper 加载，不联网获取翻译。

<details>
<summary>编辑或补充翻译</summary>

- 保留 Language Helper 模板的前两行表头，以及 `dv_scaling/` 键名。
- 修改对应语言列；底部简介使用带引号的多行 CSV 单元格。
- 当前提供英文和简体中文。其他语言使用英文回退，繁体中文暂沿用简体文本。
- 翻译源 ID 为 `dv_scaling`，支持 Language Helper 的用户覆盖。

代码通过 `TranslationInjector("dv_scaling")` 注册本地 CSV，使用游戏的 `LocalizationAPI.L` 读取文本。构建引用已安装的 DVLangHelper 程序集，模组包不附带这些第三方二进制。

</details>

## 📚 文档与许可

| 文档 | 内容 |
| --- | --- |
| [中文使用指南](package/README.zh-CN.md) | 安装、设置、启动与兼容性排查 |
| [English Guide](package/README.en.md) | English installation and usage instructions |
| [验证记录](VALIDATION.md) | 测试环境、已验证行为和实际限制 |
| [源码与许可](UPSTREAM.md) | lsfg-vk、FSR 及其他上游的版本、来源与许可 |
| [MIT License](LICENSE) | 本项目代码许可；第三方组件及模型资源按各自许可处理 |

---

<div align="center">

**by BG4JEC** · lsfg-vk by PancakeTAS and contributors

</div>

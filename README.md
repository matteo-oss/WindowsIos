# Flutter

> Windows 桌面灵动岛与桌面控制中心

Flutter 是一个面向 Windows 的桌面灵动岛应用。它将媒体播放、系统状态、通知与常用操作汇集到屏幕顶部的胶囊和展开面板中，并支持插件扩展、主题材质设置以及 Windows 原生集成。

本项目的 Windows 可执行程序程序集名为 `flutter`，应用设置、日志等数据目录仍使用 `FluidBar` 名称。

<p align="center">
  <img src="website/public/posters/hero-idle.png" alt="Flutter 灵动岛收起状态" width="640">
</p>

<p align="center">
  <a href="https://github.com/matteo-oss/WindowsIos/releases">下载 Windows 版本</a> ·
  <a href="https://fluidbar.doulor.cn">项目演示网站</a> ·
  <a href="https://github.com/matteo-oss/WindowsIos/issues">报告问题</a>
</p>

## 功能

- **桌面灵动岛**：在 Windows 桌面顶部显示紧凑胶囊，展开后浏览控制中心、媒体、通知、剪贴板、Agent 和小组件页面。
- **媒体控制**：显示受支持应用的播放状态，并提供媒体控制入口。具体可用能力取决于媒体应用及 Windows 媒体会话接口。
- **系统状态与快捷操作**：提供音量、亮度、网络、电池、蓝牙等系统信息或操作入口；部分能力受设备硬件、Windows 版本和权限影响。当前产品中仍保留 Dock 模式代码和设置项，不应将其与此前的 UI 调整混淆。
- **Windows 通知**：通过 Windows 用户通知监听接口读取通知。首次使用及系统策略可能要求在 Windows 中授权通知访问；拒绝授权时通知页无法读取系统通知。
- **剪贴板历史**：显示应用记录的剪贴板项目，并提供暂停记录、隐藏预览等设置。避免将密码、令牌等敏感内容留在剪贴板历史中。
- **任务与日历小组件**：提供任务查看和管理窗口，可新增、编辑、删除、完成任务；任务列表保存在 `%APPDATA%\FluidBar\tasks.json`。日历和天气数据是否可用取决于系统、网络和位置设置。
- **外观设置**：支持浅色/深色/跟随系统、黑曜石/亚克力/液态玻璃材质、动效、字体比例、屏幕位置及多显示器行为等设置。不同材质的实际观感取决于桌面背景与图形环境。
- **插件系统**：支持管理插件、查看权限并安装 `.fbp` 插件包。仓库包含倒计时和文件暂存可选插件；它们需要单独构建/安装，不应假定首次启动时已启用。
- **文件暂存插件**：安装并启用后可暂存拖入的文件、复制路径/文件、拖出到其他应用；插件也会监视常见的“Screenshots/屏幕截图”目录并自动收录新截图。此功能不是截图工具，不负责拍摄截图；原文件不会因暂存而被移动或删除。
- **便携式发布包**：发布脚本生成 Windows 自包含单文件程序及 ZIP。解压后运行 `flutter.exe`，无需在目标电脑上单独安装 .NET Runtime 或 SDK。

## 界面演示

以下图片是仓库中用于产品演示的静态 UI 素材，不代表当前桌面实时内容。

| 收起状态 | 展开控制中心 |
| --- | --- |
| ![收起状态](website/public/posters/hero-idle.png) | ![展开控制中心](website/public/posters/lab-expanded.png) |

浅色桌面示例：

![浅色桌面上的 Flutter 界面](website/public/images/daylight-desktop.webp)

更多交互演示请访问[项目演示网站](https://fluidbar.doulor.cn)。

## 下载与运行

前往 [GitHub Releases](https://github.com/matteo-oss/WindowsIos/releases) 下载与设备架构相符的 Windows ZIP。通常的 Intel/AMD 电脑选择 `win-x64`；Windows on ARM 设备使用专门提供的 `win-arm64` 包。

1. 解压 ZIP 到一个有写入权限的位置。
2. 双击 `flutter.exe` 启动。应用可能驻留在系统托盘中；可通过托盘图标、胶囊或配置的全局快捷键访问。
3. 若 Windows 弹出通知访问授权提示，按需允许。通知访问也可在 Windows 设置中管理。

便携包包含 .NET 运行时，因此**运行应用无需安装 .NET SDK 或 Runtime**。Windows 安全策略、缺失系统组件或组织策略仍可能阻止未经签名程序运行；本项目没有声称发布包经过代码签名。

> 当前代码声明的最低 Windows 版本为 Windows 10 版本 2004（Build 19041）。新一些的 Windows 功能会在系统不支持时不可用或降级。

## 从源码构建

### 环境要求

- Windows 10 版本 2004（Build 19041）或更新版本；
- .NET SDK `10.0.301` 或支持 `global.json` 中 `latestFeature` 滚动策略的 .NET 10 SDK；
- Git；
- 首次还原 NuGet 依赖时需要网络连接。

克隆仓库并构建：

```powershell
git clone https://github.com/matteo-oss/WindowsIos.git
cd WindowsIos
dotnet restore FluidBar.sln
dotnet build FluidBar.sln -c Release
```

运行桌面应用：

```powershell
dotnet run --project src/FluidBar.App/FluidBar.App.csproj
```

运行解决方案测试：

```powershell
dotnet test FluidBar.sln -c Release
```

### 生成便携 ZIP

在 Windows 仓库根目录双击 `tools\release\package.cmd`，或在 PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/publish.ps1
```

默认目标为 `win-x64`。也可在安装了相应 .NET 10 SDK 的 Windows ARM64 构建环境中生成 ARM64 包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/publish.ps1 -Runtime win-arm64
```

输出目录与压缩包：

```text
build/release/
  win-x64/flutter.exe
  flutter-4.1.0-win-x64-portable.zip
```

脚本以 `Release` 配置发布，启用 `SelfContained`、`PublishSingleFile`，并在输出目录写入包含运行时标记和 SHA-256 的 `publish.json`。`-SkipZip` 可仅发布目录而不创建 ZIP。每次发布前脚本会重建对应的 `build/release/<架构>` 输出目录；不要把个人文件存放在该目录中。

## 可选插件

仓库中的可选插件不会因为存在于源码仓库就自动出现在用户安装中。先构建、打包，再从应用设置中的插件安装入口选择 `.fbp` 文件。

**倒计时插件**

```powershell
dotnet build plugins/fluidbar-timer -c Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-timer --configuration Release --out build/plugins/fluidbar-timer-1.0.0.fbp
```

**文件暂存插件**

```powershell
dotnet build plugins/fluidbar-file-shelf -c Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-file-shelf --configuration Release --out build/plugins/fluidbar-file-shelf-1.0.3.fbp
```

关于暂存容量、截图目录、拖放和权限行为，请参阅各插件自己的说明：[倒计时插件](plugins/fluidbar-timer/README.md)、[文件暂存插件](plugins/fluidbar-file-shelf/README.md)。只安装信任的插件；插件会以当前用户权限运行。

## 配置与数据

- 应用设置：`%APPDATA%\FluidBar\settings.ui.json`；
- 任务列表：`%APPDATA%\FluidBar\tasks.json`；
- 日志：`%LOCALAPPDATA%\FluidBar\logs`；
- 剪贴板及插件数据按对应功能和插件的存储策略保存于本机应用数据目录。

默认设置启用匿名使用数据选项。代码定义的遥测负载包含随机匿名安装 ID、应用/系统环境（包括 Windows 版本、处理器架构和界面语言）、会话时长、首帧启动耗时、首次运行标志和退出原因等字段，不包含通知正文、剪贴板文本或文件路径；可在应用设置中关闭遥测。关闭后会丢弃本地待发送队列。具体实现与字段变更以源码为准。网络功能（例如更新检查、插件市场、天气和遥测）需要网络连接；网络不可用时相应在线能力会受限。

## 项目结构

仓库以 .NET 解决方案 `FluidBar.sln` 为中心。`src/` 中的项目按应用界面、核心状态与策略、绘制、Windows 平台适配和插件基础设施拆分；`tests/` 按对应项目组织单元、集成、架构、界面和长时运行测试。下面列出主要目录及其职责，便于从功能入口追踪到实现与测试。

```text
.
├─ FluidBar.sln                 主解决方案
├─ global.json                  .NET SDK 版本选择
├─ Directory.Build.props        .NET 项目共享构建属性
├─ NuGet.Config                 NuGet 还原配置
├─ LICENSE                      项目许可证
├─ README.md                    项目说明
├─ LearnMust.md                 学习/开发记录
├─ src/
│  ├─ FluidBar.App/             Windows 桌面应用、组合根与界面
│  │  ├─ Hosting/               生命周期、服务装配与宿主
│  │  ├─ Island/                灵动岛窗口及展开面板
│  │  ├─ State/                 展示状态、数据源与内容映射
│  │  ├─ Views/                 设置、任务、剪贴板、通知等窗口
│  │  ├─ Plugins/               应用侧插件界面与接入
│  │  ├─ Rendering/             应用级绘制支持
│  │  ├─ Assets/                图标等资源
│  │  ├─ Localization/          应用本地化 JSON
│  │  └─ Theme/                 字体、设计令牌和宿主布局
│  ├─ FluidBar.Core/            与 UI/Windows API 解耦的领域模型和策略
│  │  ├─ Activities/            活动记录、优先级、调度和状态机
│  │  ├─ Configuration/         配置类型
│  │  ├─ Events/                事件模型与分发
│  │  ├─ Interaction/           翻页、滚动等交互策略
│  │  ├─ Layout/                布局、几何、显示器和位置模型
│  │  ├─ Media/                 媒体时钟等领域逻辑
│  │  ├─ Motion/                动画策略
│  │  ├─ Presentation/          展示状态投影
│  │  ├─ Settings/              设置模型、存储、迁移和校验
│  │  ├─ Telemetry/             遥测负载与队列
│  │  └─ Diagnostics/            诊断接口和计数器
│  ├─ FluidBar.Island/           灵动岛页面布局与 Skia 绘制
│  │  ├─ IslandRenderer.cs       岛体渲染入口
│  │  ├─ PageRenderer*.cs        页面内容绘制
│  │  ├─ CompactRenderer.cs      收起态绘制
│  │  ├─ *Layout.cs              媒体、页面、轨道等布局
│  │  └─ *Painter.cs             材质和玻璃效果绘制
│  ├─ FluidBar.Platform.Windows/ Windows 原生系统能力适配
│  │  ├─ Hosting/                原生窗口与分层表面
│  │  ├─ Interop/                Win32/COM 互操作
│  │  ├─ Media/                  媒体会话、歌词、音频回环和播放器兼容
│  │  ├─ Notifications/          系统通知读取与监听
│  │  ├─ Clipboard/              剪贴板监听和历史
│  │  ├─ Display/ Monitors/      显示器枚举与策略
│  │  ├─ Devices/ Radios/        设备信息和无线电开关
│  │  ├─ Agent/                  外部 Agent 会话及事件接入
│  │  ├─ Calendar/ Tasks/        日历和任务服务
│  │  ├─ Shell/                  托盘、启动、热键和系统主题
│  │  ├─ Focus/ Weather/         专注状态与天气服务
│  │  ├─ Update/                 更新检查及应用替换
│  │  └─ Diagnostics/            平台日志和诊断
│  ├─ FluidBar.Plugins.Builtin/  随主应用提供的内置插件
│  ├─ FluidBar.PluginHost/       插件发现、加载与生命周期管理
│  ├─ FluidBar.Sdk/              插件 API、契约、清单和本地化类型
│  ├─ FluidBar.Marketplace/      市场索引、插件包及签名模型/校验
│  └─ FluidBar.Cli/              命令行工具（含插件打包命令）
├─ tests/                        自动化测试与验证程序
│  ├─ FluidBar.Core.Tests/       核心状态、布局、设置和策略
│  ├─ FluidBar.Island.Tests/     绘制、布局与 golden 图像回归
│  ├─ FluidBar.Platform.Tests/   Windows 服务与系统集成边界
│  ├─ FluidBar.Ui.Tests/         界面模型及视觉行为
│  ├─ FluidBar.PluginHost.Tests/ 插件宿主测试
│  ├─ FluidBar.Sdk.Tests/        SDK 契约和清单测试
│  ├─ FluidBar.BuiltinPlugins.Tests/ 内置插件测试
│  ├─ FluidBar.OptionalPlugins.Tests/ 可选插件测试
│  ├─ FluidBar.Integration.Tests/ 跨模块集成测试
│  ├─ FluidBar.Architecture.Tests/ 项目依赖和架构约束
│  ├─ FluidBar.Soak/             压力与长时间运行场景
│  └─ *Evidence/                  验收、设置和市场证据程序
├─ plugins/
│  ├─ fluidbar-timer/             倒计时可选插件源码
│  ├─ fluidbar-file-shelf/        文件暂存可选插件源码
│  └─ samples/                    最小/完整能力插件示例
├─ samples/                       SDK 页面扩展示例
│  ├─ FluidBar.Sample.LocalPages/ 本地页面示例
│  └─ FluidBar.Sample.OverflowPages/ 溢出页面示例
├─ marketplace/                   市场目录、索引与公钥材料
├─ schemas/                       插件、市场、语言和签名 JSON Schema
├─ telemetry/worker/              公共统计 Worker、SQL schema 和脚本
├─ tools/
│  ├─ release/                    Windows 发布、单文件和 ZIP 脚本
│  ├─ smoke/                      宿主冒烟验证脚本
│  ├─ perf/                       性能探针
│  ├─ spikes/                     图形宿主、音频等技术验证项目
│  ├─ NotificationDbProbe/        通知数据库诊断工具
│  ├─ agent-emit/                 Agent 事件桥接脚本
│  └─ dsh-plugin-fluidbar-agent/  DSH Agent 插件适配工具
├─ build/                          本地构建和发布生成物
└─ website/                        网站源码区域（当前工作树未包含）
```

### 模块职责与依赖边界

- **应用入口**：`FluidBar.App` 启动桌面进程、组合服务、管理窗口，并将系统或插件数据映射为岛上内容。
- **领域逻辑**：`FluidBar.Core` 保存可独立验证的状态、布局、设置和交互规则，避免直接依赖 Win32 或具体绘图库。
- **界面绘制与系统适配**：`FluidBar.Island` 将展示状态绘制为灵动岛视觉；`FluidBar.Platform.Windows` 提供窗口、媒体、通知、剪贴板和设备等 Windows 能力，由应用层组合使用。
- **插件链路**：`FluidBar.Sdk` 定义插件 API，`FluidBar.PluginHost` 管理运行时加载，`FluidBar.Marketplace` 处理市场数据与签名逻辑。`plugins/` 中的插件独立构建并以 `.fbp` 安装；内置插件由 `FluidBar.Plugins.Builtin` 随应用提供。
- **验证链路**：`tests/` 对应覆盖领域规则、绘制、平台适配、插件和跨模块行为；`tests/FluidBar.Island.Tests/golden/` 存放图像回归基准。

`build/` 通常由构建或发布脚本生成。`website/` 在当前工作树中不存在，因此此处仅标注仓库的网站源码区域，不表示它可以在当前检出版本中构建。

## 问题反馈与贡献

使用 [GitHub Issues](https://github.com/matteo-oss/WindowsIos/issues) 报告缺陷或提出建议。请尽量提供 Windows 版本、处理器架构、应用版本、复现步骤和相关日志；提交前删除通知内容、个人路径等敏感信息。代码改动建议先运行相关测试，并说明验证过的系统环境。

## 许可证

本项目以 [MIT License](LICENSE) 授权。第三方依赖和网站字体/组件可能分别适用其自身许可证，请同时遵守相应许可文件。

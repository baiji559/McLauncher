# MC Launcher

一个面向 Windows 的轻量级 **Minecraft: Java Edition** 启动器。界面用 WPF，登录、下载、校验和启动交给固定版本的 [PortableMC](https://github.com/theorzr/portablemc) 后端，不依赖 PCL、HMCL 或其他第三方启动器核心。

![platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-68217A)
![backend](https://img.shields.io/badge/backend-PortableMC%205.0.5-2F6F44)

## 界面

![MC Launcher 主界面](docs/screenshot.png)

左侧是账号、版本与内存设置，右侧是 PortableMC 的实时日志，底部是操作按钮与状态行。

## 功能特性

| 功能 | 说明 |
| --- | --- |
| 微软正版登录 | 设备代码流：登录时自动复制验证码并打开微软验证页，成功后账号进入本地列表 |
| 离线账号 | 3–16 位字母、数字或下划线，启动时以离线身份进入游戏 |
| 版本列表 | 先扫描本地 `.minecraft/versions`，再合并 Mojang 正式版清单；下拉框可编辑，也能直接手输版本名 |
| 目录识别 | 可选 `.minecraft` 根目录、`versions` 目录或某一个具体版本目录；版本目录里存在 `mods/`、`saves/` 或 `options.txt` 时按隔离实例运行，使用自己的 `mods/config/saves` |
| 下载与修复 | 调用 PortableMC 的 `--dry` 校验，缺什么补什么，已完整的文件不会重复下载 |
| Java 处理 | 优先用系统 Java，没有合适版本时自动下载 Mojang Runtime（`--jvm-policy system-then-mojang`） |
| 内存设置 | 1–32 GB 滑杆，直接映射为 `-Xmx` |
| 进度与日志 | 下载进度按文件数换算，界面最多保留最近 1000 行日志 |
| 随时取消 | 取消会结束整棵进程树，不会留下后台 Java 进程 |
| 设置持久化 | 目录、版本、内存、账号方式写入 `%LOCALAPPDATA%\McLauncher\settings.json`，下次启动自动恢复 |

## 快速开始

环境要求：Windows 10 1809 或更高版本，.NET 10 SDK（开发）或 .NET 10 Desktop Runtime（运行已构建的程序）。

```powershell
git clone https://github.com/baiji559/McLauncher.git
cd McLauncher
dotnet build McLauncher.slnx -c Release
dotnet run --project src\McLauncher.App\McLauncher.App.csproj
```

只想构建界面项目时可以直接指定项目，Core 会被自动带上：

```powershell
dotnet build src\McLauncher.App\McLauncher.App.csproj -c Release
```

运行核心冒烟测试（不依赖第三方测试框架）：

```powershell
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj
```

正常情况下输出 6 行 `PASS`。

## 使用流程

1. **选择目录**：点「浏览…」选 `.minecraft` 根目录，或 `versions` 下的某个版本目录。默认目录是 `%LOCALAPPDATA%\McLauncher\game`。
2. **选择版本**：下拉框中本地版本排在前面，Mojang 正式版跟在后面；也可以直接输入版本名。
3. **选择账号**：默认微软正版，第一次需要点「登录」完成设备代码授权；也可以切到「离线」并填用户名。
4. **下载 / 修复**：第一次装某个版本先点这个按钮，等待进度条走完。之后文件缺失或被改动时同样用它修复。
5. **启动游戏**：按钮下方显示状态和 Java 选择结果，右侧日志给出 PortableMC 的完整输出。

> 微软认证缓存在资源目录下的 `portablemc_msa.json`，里面是会话令牌，不要提交到仓库或分享给别人。

## 项目结构

```text
McLauncher.slnx
├─ src/
│  ├─ McLauncher.Core/          PortableMC 适配层：进程、命令行参数、机器输出解析、设置模型
│  └─ McLauncher.App/           WPF 界面：MainWindow.xaml + MainViewModel（MVVM）
├─ tests/
│  └─ McLauncher.Core.Tests/    控制台冒烟测试，覆盖参数构造、事件解析、目录识别
├─ tools/
│  └─ portablemc/               固定的 PortableMC 5.0.5 发行文件、LICENSE 与版本说明
├─ Directory.Build.props        统一开启 nullable、隐式 using，警告视为错误
└─ THIRD_PARTY_NOTICES.md       第三方组件版本、许可证与哈希
```

## 设计要点

```mermaid
flowchart LR
    UI["WPF 界面<br/>MainWindow + MainViewModel"] -->|"ICommand"| CORE["McLauncher.Core<br/>PortableMcClient"]
    CORE -->|"ArgumentList"| PMC["portablemc.exe 5.0.5"]
    PMC -->|"下载 / 校验"| MOJANG[("Mojang 清单与资源")]
    PMC -->|"启动"| GAME["Minecraft 客户端"]
    PMC -.->|"制表符机器事件"| CORE
```

- **UI 与后端解耦**：`McLauncher.Core` 不引用 WPF，`McLauncher.App` 只做界面与交互，换 UI 或换后端都只动一侧。
- **只认机器输出**：所有调用都带 `--output machine`，按制表符切分事件后映射到界面，不解析人类可读文本，避免日志格式变化导致崩溃。
- **同一时刻一个后端进程**：`PortableMcClient` 用信号量串行化调用，取消时连带结束子进程树。
- **线程模型**：可执行文件输出的每一行先进并发队列，再通过 `Dispatcher` 回到 UI 线程，长任务不阻塞界面。

## 已知限制

- 只支持原版和已有的本地版本启动，不提供模组、加载器或整合包的安装管理。
- 没有启动器自动更新，也没有 Mod 市场。
- 仅支持 Windows（WPF），界面只有中文。
- 微软登录使用设备代码，需要在浏览器里手动确认一次。

## 第三方组件

- **PortableMC 5.0.5**（Apache License 2.0，commit `99fdead`）：作为独立进程调用，未修改、未链接其源码。仓库内 `tools/portablemc/` 是上游官方 Windows x64 发行包的原样文件，许可证与 SHA-256 见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
- 选型过程与放弃的方案（Prism Launcher、Modrinth App、XMCL、ATLauncher、CmlLib.Core 等）记录在 [OPEN_SOURCE_RESEARCH.md](OPEN_SOURCE_RESEARCH.md)。

## 许可证

本仓库暂未附带开源许可证，默认保留所有权利。若要复用其中的代码，请先联系作者。

## 免责声明

本项目不是 Mojang Studios 或 Microsoft 的官方产品，与二者无关联，也不分发任何游戏本体或资源文件。使用前请自备 Minecraft 正版账号，并遵守 [Minecraft 最终用户许可协议](https://www.minecraft.net/eula)。

## English overview

MC Launcher is a small Minecraft: Java Edition launcher for Windows, written in C# with WPF on .NET 10. It delegates login, download, integrity verification and game launch to a pinned PortableMC 5.0.5 binary, and only consumes that binary's tab-separated `--output machine` events, so the UI layer and the launcher backend stay decoupled.

Highlights: Microsoft device-code login plus offline accounts, local and remote version lists, isolated-instance detection, download/repair, automatic Java detection with Mojang runtime fallback, configurable heap size, live progress and log, and cancellation that kills the whole process tree.

```powershell
dotnet build McLauncher.slnx -c Release
dotnet run --project src\McLauncher.App\McLauncher.App.csproj
```

No license is granted yet; all rights reserved.

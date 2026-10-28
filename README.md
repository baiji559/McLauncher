<div align="center">

# MC Launcher

**一个轻量、简洁的 Minecraft: Java Edition 启动器**

在 Windows 上管理游戏版本、启动实例，并从 Modrinth 和 CurseForge 查找资源。

![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF-68217A)
![PortableMC](https://img.shields.io/badge/PortableMC-5.0.5-2F6F44)

[功能](#功能) · [开始使用](#开始使用) · [资源中心](#资源中心) · [从源码构建](#从源码构建) · [已知限制](#已知限制)

![MC Launcher 主界面](docs/screenshot.png)

</div>

## 功能

| | |
| --- | --- |
| **启动与安装** | 浏览本地版本和 Mojang 正式版，下载、修复并启动游戏；支持手动输入版本。 |
| **账号** | 使用微软账号设备代码登录，或设置离线用户名。 |
| **游戏目录** | 选择 `.minecraft` 根目录或具体版本目录，识别已有隔离实例。 |
| **资源中心** | 搜索 Modrinth 和 CurseForge 的模组、整合包、光影包和资源包，按游戏版本和加载器筛选。 |
| **运行管理** | 调整最大内存，查看进度和最近 1000 行日志，取消正在进行的操作。 |

启动、登录、游戏文件下载与校验由 [PortableMC 5.0.5](https://github.com/theorzr/portablemc) 处理。启动器自动检测系统 Java，需要时使用 Mojang Runtime。

## 开始使用

### 运行环境

- Windows 10 1809 或更新版本，x64。
- 首次下载游戏、使用微软账号登录或搜索资源时需要网络连接。
- 独立单文件版包含 .NET 运行时；轻量版需要安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。
- 从源码构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

从源码构建或发布的命令见下方[从源码构建](#从源码构建)。

### 启动游戏

1. 打开「设置」，选择游戏目录。默认目录为 `%LOCALAPPDATA%\McLauncher\game`；也可以选择已有的 `.minecraft`、`versions` 或具体版本目录。
2. 打开「游戏版本」，选择或输入版本。列表优先显示本地版本；选择已安装版本后会回到首页。
3. 在「设置」中选择微软正版或离线账号。微软登录会打开验证页面并复制设备代码；离线用户名需为 3–16 位英文字母、数字或下划线。
4. 按需调整最大内存。首次使用某个版本时，可在「游戏版本」中点击「下载 / 修复」。
5. 回到首页，点击「启动游戏」。底部状态和「运行日志」会显示下载、Java 选择及游戏运行情况。

选择具体版本目录时，启动器使用该目录运行游戏。从游戏根目录选择版本时，如果该版本目录包含 `mods/`、`saves/` 或 `options.txt`，也会将其识别为隔离实例。

## 资源中心

选择平台与资源类型，输入关键词，再按需填写游戏版本和加载器。搜索后选择项目，点击「查看版本」，选择文件并下载；关键词留空可浏览热门资源。

| 资源 | 当前游戏运行目录下的保存位置 |
| --- | --- |
| 模组 | `mods/` |
| 整合包 | `modpacks/` |
| 光影包 | `shaderpacks/` |
| 资源包 | `resourcepacks/` |

下载时会校验平台提供的文件大小和哈希；失败或取消时会清理临时文件，已有同名文件不会被覆盖。模组搜索支持部分中文名称和缩写，如「苹果皮」「锂」「实体渲染机制优化」和「JEI」。未收录的名称会按原词搜索，也可以尝试英文名。名称索引位于 [`mod-search-names.json`](src/McLauncher.Core/mod-search-names.json)。

Modrinth 不需要 API Key。使用 CurseForge 搜索和查看文件时，需要在「网络设置 / CurseForge API Key」中输入官方密钥，或在启动前设置 `CURSEFORGE_API_KEY` 环境变量。密钥可通过 [CurseForge for Studios](https://console.curseforge.com/) 申请，仅在当前进程中使用，不写入启动器设置。如果作者未开放直接下载，可使用「打开文件网页」手动获取。

## 从源码构建

在 PowerShell 中运行：

```powershell
git clone https://github.com/baiji559/McLauncher.git
cd McLauncher
dotnet build McLauncher.slnx -c Release
dotnet run --project src\McLauncher.App\McLauncher.App.csproj -c Release
```

普通构建输出位于 `src\McLauncher.App\bin\Release\net10.0-windows\`，运行时需要保留同目录的 DLL 和配置文件。

### 发布单文件

**独立版**包含运行时，可直接在 Windows x64 上运行：

```powershell
dotnet publish src\McLauncher.App\McLauncher.App.csproj -p:PublishProfile=Standalone
```

输出：`artifacts\standalone\McLauncher.App.exe`

**轻量版**使用电脑上已安装的 .NET 10 Desktop Runtime：

```powershell
dotnet publish src\McLauncher.App\McLauncher.App.csproj -p:PublishProfile=Lightweight
```

输出：`artifacts\lightweight\McLauncher.App.exe`

两种发布版都内嵌压缩后的 PortableMC 和许可证。启动时会将后端释放到 `%LOCALAPPDATA%\McLauncher\backend\` 并校验 SHA-256。

### 运行测试

核心测试和 Windows 界面测试不依赖第三方测试框架：

```powershell
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release
dotnet run --project tests\McLauncher.App.Tests\McLauncher.App.Tests.csproj -c Release
```

界面测试会检查本地数据、导航、绑定和窗口布局，并在 `artifacts/` 中生成预览图。核心测试支持按需运行真实网络测试和分页内存分配测量：

```powershell
# Modrinth 搜索、文件列表和下载
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release -- --live

# 中文名称搜索
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release -- --live-search

# 系统代理与直连诊断
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release -- --live-network

# 测量 5000 个版本中一页文件的托管内存分配
dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release -- --measure-allocations
```

## 数据与网络

- 设置保存在 `%LOCALAPPDATA%\McLauncher\settings.json`。
- 微软登录会话保存在所选资源目录的 `portablemc_msa.json` 中，包含会话令牌，请勿分享。
- 资源搜索和下载默认使用系统代理，可在「网络设置」中切换为直连；此设置不控制 PortableMC 的网络连接。
- 资源请求遇到部分临时网络错误或限流时会重试；TLS 握手失败时会尝试兼容连接，证书校验保持开启。

## 已知限制

- 支持启动原版和已有本地版本，暂不自动安装加载器、模组依赖或导入整合包。整合包以平台原始 `.mrpack` 或 `.zip` 文件保存。
- 模组需要匹配游戏版本、加载器及依赖；光影包需要兼容的光影支持。
- 暂不自动更新启动器或已下载的资源。界面为中文，仅支持 Windows。

## 项目资料

- [`src/McLauncher.App`](src/McLauncher.App)：WPF 界面与交互。
- [`src/McLauncher.Core`](src/McLauncher.Core)：PortableMC 适配、资源平台 API 与下载。
- [`tests`](tests)：核心逻辑和界面测试。
- [`tools/portablemc`](tools/portablemc)：PortableMC 5.0.5 及其许可证。
- [界面说明](docs/UI-POLISH.md) · [代码审查与优化记录](docs/CODE-REVIEW.md) · [第三方组件](THIRD_PARTY_NOTICES.md) · [选型记录](OPEN_SOURCE_RESEARCH.md)

## 许可证与免责声明

本仓库暂未附带开源许可证，默认保留所有权利。若要复用代码，请先联系作者。

本项目不是 Mojang Studios 或 Microsoft 的官方产品，与二者无关联，也不分发游戏本体或资源文件。使用前请自备 Minecraft 正版账号，并遵守 [Minecraft 最终用户许可协议](https://www.minecraft.net/eula)。

## English overview

MC Launcher is a Windows x64 Minecraft: Java Edition launcher built with WPF and .NET 10. It uses PortableMC 5.0.5 for authentication, installation and launch, and supports browsing and downloading content from Modrinth and CurseForge. CurseForge requires an API key; mod loaders, dependencies and modpacks are not installed automatically.

See the build and publish commands above. No license is granted for this repository yet; all rights reserved.

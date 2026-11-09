# LATTICE

一个面向 Windows x64 的 Minecraft: Java Edition 启动器，使用 WPF / .NET 10 构建，基于 PortableMC 5.0.5 处理账号、游戏安装、修复与启动。

当前代码为精简后的 Soft Voxel 界面版本，支持 PCL 游戏目录、本地模组及资源管理。主导航为 **主页、模组、资源、设置**。

## 功能

| 功能 | 说明 |
| --- | --- |
| 游戏版本 | 识别已有本地版本，选择或输入游戏版本，下载、修复并启动游戏。 |
| 账号与运行 | 微软账号设备代码登录或离线用户名，设置最大内存，自动选择 Java，查看日志及取消操作。 |
| 本地模组 | 搜索、筛选已启用模组，实际启用或停用 `.jar` 文件。 |
| 在线资源 | 搜索 Modrinth / CurseForge 的模组、整合包、资源包、光影，按游戏版本和加载器筛选。 |
| 本地已下载 | 汇总共享目录及各实例资源，按文件名或实例搜索、按类型筛选，打开位置或移到回收站。 |
| PCL 与便携 | 识别 PCL 游戏目录和版本隔离设置，支持相对路径配置及整目录迁移。 |

## 运行环境

- Windows x64。
- **轻量版不包含 .NET**，运行前安装 [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0)。请选择桌面运行时。
- 独立版包含 .NET 运行时，可直接运行。
- 从源码构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
- 在线搜索、下载和微软账号登录需要网络连接。

## 从源码运行

```powershell
git clone https://github.com/baiji559/McLauncher.git
cd McLauncher
dotnet build McLauncher.slnx -c Release -m:1
dotnet run --project src/McLauncher.App/McLauncher.App.csproj -c Release
```

普通构建输出位于 `src/McLauncher.App/bin/Release/net10.0-windows/`，运行该输出时保留同目录 DLL 和配置文件。

## 发布 EXE

两种配置均生成 Windows x64 单文件 EXE，并包含启动器资源与 PortableMC 后端。

| 发布配置 | .NET 运行时 | 输出 |
| --- | --- | --- |
| `Lightweight` 轻量版 | 使用电脑已安装的 .NET 10 Desktop Runtime | `artifacts/lightweight/McLauncher.App.exe` |
| `Standalone` 独立版 | 包含运行时 | `artifacts/standalone/McLauncher.App.exe` |

### 轻量版：不带 .NET

```powershell
dotnet publish src/McLauncher.App/McLauncher.App.csproj -p:PublishProfile=Lightweight -m:1
```

### 独立版：包含 .NET

```powershell
dotnet publish src/McLauncher.App/McLauncher.App.csproj -p:PublishProfile=Standalone -m:1
```

升级启动器时关闭旧程序，只替换 EXE，保留自己的 `lattice.portable.json`。

## 开始使用

1. 在「设置 → 通用」中选择游戏目录。支持 `.minecraft`、`versions`、具体实例目录，以及包含 `.minecraft` 的 PCL 上级目录。
2. 点击主页大版本号进入版本管理，选择已有版本或输入新版本。首次使用可点击「下载 / 修复」。
3. 选择微软账号或填写离线用户名，按需设置内存。
4. 回到主页点击「启动游戏」。进度和状态位于页脚，详细信息见运行日志。

选择实例的 `natives`、`mods` 或 `saves` 子目录时，会尝试恢复到所属游戏目录。PCL `PCL/Setup.ini` 中明确的共享或隔离设置会被遵循。

## 资源下载与删除

在「资源」中选择分类和平台，输入关键词并搜索；留空可浏览热门项目。选中项目会加载右侧文件列表，点击对应版本行即可下载。无法直接下载的文件提供官网入口。

| 资源类型 | 保存位置 |
| --- | --- |
| 模组 | `mods/` |
| 整合包 | `modpacks/` |
| 资源包 | `resourcepacks/` |
| 光影 | `shaderpacks/` |

在「资源 → 本地已下载」查看当前游戏根目录与 `versions/` 下各实例的资源，也包含手动放入的文件和已停用模组。

**删除步骤：点击资源行右侧「删除」→ 在列表上方核对文件名 → 点击「确认删除」。** 文件移到 Windows 回收站，列表和模组数量同步刷新。取消会保留文件；文件被占用时会显示失败原因，关闭占用程序后可重试。

下载会检查平台提供的文件大小与哈希，失败或取消会清理临时文件，已有同名文件不会被覆盖。平台日期异常时显示「日期未知」，不影响文件列表加载。

Modrinth 不需要 API Key。CurseForge 需要官方密钥，可在连接选项中输入，或通过 `CURSEFORGE_API_KEY` 环境变量提供；密钥仅在当前进程使用，不写入配置。资源连接默认使用系统代理，可切换为直连。

## PCL 与换设备

把 EXE 放到 `.minecraft` 旁边，或放进同级的启动器子文件夹，程序会识别附近游戏目录并使用便携设置。也可提供 `lattice.portable.json`，例如：

```json
{
  "GameDirectory": ".minecraft",
  "Version": "release",
  "MemoryMb": 4096,
  "UseMicrosoftAccount": false,
  "OfflineUsername": "Player"
}
```

便携模式将游戏目录保存为相对于启动器的路径。换设备时复制启动器、便携配置和完整 `.minecraft`，保持相对位置；微软账号在新设备重新登录。游戏位于其他独立位置时，在设置中选择实际目录。

普通模式设置位于 `%LOCALAPPDATA%/McLauncher/settings.json`。微软会话令牌保存在游戏目录，不能随源码或日志公开分享。

## 验证与源码打包

```powershell
dotnet run --project tests/McLauncher.Core.Tests/McLauncher.Core.Tests.csproj -c Release
dotnet run --project tests/McLauncher.App.Tests/McLauncher.App.Tests.csproj -c Release
powershell -File tools/package-source.ps1
```

核心测试覆盖双平台资源 API、异常日期、分页、下载校验、取消、网络重试和路径边界；WPF 测试使用隔离夹具，覆盖真实回收站删除、文件占用保护、绑定、导航、滚动和不同窗口尺寸。测试不需要真实微软账号或启动真实 Minecraft。

源码包默认输出到 `artifacts/source/LATTICE-latest-source.zip`，包含校验清单；说明文件使用英文名，排除编译缓存和本地配置。

## 当前范围

- 世界管理页面及相关模型、扫描、选择、备份代码已移除；已有存档文件保留。
- 模组依赖及加载器需要另外安装；整合包目前以原始 `.mrpack` / `.zip` 保存。
- 不会自动更新启动器或已下载资源。

## 项目结构

- [`src/McLauncher.App`](src/McLauncher.App)：WPF 界面、交互与视图模型。
- [`src/McLauncher.Core`](src/McLauncher.Core)：游戏目录、PortableMC、资源 API 与下载。
- [`tests`](tests)：核心与界面回归测试。
- [`tools/portablemc`](tools/portablemc)：固定版本后端及许可证。
- [`DESIGN.md`](DESIGN.md)：当前设计约束。
- [`REFINEMENT_NOTES.md`](REFINEMENT_NOTES.md)：本轮代码精简说明。

第三方组件见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目不隶属于 Mojang 或 Microsoft；本仓库未单独授予开源许可证，第三方依赖仍遵循各自许可证。

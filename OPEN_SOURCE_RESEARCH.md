# 开源方案调研

调研日期：2026-10-27。候选启动器以 GitHub 星标不少于 500、近期仍有维护活动、许可证明确为硬条件。星标数会随时间变化，以下是调研时的近似值。

| 项目 | 星标 | 许可证 | 实现与依赖 | 结论 / 已知代价 |
|---|---:|---|---|---|
| [Prism Launcher](https://github.com/PrismLauncher/PrismLauncher) | 10.9k | GPL-3.0 | C++20、Qt 6，完整桌面启动器 | 设计成熟，但核心与 Qt UI 结合较深；GPL 传播要求和本项目后续换 UI 的目标不合适。 |
| [Modrinth App](https://github.com/modrinth/code) | 2.5k | GPL-3.0-only | Rust、Tauri、Vue | 下载和任务模型成熟，主要面向模组生态；Web UI 技术栈与首版 Windows 原生 UI 不符。 |
| [XMCL](https://github.com/Voxelum/x-minecraft-launcher) | 1.7k | MIT | TypeScript、Electron、Vue | 模块较多且许可证宽松，但 Electron 运行时较重，产品范围也远大于本期原版启动需求。 |
| [ATLauncher](https://github.com/ATLauncher/ATLauncher) | 856 | GPL-3.0 | Java、Swing | 长期维护，重点是整合包和 Mod 平台；需额外 Java UI 运行时，且 GPL 不适合直接嵌入。 |
| [PortableMC](https://github.com/theorzr/portablemc) | 558 | Apache-2.0 | Rust CLI，单文件 Windows 后端，稳定机器输出 | 采用。覆盖微软登录、原版校验/下载、Java 选择与 Mojang Runtime 回退，适合作为可替换的进程适配层。 |

未采用 [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core)：调研时约 283 星，未达到用户指定的 500 星门槛。

## 采用方式

本项目没有复制上述启动器源码。WPF 仅通过 `ProcessStartInfo.ArgumentList` 调用固定版本 PortableMC，并解析其制表符机器输出。这样可以隔离命令行细节，也能在后续替换 UI 或后端时保留清晰边界。

PortableMC 5.0.6 在当前 Windows 网络环境中可稳定复现访问 Mojang 清单时的 rustls `unexpected_eof` / `Connect` 失败；同一环境下系统 HTTP 客户端和 PortableMC 5.0.5 可访问。因此首版固定为 5.0.5，后续升级必须先重新执行 Mojang 清单、文件下载和微软登录连通性测试。这是本机复现结果，不等同于上游对所有环境的通用结论。


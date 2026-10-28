# 桌面界面更新

沿用用户确认的第一版：深色固定侧栏、主世界落日背景、单个启动区域，以及读取本地文件的版本和模组列表。

- 首页连接原有启动命令，展示当前版本、加载器与实际内存配置。
- 游戏版本页保留在线列表、手动输入和下载 / 修复功能。
- 资源中心保留 Modrinth / CurseForge、四类资源、版本与加载器筛选。
- 账号、目录、内存与 Java 信息位于设置页；完整输出位于运行日志页。
- 标题栏支持拖动、缩放、最小化、最大化和关闭；初始尺寸适配屏幕工作区。
- 首页只展示已有版本和启用的 `.jar` 文件；没有本地内容时展示空状态。

## 资源

风景图通过内置 imagegen 工具生成，已复制并嵌入 `src/McLauncher.App/Assets/overworld-sunset.png`。草方块、头像与线条图标由 XAML 绘制，无新增图标依赖。

最终图像提示词：

```text
Use case: stylized-concept. Asset type: background artwork for a native Minecraft Java launcher, wide cinematic 16:9 landscape, 1920x1080 or larger. Primary request: a peaceful Minecraft voxel overworld at late golden hour, blocky grass terraces, oak and birch trees, a quiet winding river in the valley, layered distant mountains, a few small white flowers in foreground. Authentic cubic Minecraft-style block materials with restrained shader lighting and subtle atmospheric depth, natural warm sunlight and muted forest greens. Composition: wide vista with horizon in upper half, sun low toward right, middle-left area of dark woodland and open sky providing room for overlay text. Lush but understated, calm nostalgic singleplayer world, no oversaturated colors or dramatic fantasy structures. Constraints: landscape artwork only, edge to edge; absolutely no interface, no words, no lettering, no logos, no watermark, no frames, no characters.
```

## 验证与产物

`McLauncher.App.Tests` 覆盖实际目录与加载器识别、禁用模组过滤、五页导航、选择版本返回首页、内存绑定、1280 × 820 和 1020 × 640 窗口渲染，以及原有资源搜索与下载绑定检查。测试使用独立目录，不修改用户游戏文件和账号设置。

预览图位于 `artifacts/launcher-*-preview.png` 与 `artifacts/launcher-*-small.png`。

独立单文件版：`artifacts/standalone/McLauncher.App.exe`。轻量单文件版：`artifacts/lightweight/McLauncher.App.exe`。

本次验证不包含实际微软登录或启动游戏会话。

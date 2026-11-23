# 与原始 v1.0.0 的累计差异

对比基线为 mytjxs/codex-startup-animation 的 main 提交 `9ea3b32b4e53f2d9db5d54265f6341b63aee5c04`，以及原作者 v1.0.0 完整安装包。Git 主分支只有 README 与 ZIP 校验信息，因此源码对比以 release ZIP 内的源码为准。

原始 ZIP SHA-256：`682928d2e6ce85bfb3d83acadb4e85da9efe4948a593a63e4dc2e605a08ff524`。已下载并核对一致。逐文件记录见 [基线清单](upstream-v1.0.0-inventory.json)。本分支包含累计版本的完整源码，最近一次启动修复属于其中一部分。

| 范围 | 原始版本 | 累计版本 |
| --- | --- | --- |
| 启动画面 | 8 秒固定影片 | 开场、可循环等待、结尾冲击与主窗口交接；已有流泪转场素材 |
| 影片与角色 | 原版角色、60 fps 影片 | 自定义角色素材及六段影片；高帧率常态壁纸与粒子壁纸 |
| 程序内壁纸 | 无 | renderer、宿主、私有本地管道、准备/提交与播放状态确认 |
| 壁纸轮换 | 无 | 常态/粒子随机轮换、间隔、隐藏暂停、资源压力与恢复处理 |
| 窗口交接 | 影片结束后启动 | 窗口定位、隐藏确认、遮罩、焦点、恢复守卫和失败回退 |
| 更新兼容 | 手工配置应用标识 | 本机官方 Store 安装发现、签名与文件校验、更新入口 |
| 任务栏 | 原入口 | 本地任务栏图标与启动入口适配，尊重已存在的窗口配置 |
| 分发 | 已编译 ZIP 与源码 | 完整媒体清单、四个可执行组件、安装/卸载、编译记录和包校验 |
| 排查 | 原有预览结果 | 有界数字诊断、脱敏报告与离线测试 |

## 原有源码

原 ZIP 中 5 个 C# 文件均有累计修改：DragonLauncher.cs、CinematicScene.cs、EyeRig.cs、FilmSplash.cs、StrokeTrace.cs。新增 FilmSplashV2、BridgeActivation、NativeHandoff、NativeRestoreGuard、PulseReveal、StartupReliability、TaskbarIcon、WallpaperControls，以及 wallpaper-host-src、renderer.js、packaging 和 tests。

WallpaperSetup.cs 与 SetupReleaseManifest.cs 保留作历史源码参考，不属于公开入口的编译目标。早期的本机官方图标集成、个人配置、登录资料、机器日志和旧安装目录不在本分支分发范围内。

## 2026-11-23 启动兼容修复

- 在写入 renderer 之前，主窗口明确被替换时最多重新选择三次，并排除所有旧页面。
- 工作区就绪共享 60 秒期限，换窗不重新计时；实际工作区出现后，安装与布局共享 12 秒。
- 根节点检查容忍尚未建立的 documentElement；读取中的短暂 TypeError/ReferenceError/未知异常最多重试两次。持续错误终止；SyntaxError/EvalError/SecurityError 立即终止。
- 新增工作区就绪与异常类别的数字诊断，保留现有视频、视觉配置及外层 90 秒上限。

## 验证与范围

Windows 本机已通过就绪检测、页面选择、准备/提交、运行期诊断和新增兼容测试。新增测试覆盖 17 秒后就绪、瞬时与持续脚本异常、两次换窗、共享期限，以及语法错误即时失败。空 documentElement、无工作区、隐藏工作区、就绪工作区及外站拒绝也已检查。完整包的媒体与源码清单已核验。

最新启动候选版尚未完成登录用户会话下的重启集成验证，故作为待维护者审阅的候选提交。离线通过证明所测分支的行为；完整视觉交接与持续播放须在真实客户端验证。与官方客户端共用 renderer/GPU，宿主不可被强制终止来恢复壁纸。

## 构建与媒体

Git 中提交可审查源码与 46 个媒体文件的哈希清单（约 374 MB），完整媒体与二进制通过贡献者 fork 的候选 release ZIP 提供。保持原仓库 v1.0.0 的下载与校验入口。使用完整候选包作 MediaRoot，可从本分支运行 Build.ps1 重新构建。未为上游代码或媒体新增许可证，保留已有来源与字体许可说明。

完整候选包与校验文件：[community-v34-review-20261123](https://github.com/he2542/codex-startup-animation/releases/tag/community-v34-review-20261123)。

本次整理还补齐了三个窗口测试编译脚本缺少 TaskbarIcon.cs 的依赖，避免累计任务栏改动后测试无法编译。

完整包同时纳入任务栏所需的本地图标文件，避免源码构建后的任务栏适配因缺少图标而跳过。

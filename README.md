# Codex 龙娘启动动画与动态壁纸 · 社区 R13

**当前完整包为 R13，用户反馈已确认运行成功，现按确认过的 ZIP 原字节发布。** 原发布页及下载直链保持不变；链接和文件名中的 `r5` 用于保留原地址，实际程序版本为 **34.13.0.0**。

[下载完整 Windows x64 包](https://github.com/HanwenZhang-George/codex-startup-animation/releases/download/community-v34-r5/CodexDragon-V34-Portable-Windows-x64-r5.zip) · [固定发布页](https://github.com/HanwenZhang-George/codex-startup-animation/releases/tag/community-v34-r5) · [故障排查与反馈教程](TROUBLESHOOTING.md) · [验证记录](VERIFICATION.txt)

> 请下载上述完整包。GitHub 的 **Code → Download ZIP** 和 **Source code (zip)** 是源码下载，不是包含全部视频的可运行包。

## 第一次使用

1. 完整解压 ZIP 到一个新的本地文件夹，例如 `D:\CodexDragon-R13`。不要在 ZIP 内运行，不要混入旧版文件。
2. 确认当前用户安装的官方 Microsoft Store Codex 能正常打开并登录。
3. 保存工作，正常完整退出 Codex。仅关闭窗口但仍留在后台，可能不算退出。
4. 双击解压目录最外层 **`Launch.exe`**。通常无需管理员权限，不必先安装或运行开发测试。
5. 检查启动动画、流泪与波纹交接，以及进入 Codex 后的壁纸、输入框透明度、点击与输入。

首次壁纸固定使用常态视频；以后每次出现按 **75% 常态 / 25% 粒子视频**选择，两次出现之间保留 **10–15 秒随机间隔**。人物暂时离开属于正常设计；25% 不表示每四次必有一次粒子视频。

启动等待明显异常时，**按 ESC 跳过并立即收集报告，不必等几分钟**。跳过保留壁纸请求，但资源、传输或解码问题仍可能影响显示。

普通官方入口启动的 Codex 没有本包的壁纸宿主，不能安全地事后补接。请正常退出后从本包 `Launch.exe` 启动。由同一份包启动后，再次点击同一个 `Launch.exe` 可重放启动流程并回到已有窗口；不要同时连点多个副本。

## 内容与本次修复

完整包包含启动动画、流泪转场、常态和粒子壁纸、字体、源码、四个程序组件、完整性清单和轻量诊断工具。全部 **46 项媒体/字体/图像素材，其中 6 个 MP4**均保留原字节，未重新编码或降低画质、帧率，原有透明度参数、概率和出现间隔保持不变。**不含 CC Switch**，也不需要视频生成 API Key。

R13 及本次一并发布的 R11/R12 修复包括：

- 冷启动时页面查询响应较慢，原来的短等待可能过早失败。R13 仅在只读查询响应超时后，将下一次响应预算从 2 秒调整为 4 秒；总预算最多 30 秒，连接动作只发一次，并受最多 12 秒和剩余预算约束。不会无限等待或重复提交修改操作。
- 已经确认属于本次启动的隐藏主窗口，不再仅因随后出现隐藏辅助窗口而失去失败恢复资格；无可信所有权、身份或位置不符时仍不猜选窗口。
- 补齐一种经过严格检查的官方页面结构，并在节点重新挂载或属性变化后重新核验透明样式；未知或冲突结构仍保守拒绝。
- 可选官方快捷方式绑定从当前注册清单读取入口；识别期间遇到注册版本切换时最多重新发现一次，继续验证签名和路径。重复启动可有界恢复选定时已最小化的同一窗口，保留之后的用户操作。
- 诊断区分首次失败、后续成功、连接丢失和进程退出。正常退出码不能单独证明用户主动退出；证据不足会明确说明。

R10 的自动版本识别及 R9 的内存暂停/恢复策略继续保留。普通官方版本号与安装路径更新无需手改配置；包身份、签名主体、底层框架、通信协议或页面结构的大改仍可能需要适配，**不承诺任意未来版本都免维护**。

## 环境与兼容范围

- Windows 10/11 **x64**、.NET Framework **4.8+**、系统 Windows PowerShell **5.1**。
- 当前用户安装的官方 Microsoft Store Codex x64；壁纸接入支持已核验的 **Electron 私有 CDP 管道**。
- 正常 Windows 媒体组件和视频解码能力；Windows N 版需要相应媒体组件。
- 本地可写 NTFS 文件夹，不使用网络共享、目录链接或不完整的解压副本。

尚未实现 Tauri/Wry/WebView2 壁纸传输。WebView2 文件等线索本身不能确定客户端运行时，也不表示已支持该后端。不要关闭身份校验或开启公开调试端口来绕过问题。

## 出问题时：一键生成报告

1. 出现问题后，到**实际运行这份程序的目录**，打开 `Diagnostics`。
2. 双击 **`One-Click-Diagnostics.cmd`**。不用先退出 Codex，不用再次启动或等待壁纸。
3. 成功后打开 `Diagnostics\Reports`：**`latest-analysis.txt`** 是中文分析；把 **`latest-report.zip`** 发给维护者。
4. 如果准备重试启动，先把本次 ZIP 另存；工具只保留最新一份，不堆积报告。最好在故障出现后、再次关闭或启动前收集。

一键诊断默认核对**全部正式文件 SHA-256**，然后收集有界的日志摘要和数值环境证据；文件核验失败也会尽量生成报告。它不启动、关闭或等待应用，不联网、不安装依赖、不做 GPU/整机压力测试。完整哈希会读取视频文件，慢磁盘或安全软件检查可能延长耗时。需要停止可按 Ctrl+C 或关闭诊断窗口。

只想核对文件可运行 `Diagnostics\Check-All-Files.cmd`，一般反馈直接运行一键诊断即可。工具未完成时，旧 ZIP 不代表本次结果，请提供错误窗口截图。具体测试顺序、快速模式和反馈模板见 [详细教程](TROUBLESHOOTING.md)。

反馈任选一种方式：

- [GitHub Issue](https://github.com/HanwenZhang-George/codex-startup-animation/issues/new)：附 `latest-report.zip` 与故障现象。
- 邮件 [hanwen.3.zhang@kcl.ac.uk](mailto:hanwen.3.zhang@kcl.ac.uk) 或 [zhanghw0511@gmail.com](mailto:zhanghw0511@gmail.com)。

不要发送整个安装目录、原始日志、API Key、私钥、账号配置、浏览器 Cookie 或聊天记录。GitHub Issue 是公开的；截图和文字也请先检查私人信息。

## 可选：绑定原来的官方桌面图标

先确认便携运行正常，正常退出 Codex，再双击 **`Install-and-Bind.cmd`**。它将独立副本安装到 `%LOCALAPPDATA%\Programs\CodexDragonPublic`，尝试绑定当前用户桌面唯一可信的官方快捷方式，保留原名称和图标，不修改官方程序。

安装目录已存在、快捷方式不唯一或不支持时，工具会停止或保留原入口并提示。安装后在**安装目录**的 `Diagnostics` 收集报告。需要还原时运行安装目录中的 `Uninstall.ps1`，不要直接删除仍被快捷方式引用的目录。详细操作见 [安装与还原](TROUBLESHOOTING.md#可选安装后的报告位置与还原)。

## 下载核验

公开 ZIP 与用户确认通过的 R13 ZIP 字节完全一致，没有再次编译、打包或修改诊断文件。包内 `test`、候选版、“尚未发布”等文字保留原样以保持哈希，当前发布状态以本页和发布页为准。

```text
公开文件名：CodexDragon-V34-Portable-Windows-x64-r5.zip
实际内容：V34-r13
程序文件版本：34.13.0.0
发布清单版本：34.4-reliability-r13-test
文件字节数：369377273
SHA-256：1D817B93F879AB634A4F96F56665E751B018092A994087E13EF16B801540B884
```

本次核验覆盖 185 项清单文件、186 项 ZIP 成员及 114 份源码；原 46 项素材包含的 6 段视频保持不变。精确 ZIP 提取副本通过目标发现、原生窗口、生命周期、历史诊断和发布完整性测试；具体范围见 [VERIFICATION.txt](VERIFICATION.txt)。这些测试及用户成功反馈不代表覆盖所有硬件、驱动或未来版本。

R13 源码标记为 `community-v34-r13`；原 `community-v34-r5` 发布页和完整包 URL 继续使用。[分支贡献与 Pull Request 教程](CONTRIBUTING.md)。

此版本不是 OpenAI 官方发行版。上游：[mytjxs/codex-startup-animation](https://github.com/mytjxs/codex-startup-animation)；社区修改与打包：HanwenZhang-George。保留上游归属与字体许可，不为第三方代码或媒体自行新增授权。

---

## 上游 v1.0.0 历史说明（仅保留原文）

以下为原作者早期版本的记录，**不是当前 R13 的下载或使用教程**。旧入口、旧文件名和旧校验值只适用于历史版本；当前使用以上社区 R13 下载链接。

<details>
<summary>展开上游历史原文</summary>

# codex启动动画

Windows Codex 龙娘启动动画安装包。横向 **16:9、2560 × 1440、60 帧/秒、8 秒**，从发光线条勾勒人物到龙娘显现、缓缓睁眼，随后打开 Codex 桌面应用。

标题为 **Chatgpt**，欢迎语为 **欢迎主人回家 / WELCOME HOME, MASTER**。

## 下载

[下载 v1.0.0 完整安装包](https://github.com/mytjxs/codex-startup-animation/releases/download/v1.0.0/codex-startup-animation-v1.0.0-win-x64.zip) · [版本下载页](https://github.com/mytjxs/codex-startup-animation/releases/tag/v1.0.0)

## 使用

1. 下载 ZIP，完整解压到固定文件夹。
2. 双击 `Start-Codex-Dragon.exe`，播放启动动画后进入 Codex。
3. 为该 EXE 创建桌面快捷方式，以后从此入口打开。
4. 只想查看动画时，双击 `Preview-Dragon.cmd`。

安装包解压即用，无需安装 Python、Node.js 或 FFmpeg。需要 Windows 10/11、.NET Framework 4.x，以及已安装的 Codex 桌面应用。保留 EXE 同目录的配置、素材和视频文件夹。

按 Esc、Enter 或空格可以跳过动画；点击关闭按钮取消本次启动。

如需调整目标应用，修改 `launcher.config.xml`。默认应用标识为 `OpenAI.Codex_2p2nqsd0c76g0!App`；本机标识不同可修改 `appUserModelId`，或填写 `executablePath`。

此启动器提供额外启动入口。从 Codex 原有图标打开时仍直接进入应用。

## 包内容与校验

完整安装包保持原样，内部包含启动器、配置、视频、角色素材、说明及源码。详细说明在压缩包中的 `README.md`。

仓库单独提供使用说明与校验信息。下载页中的 `SHA256SUMS.txt` 用于核对 ZIP 文件。

安装包 SHA-256：

```text
682928d2e6ce85bfb3d83acadb4e85da9efe4948a593a63e4dc2e605a08ff524
```

## 社区交流

社区交流：[LINUX DO](https://linux.do/)

</details>

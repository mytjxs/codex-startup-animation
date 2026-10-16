> **Community V34 contribution:** a more extensively customized startup animation and dynamic wallpaper is available for review in [`community-v34/`](community-v34/README.txt). The original v1.0.0 instructions below are preserved.
>
> The V34 ready-to-run distribution is **`CodexDragon-V34-Portable-Windows-x64-r2.zip`**, supplied as a Release asset with both wallpaper videos, all character assets, font, source and executables. GitHub's **Code → Download ZIP** is source only. Read the requirements and verification limits in `community-v34/README.txt` before installing.
>
> The portable version starts through **`Launch.exe`**; it does not replace the official Codex icon or contain the author's local account/configuration. Contributor: **HanwenZhang-George**. No new license is assigned to upstream code or media.

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

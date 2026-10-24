# 反馈与贡献修改

使用问题请先阅读 [诊断教程](TROUBLESHOOTING.md)，再到 [Issues](https://github.com/HanwenZhang-George/codex-startup-animation/issues) 提交现象和 `latest-report.zip`。不要提交整个安装目录、账号配置、原始日志或密钥。

## 提交代码修改

1. 在 GitHub 点击 **Fork**，创建你自己的仓库副本。
2. 在自己的副本从最新 `main` 新建分支，例如 `fix/wallpaper-recovery`。
3. 修改相关源码。当前社区实现位于 `community-v34/`；运行与改动相关的隔离测试，说明实际测试范围。不要改变素材画质来隐藏性能问题。
4. 提交并推送到你自己的分支，然后点击 **Contribute → Open pull request**。
5. 目标仓库选择 `HanwenZhang-George/codex-startup-animation`，目标分支选择 `main`。描述复现步骤、修改原因、验证结果，以及是否改变启动或资源保护行为。
6. 等待维护者审查后合并。无需维护者事先授予仓库写权限，公开仓库可通过 Fork 和 PR 参与；提交 PR 不会自动修改正式下载。

请勿提交 API Key、密码、Cookie、真实用户目录、聊天记录、生成日志、个人配置或未经核验的构建产物。视频和完整运行包通过 Release 分发；不要把大文件或私人测试报告混入源码提交。

## 发布约定

稳定发布页和已有下载直链保留 `community-v34-r5` 与 `r5.zip` 名称，当前内容为已测 R13；不能仅凭旧文件名判断实际版本，应核对发布说明和 SHA-256。社区源码同时更新 `main` 与 `community-portable-v34`；后者关联原上游 PR。Release 附件只发布测试验收后的原始 ZIP，文档更新不应静默重打包。

# E²-Day 2.1.9 · 向着朝阳出发

每次启动日常 Windows App，播放约 2.2 秒的品牌开场：天空蓝底板展开、弯道向朝阳延伸、太阳升起，E 与上角标 ² 合拢，落定为原版 logo 后进入应用。默认静音。

- 第一帧起点击动画窗口任意位置立即跳过，支持触屏及 Esc、Enter、空格。跳过输入由开场接收，不触发后面的登录或任务按钮。
- 本机账号在后台同时读取；随后显示登录页或记住账号的今日清单。读取尚未完成时，显示静态 logo 和“正在打开…”。
- 遵循系统客户端区域动画设置，减少动画时以约 0.2 秒静态品牌画面替代；自动完成、跳过或关闭均停止帧计时器。
- 继续兼容现有账号、任务、图片、历史、备份和文件交换，保留粉紫玻璃界面和平滑滚动。家教版与手机端保持原版本。

![品牌定格（隔离分镜）](https://raw.githubusercontent.com/yilaxin/E2-Day/v2.1.9/docs/images/startup-opening.png)

## 下载与更新

| Windows 架构 | 自包含程序包 |
| --- | --- |
| Intel / AMD x64 | [E2-Day-v2.1.9-win-x64.zip](https://github.com/yilaxin/E2-Day/releases/download/v2.1.9/E2-Day-v2.1.9-win-x64.zip) |
| Windows ARM64 | [E2-Day-v2.1.9-win-arm64.zip](https://github.com/yilaxin/E2-Day/releases/download/v2.1.9/E2-Day-v2.1.9-win-arm64.zip) |

完整解压到新文件夹后运行 DailyRecord.exe，保留旁边的 DLL、Data、Mobile 和图标文件。旧数据与程序目录分开保存。保留历史发布与标签；程序未做数字签名，提供 SHA256SUMS.txt。版权及许可继续使用 LICENSE。

## 验证

发布前运行核心与手机规则、文件交换、离线启动回归，并分别运行 ARM64 和 x64 自包含程序的 WPF 验证。开场覆盖自动结束、首帧点击、图形／边缘／留白点击、输入消费、Esc／Enter／空格、重复启动、减少动画和关闭后的计时器及帧停止。

分镜与页面截图仅使用隔离示例数据。检查程序版本 2.1.9.0、两种 PE 架构、包内必要文件及 SHA-256；发布后验证 GitHub 附件摘要及公开下载。x64 在 Windows ARM64 主机兼容环境执行。不上传个人数据库、图片、备份、日志、凭据及构建缓存。

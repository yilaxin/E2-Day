# E²-Day 家教版 1.0.2 · 下载与使用图文教程

本版完善朋友第一次下载使用的交付材料，保留既有家教功能与本机记录。

## 下载

- [普通 Intel / AMD 电脑：x64 程序包](https://github.com/yilaxin/E2-Day/releases/download/tutor-v1.0.2/E2-Day-Tutor-v1.0.2-win-x64.zip)
- [高通等 ARM 电脑：ARM64 程序包](https://github.com/yilaxin/E2-Day/releases/download/tutor-v1.0.2/E2-Day-Tutor-v1.0.2-win-arm64.zip)
- [七页图文 PDF：下载、首次启动与使用教程](https://github.com/yilaxin/E2-Day/releases/download/tutor-v1.0.2/E2-Day-Tutor-Guide-v1.0.2.pdf)

完整解压后先阅读“先读我.txt”或“安装与使用教程.pdf”，再双击 **E2Day.Tutor.exe**。无需另装 .NET。不要在 ZIP 内直接运行，也不要只复制一个 exe。需要桌面图标时为解压后的 exe 创建快捷方式。

## 首次运行需要了解

本版没有数字签名，Windows 仍可能显示“Windows 已保护你的电脑”。请先确认下载来源为本仓库正式家教版附件；SHA256SUMS.txt 可用于核对文件。只有确认来源且信任程序时，才考虑在“更多信息”中选择“仍要运行”。如果没有此选项、设备策略或 Smart App Control 拦截，联系设备管理员或作者；若出现具体恶意软件提示，停止运行并反馈。不要关闭系统安全功能。

未签名信誉提醒无法通过普通重打包消除。签名和系统信誉是独立于程序包完整性的问题，说明见 [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)。

## 包与使用验证

重新从公开链接下载两种包，核对完整 SHA-256；在全新目录解压，检查版本、运行依赖、PE 架构、离线教程与必要文件；用隔离数据目录运行真实首次账号窗口，再执行核心及 WPF 规则验证。验证主机为 Windows ARM64，x64 程序通过系统兼容环境运行，未逐台验证独立 Intel / AMD 设备或朋友的电脑。

PDF 含架构选择、下载解压、Windows 安全提醒、本机账号、第一节课、图文反馈、收入统计、备份更新、换电脑与常见问题。截图均为隔离示例数据，中文字体已嵌入，链接可点击。程序包与独立 PDF 均列入 SHA256SUMS.txt。

更新前导出备份、关闭旧版，完整解压新版本。继续使用原家教版本机账号与数据。日常版入口与旧发布保留。许可继续保留版权并允许下载运行正式发布 App。

# E²-Day 家教版 1.0.0 · 记录每一堂课

基于 Eday 的日历、图文备注与本机保存能力，新增独立家教版。日常版与家教版分别运行、分别保存数据。

## 下载与开始

- Intel / AMD Windows 电脑：[x64 自包含程序包](https://github.com/yilaxin/E2-Day/releases/download/tutor-v1.0.0/E2-Day-Tutor-v1.0.0-win-x64.zip)
- ARM64 Windows 电脑：[ARM64 自包含程序包](https://github.com/yilaxin/E2-Day/releases/download/tutor-v1.0.0/E2-Day-Tutor-v1.0.0-win-arm64.zip)

完整解压后运行 **E2Day.Tutor.exe**。无需另装 .NET。首次创建本机账号并保存恢复码，然后添加学生与课程。程序包未作数字签名，附件 SHA256SUMS.txt 提供校验值。

## 首版功能

- 今日课程、待确认课程、待补反馈、近期安排与备课待办。
- 月历与全年汇总，支持单次、每周重复、临时改期、修改单次或这次及以后。
- 学生档案、默认科目和时薪、学习特点与结课归档。
- 按实际授课日汇总课时与收入，保留每节课时薪；支持月份、自选范围、学生筛选与趋势。
- 教学内容、掌握情况、问题、方法与效果、作业计划、内部备注和图片；可参考历史方法。
- 一键复制家长反馈，排除内部备注、图片与收入，交由老师自行粘贴发送。
- 独立本机账号与数据库、自动保存、30 天自动备份、含图导出和恢复。

![授课日历](https://raw.githubusercontent.com/yilaxin/E2-Day/tutor-v1.0.0/docs/images/tutor-calendar.png)

![收入统计](https://raw.githubusercontent.com/yilaxin/E2-Day/tutor-v1.0.0/docs/images/tutor-income.png)

## 独立性与范围

家教版默认数据目录 `%LOCALAPPDATA%\E2DayTutor`，日常版仍为 `%LOCALAPPDATA%\DailyRecord`。不迁移个人日常记录，可同时使用。家教版采用独立备份格式，与日常版和手机交换文件互不兼容；记录保存在本机，没有云同步。密码登录不等于数据库加密。

第一版面向一对一授课，授课收入表示课程产生的收入，不表示已收款。尚不包括多人班课、课包、欠费、手机端、系统通知或云服务。

## 验证与许可

家教版核心行为与两个架构的发布程序 WPF 验证、日常版回归、包内版本／架构／必要文件与 SHA-256 校验的结果见[验证记录](https://github.com/yilaxin/E2-Day/blob/tutor-v1.0.0/验证记录.md)。x64 在 Windows ARM64 的兼容环境运行验证。截图只使用隔离示例数据，正式首次启动为空白。

保留版权，允许下载和运行正式发布 App，不改为通用开源许可。使用步骤见[家教版说明](https://github.com/yilaxin/E2-Day/blob/tutor-v1.0.0/docs/TUTOR-GUIDE.md)。

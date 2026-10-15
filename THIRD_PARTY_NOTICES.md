# 第三方组件与数据来源

## .NET / WPF 与 SQLite

- .NET / WPF：Microsoft / .NET Foundation，MIT 及其自身随附声明。来源：https://github.com/dotnet/wpf 、https://github.com/dotnet/runtime 。
- Microsoft.Data.Sqlite 10.0.11：Microsoft，MIT。来源：https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.11 。
- SQLitePCLRaw 2.1.12：Eric Sink 等，Apache-2.0。来源：https://github.com/ericsink/SQLitePCL.raw 。
- SQLite：SQLite 作者提供的公共领域软件。来源：https://www.sqlite.org/copyright.html 。
- System.Memory 4.5.3：Microsoft，MIT。来源：https://www.nuget.org/packages/System.Memory/4.5.3 。

## 中国节假日数据

2024、2025、2026 年 JSON 数据来自 `NateScarlet/holiday-cn`，采用 MIT 许可证。原始许可证完整保存在程序的 `Data/Holidays/LICENSE` 和源码的 `data/holidays/LICENSE` 中。

来源：https://github.com/NateScarlet/holiday-cn 。年度数据中的 `papers` 字段保留政府原文链接。

农历转换使用 .NET 的 `ChineseLunisolarCalendar`，按普通月份处理年度重复，支持范围由该日历实现决定。

## RSS 阅读来源

程序仅在个人本地阅读场景展示来源提供的标题、简介、时间和原文链接，各资讯来源的内容权利由其原作者保留。

- 微软 Source Asia：https://news.microsoft.com/source/asia/feed/?lang=zh-hans
- Google 台湾官方博客：https://blog.google/intl/zh-tw/rss/
- OpenAI：https://openai.com/news/rss.xml
- Google DeepMind：https://deepmind.google/blog/rss.xml
- Microsoft Research：https://www.microsoft.com/en-us/research/blog/feed/

程序不抓取文章全文，不调用第三方自动转译服务，不将个人任务、事件或备份发送给资讯来源。

## 手机资源

手机页面为项目自带静态资源，不使用远程 CDN。农历重复日期表由 .NET ChineseLunisolarCalendar 生成，图标沿用本项目 logo。独立手机版本使用浏览器的 IndexedDB、Web Crypto 与 Service Worker；应用不再包含 ASP.NET Core / Kestrel 局域网连接服务。

## 鸿蒙原生工程

工程骨架通过华为官方 `@deveco/deveco-cli` 1.3.5 模板生成；保留模板代码内 Huawei Device Co., Ltd. 的 Apache-2.0 声明。Apache-2.0 许可正文见 `src/DailyRecord.Harmony/LICENSE-TEMPLATE.txt`。开发工具本身及其依赖不打包为应用源码或程序附件。原生界面、任务与文件交换代码沿用本项目许可；图标和农历表沿用项目已有资源。

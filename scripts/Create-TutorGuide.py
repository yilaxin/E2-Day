"""Generate the Chinese, illustrated guide shipped with E²-Day Tutor."""
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import escape
import shutil
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, white
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).resolve().parents[1]
VERSION = ElementTree.parse(ROOT / 'src/DailyRecord.Tutor/DailyRecord.Tutor.csproj').findtext('./PropertyGroup/Version')
RELEASE = f'https://github.com/yilaxin/E2-Day/releases/tag/tutor-v{VERSION}'
BASE = f'https://github.com/yilaxin/E2-Day/releases/download/tutor-v{VERSION}'
OUT = ROOT / 'output/pdf/E2-Day-Tutor-Guide.pdf'
OUT.parent.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('CN', 'C:/Windows/Fonts/Deng.ttf'))
pdfmetrics.registerFont(TTFont('CN-Bold', 'C:/Windows/Fonts/Dengb.ttf'))
pdfmetrics.registerFontFamily('CN', normal='CN', bold='CN-Bold', italic='CN', boldItalic='CN-Bold')
W, H = 595.276, 841.89
M, CW = 42, W - 84
INK, BLUE, MUTED = map(HexColor, ['#20384D', '#216FA6', '#5C7184'])
LINE, PALE, GOLD = map(HexColor, ['#DCE7F1', '#EFF6FD', '#FFF6DC'])
c = canvas.Canvas(str(OUT), pagesize=(W, H), pageCompression=1)
c.setTitle(f'E²-Day 家教版 {VERSION} - 下载、安装与使用教程')
c.setAuthor('E²-Day')
c.setSubject('面向 Windows 用户的家教版下载、首次启动、授课、反馈、统计及备份教程')
y = 0

def para(text, x=M, width=CW, size=10.7, color=INK, bold=False, gap=8):
    global y
    style = ParagraphStyle('body', fontName='CN-Bold' if bold else 'CN', fontSize=size,
                           leading=size * 1.62, textColor=color, wordWrap='CJK', spaceAfter=0,
                           allowWidows=0, allowOrphans=0)
    p = Paragraph(text, style)
    _, height = p.wrap(width, 2000)
    if y-height < 58:
        raise RuntimeError(f'Page overflow at: {text[:45]} ({y-height:.1f})')
    p.drawOn(c, x, y-height)
    y -= height + gap
    return height

def section(title):
    global y
    y -= 6
    para(title, size=14, color=BLUE, bold=True, gap=8)

def box(text, gold=False):
    global y
    p = Paragraph(text, ParagraphStyle('box', fontName='CN', fontSize=10.5, leading=17,
                                     textColor=INK, wordWrap='CJK'))
    _, h = p.wrap(CW-28, 2000)
    if y-h-25 < 58: raise RuntimeError('Callout overflow')
    c.setFillColor(GOLD if gold else PALE)
    c.roundRect(M, y-h-24, CW, h+24, 9, fill=1, stroke=0)
    p.drawOn(c, M+14, y-h-12)
    y -= h+36

def shot(name, max_height, caption):
    global y
    path = ROOT / 'docs/images' / name
    img = ImageReader(str(path)); iw, ih = img.getSize()
    scale = min(CW/iw, max_height/ih); dw, dh = iw*scale, ih*scale
    if y-dh-30 < 58: raise RuntimeError('Screenshot overflow: '+name)
    x = M+(CW-dw)/2
    c.setStrokeColor(LINE); c.setFillColor(white)
    c.roundRect(x-1, y-dh-1, dw+2, dh+2, 7, fill=1, stroke=1)
    c.drawImage(img, x, y-dh, dw, dh, mask='auto')
    y -= dh+9
    para(caption, size=8.5, color=MUTED, gap=12)

def link(label, url):
    para(f'<link href="{escape(url)}" color="#216FA6"><u>{escape(label)}</u></link>', size=10.5)

def page(number, title, subtitle):
    global y
    if number > 1: c.showPage()
    c.setFillColor(BLUE); c.roundRect(M, H-54, 102, 22, 6, fill=1, stroke=0)
    c.setFillColor(white); c.setFont('CN-Bold', 9); c.drawString(M+11, H-46, 'E²-Day  家教版')
    c.setFillColor(MUTED); c.setFont('CN', 9); c.drawRightString(W-M, H-46, 'Windows 电脑使用教程')
    y = H-83
    para(title, size=24, bold=True, gap=8)
    para(subtitle, size=10.5, color=MUTED, gap=17)
    c.setStrokeColor(LINE); c.line(M, 47, W-M, 47)
    c.setFont('CN', 8); c.setFillColor(MUTED)
    c.drawString(M, 31, f'适用家教版 {VERSION}  ·  2026-10-18')
    c.drawRightString(W-M, 31, f'{number} / 7')
    c.bookmarkPage(f'page{number}')
    c.addOutlineEntry(title, f'page{number}', level=0)

page(1, '下载后，先完整解压', '从 GitHub 到第一次打开，按这三个步骤操作。')
box('这是独立的<b>家教版</b>。本教程使用 Windows 11 电脑演示。程序为免安装版本，所需运行依赖已包含在包内；首次打开前需要把 ZIP 完整解压。')
section('01  打开家教版发布页')
link(f'点击进入：E²-Day 家教版 {VERSION}', RELEASE)
para(f'在页面的 <b>Assets（附件）</b> 中选 Windows 程序包。请认准 <b>tutor-v{VERSION}</b>；仓库的 Latest 入口可能对应日常版。不要下载 Source code 或鸿蒙 HAP。')
section('02  选对你的电脑架构')
para('打开 Windows「设置 → 系统 → 系统信息（或“关于”）→ 系统类型」，查看处理器类型。')
box('<b>Intel / AMD、基于 x64 的处理器：</b>选择 win-x64.zip。<br/><b>Snapdragon、高通、基于 ARM 的处理器：</b>选择 win-arm64.zip。<br/>家教版不提供 macOS、Linux 或 32 位 Windows 程序包。')
link('下载 x64 版（普通 Intel / AMD 电脑）', f'{BASE}/E2-Day-Tutor-v{VERSION}-win-x64.zip')
link('下载 ARM64 版（高通等 ARM 电脑）', f'{BASE}/E2-Day-Tutor-v{VERSION}-win-arm64.zip')
section('03  完整解压，再打开程序')
para('等待下载完成。右键 ZIP →「全部解压缩」，选择自己能写入的固定文件夹，例如“文档”中的 E2-Day-Tutor。进入解压后的文件夹，双击 <b>E2Day.Tutor.exe</b>。')
box('保留旁边所有 DLL、Data、images 等文件。不要在 ZIP 压缩包里直接运行，也不要只把 exe 单独拷到桌面。桌面快捷方式的创建方法见第 6 页。', gold=True)
para('同一文件夹内的“先读我.txt”和这份 PDF 可以离线阅读。遇到 Windows 安全提示，继续看下一页。', size=9.5, color=MUTED)

page(2, '首次启动与本机账号', '遇到提示先看清文字，再决定下一步。')
section('出现“Windows 已保护你的电脑”')
para('本版没有数字签名，下载次数也较少，可能触发 Windows 的应用信誉提醒。这类提醒本身不等于发现病毒；也不能据此认定文件安全。请先核对来源和文件。')
box('仅在你确认来自 <b>github.com/yilaxin/E2-Day</b> 的家教版正式附件、文件名正确，并愿意信任该来源时：点击「更多信息」，核对应用名为 <b>E2Day.Tutor.exe</b>，再选择「仍要运行」。发布页提供 SHA256SUMS.txt，可供进一步核对。', gold=True)
para('如果没有“仍要运行”，或提示 Smart App Control、管理员策略禁止运行，先联系作者或设备管理员。若提示具体病毒/恶意软件名称，停止运行并把提示发给作者。无需关闭杀毒软件，也不要为此关闭系统安全功能。', size=10)
section('第一次使用：创建自己的账号')
para('1. 在登录窗口选择「创建独立账号」。<br/>2. 填写账号名、密码，并再次输入相同密码。账号名为 2-40 个字符；密码为 8-128 个字符。<br/>3. 点击「继续」，按提示保存离线恢复码。<br/>4. 进入家教版后，从「新增学生」开始。')
box('这是这台电脑上的本机账号，不是 GitHub 或云端账号。忘记密码时可在登录窗口选择“使用离线恢复码”。恢复码用于重置访问密码，课程数据仍需从原电脑或备份保留。')
section('以后打开')
para('可以勾选“记住这台电脑”。如果已经打开一个家教版窗口，再双击程序可能提示“家教版已经打开”，请从任务栏切回已有窗口。每个本机账号的学生和课程分别保存。')
para('数据在本机保存，账号密码登录不等于数据库或备份已加密。请自行保管电脑、恢复码和备份。', size=9.5, color=MUTED)

page(3, '记下你的第一节课', '先添加学生，再安排或补录一节课程。')
section('01  新增学生')
para('点击「学生档案 → 新增学生」，填写姓名或昵称、年级、默认科目和默认时薪；在“学习特点与长期目标”中写下需要长期关注的情况。以后选中学生，录课时会自动带入科目与时薪。')
shot('tutor-student.png', 228, '学生档案示例：汇总授课次数、时长、收入，并保留历史反馈。截图为虚构示例。')
section('02  添加课程')
para('在「今日授课」点击“添加课程”，或在「授课日历」选中日期，点击蓝色的“在这一天添加课程”。选择学生、日期、开始时间、授课时长与本节时薪。')
box('<b>待上课：</b>只作安排，显示预计金额，不计收入。<br/><b>已授课：</b>记录实际情况，并计入课时与授课收入。<br/><b>已取消：</b>保留记录，不计收入。')
para('例：上课 1.5 小时，时薪 150 元，确认“已授课”后，本节收入为 225 元。时长用小时填写，如 1、1.5、2；也可以补录以前的课程。')
para('补录时把日期改成实际授课日。9 月 30 日的课即使在 10 月补录，收入仍归到 9 月 30 日。每节课保留当时的时薪，修改学生默认时薪不会改写旧课。', size=9.5, color=MUTED)

page(4, '课后反馈，留给未来的自己', '一份记录，兼顾家长反馈与自己的教学积累。')
para('课程保存后，打开“查看 / 编辑反馈”，切到「课程反馈」：填写教学内容、掌握情况、存在问题、教学方法与效果、作业与下次计划。可以先确认授课，再从首页“待补充反馈”中补写。')
shot('tutor-editor.png', 310, '“课程反馈”页签。下方继续滚动，可填写内部备注并添加图片。截图为虚构示例。')
section('图片与分享')
para('可以上传图片，或复制截图后点击“粘贴图片”。每节课最多 8 张，单张原图不超过 20 MB；点击缩略图可放大。图片在点击“保存”后生效。')
box('“复制给家长”只复制反馈文字与课程基本信息，不包含收入、自己的内部备注或图片。然后到聊天窗口自行粘贴发送；应用不会自动给家长发消息。')
section('找到以前有效的办法')
para('在「教学记录」按学生、状态或关键词搜索。在课程编辑器的「参考历史」中搜索问题或方法，可以引用旧课的教学内容、方法和下次计划，再针对当前学生修改。不会自动复制旧学生的掌握情况。')

page(5, '排课与收入，一眼看清', '安排中的课与已经上完的课，分开处理。')
section('日历与重复课程')
para('点击日历日期查看当天课程。新增待上课程时，可勾选“每周同一天、同一时间重复排课”。临时改期时选择“仅修改这一次”；调整固定安排时选择“这一次及以后”。已授课历史继续保留。')
para('停止后续课程：编辑一节待上的重复课，选择“这一次及以后”，把状态改为“已取消”并保存。将学生设为“已结课”只做归档，不会自动取消既有排课。')
section('查看收入与课时')
para('进入「收入统计」，选择今天、本周、本月、今年或自选日期，并可筛选某位学生。只有已授课课程计入收入。超过 62 天的范围按月展示趋势。')
shot('tutor-income.png', 260, '收入统计示例：按日期及学生筛选，查看次数、课时、收入与趋势。')
box('<b>授课收入不等于已经收到的钱。</b>本版不记录实际到账、欠费、课包或退款。每节课按“分钟数 × 时薪 ÷ 60”计算并四舍五入到分，再累加汇总。', gold=True)
para('过去未确认的课程不会自动挪到今天，也不会自动计费。可在首页查看近期摘要，或到「教学记录 → 待确认课程」逐节补录、改期或取消。')

page(6, '桌面入口、备份与换电脑', '用快捷方式打开，把备份保存在程序文件夹之外。')
section('在自己的电脑上创建桌面快捷方式')
para('1. 在固定的解压文件夹中找到 E2Day.Tutor.exe。<br/>2. 右键程序 →「显示更多选项」（如有）→「发送到 → 桌面快捷方式」。<br/>3. 将快捷方式改名为“E²-Day Teacher”。<br/>4. 以后双击桌面图标；不要只移动 exe，也不要删除它指向的程序文件夹。')
section('定期导出备份')
para('进入「设置与备份 → 导出家教版备份」，把 JSON 文件存到自己能找到的位置，并额外保存一份到移动硬盘等其他位置。备份包含学生、课程、重复规则、图文反馈和备课待办。')
box('应用运行当天自动更新一份备份，保留最近 30 份。电脑丢失或硬盘损坏时，同一硬盘上的自动备份也可能丢失；手动保存到其他位置的备份更有用。')
section('换电脑或恢复记录')
para('在新电脑下载并解压家教版 → 创建本机账号并保存新恢复码 →「设置与备份 → 从备份恢复」→ 选择之前导出的家教版 JSON → 核对预览后确认。')
para('恢复会替换当前账号的教学记录，确认前会另存当前记录的恢复前备份。教学备份不包含账号密码或恢复码；日常版和手机交换文件不能作为家教版备份导入。')
section('更新与卸载')
para('更新前先导出备份并关闭旧版。把新版完整解压到新的固定文件夹，运行新 exe，沿用这台电脑原有家教版本机账号与记录；重新创建或更新桌面快捷方式，确认它指向新版。')
para('删除程序文件夹只移除程序，不自动删除个人记录。家教版数据根目录为 %LOCALAPPDATA%\\E2DayTutor。准备清理个人数据前，先保存并确认可用的备份。', size=9.5, color=MUTED)

page(7, '卡住时，按这里排查', '把提示文字发给作者，比反复点击更容易解决。')
items = [
('ZIP 提示损坏、无法解压', '先删除未下载完整的 ZIP，再从家教版发布页重新下载。需要时核对发布页 SHA256SUMS.txt；校验不一致时不要运行。'),
('提示“此应用无法在你的电脑上运行”', '重新检查系统类型并选择 x64 / ARM64。不要把 ARM64 包发给普通 Intel / AMD 电脑使用。'),
('提示缺 DLL、缺 .NET，或打开即退出', '确认使用“全部解压缩”，exe 与旁边依赖文件齐全。重新解压到自己有写入权限的文件夹，不要只复制一个 exe。'),
('提示 Windows 已保护你的电脑', '按第 2 页核对来源与应用名。没有“仍要运行”或出现具体恶意软件提示时，联系作者或设备管理员，不关闭安全功能。'),
('桌面图标无反应、仍打开旧版', '检查任务栏是否已有窗口；再右键快捷方式查看目标位置。更新程序或移动文件夹后，重新创建指向正确 exe 的快捷方式。'),
('忘记密码或看不到原记录', '选对本机账号；忘记密码可用离线恢复码。数据不会自动跨电脑同步；新电脑需恢复自己导出的家教版备份。'),
]
for title, text in items:
    para(title, size=10.7, bold=True, color=BLUE, gap=2)
    para(text, size=9.7, gap=9)
section('这次检查能证明什么')
para('发布检查覆盖：两个架构的公开下载与 SHA-256、完整解压、包内依赖与版本、独立测试目录下的首次账号窗口、核心规则和 WPF 功能。验证电脑为 Windows ARM64，x64 通过系统兼容环境运行；尚未逐台验证朋友的电脑或独立 Intel / AMD 设备。系统策略、Smart App Control 与未签名信誉提醒仍可能影响首次运行。', size=9.4)
para('求助时提供：家教版版本、电脑系统类型、错误提示文字或截图。请遮住真实学生信息；不要把学生数据库、密码、恢复码或个人备份上传到公开 Issues。', size=9.4)
link('家教版下载与发布说明', RELEASE)
link('反馈问题（GitHub Issues）', 'https://github.com/yilaxin/E2-Day/issues')
para('参考资料（核对日期：2026-10-15）', size=8.5, color=MUTED, gap=3)
link('Microsoft Learn：SmartScreen 应用信誉与未签名程序', 'https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation')
link('Microsoft Support：Smart App Control 常见问题', 'https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions')
c.save()
docs_out = ROOT / 'docs/E2-Day-Tutor-Guide.pdf'
shutil.copyfile(OUT, docs_out)
print(f'Created {OUT} ({OUT.stat().st_size} bytes); 7 pages')

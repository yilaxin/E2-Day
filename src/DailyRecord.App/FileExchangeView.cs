using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;
using Microsoft.Win32;

namespace DailyRecord.App;

public partial class MainWindow
{
    internal const string MobileAppUrl = "https://yilaxin.github.io/E2-Day/mobile/";
    private void BuildFileExchange()
    {
        var panel = Ui.Stack(Ui.Text("手机与文件交换", 18, bold: true),
            Ui.Text("手机独立记录，电脑与手机通过交换文件合并任务、事件、历史和图片。每次导入先预览，确认前保留当前记录。", 12, Ui.Muted));
        panel.Children.Add(Ui.Row(Ui.Button("导出交换文件", ExportExchange, accent: true), Ui.Button("导入交换文件", ImportExchange)));
        panel.Children.Add(Ui.Text("手机安装入口", 12, Ui.Muted));
        panel.Children.Add(new TextBox { Text = MobileAppUrl, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(Ui.Button("打开手机安装页面", () => Process.Start(new ProcessStartInfo(MobileAppUrl) { UseShellExecute = true }), quiet: true));
        panel.Children.Add(Ui.Text("用手机浏览器打开安装页面，添加到桌面后创建本机账号。记录保存在自己的设备上，交换文件包含记录与图片。", 12, Ui.Muted));
        PageBody.Children.Add(GlassCardBorder.Create(panel));
    }
    private void ExportExchange()
    {
        var picker = new SaveFileDialog { Title = "导出 E²-Day 交换文件", Filter = "E²-Day 交换文件 (*.json)|*.json", FileName = $"E2-Day-交换文件-{_service.Today:yyyy-MM-dd}.json" };
        if (picker.ShowDialog(this) != true) return;
        try { _store.ExportPortableBackup(picker.FileName, _service.State); MessageBox.Show(this, "交换文件已导出，包含当前记录和图片，可在另一台设备预览并合并。", "导出完成"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "文件未导出"); }
    }
    private void ImportExchange()
    {
        var picker = new OpenFileDialog { Title = "选择 E²-Day 交换文件", Filter = "E²-Day 记录文件 (*.json)|*.json", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var file = SqliteStore.ReadBackupData(picker.FileName);
            var plan = FileImport.Preview(_service.State, file.State);
            CreateImportPreview(plan, file.Images).ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "文件未导入"); }
    }
    internal Window CreateImportPreview(FileImportPlan plan, IReadOnlyList<NoteImage> images)
    {
        var dialog = new Window { Title = "预览文件导入", Owner = this, Width = 700, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Page };
        var panel = new StackPanel { Margin = new Thickness(26) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Ui.Text("把另一台设备的记录带过来", 22, Ui.Accent, true));
        panel.Children.Add(Ui.Text($"新增 {plan.Added} 条 · 相同 {plan.Unchanged} 条 · 需选择 {plan.Conflicts.Count} 条", 13, Ui.Muted));
        panel.Children.Add(Ui.Text("已有的其他记录会继续保留；本机偏好设置继续使用当前值。确认合并前保存导入前副本。", 12, Ui.Muted));
        var choices = new Dictionary<int, string>();
        for (var i = 0; i < plan.Conflicts.Count; i++)
        {
            var index = i; var conflict = plan.Conflicts[i];
            var kind = conflict.Collection switch { "Tasks" => "任务", "Events" => "事件", _ => "历史" };
            panel.Children.Add(Ui.Label(kind + " · " + (conflict.Current["Title"]?.ToString() ?? "记录")));
            foreach (var option in new[] { ("current", "保留本机", conflict.Current), ("incoming", "采用文件", conflict.Incoming) })
            {
                var row = new RadioButton { GroupName = "file-import-" + i, Tag = "file-import-" + i + "-" + option.Item1,
                    Content = Ui.Text(option.Item2 + "：\n" + DescribeRecord(option.Item3), 13), Margin = new Thickness(0, 6, 0, 10) };
                row.Checked += (_, _) => choices[index] = option.Item1; panel.Children.Add(row);
                var ids = (option.Item3["ImageIds"] as JsonArray)?.Select(id => id!.ToString()).ToArray() ?? [];
                var thumbnails = ids.Select(id => images.FirstOrDefault(image => image.Id == id) ?? _store.GetNoteImages([id]).Single()).ToArray();
                if (thumbnails.Length > 0) panel.Children.Add(NoteImages.Gallery(thumbnails, dialog));
            }
        }
        var error = Ui.Text("", 12, Ui.Amber); error.Tag = "file-import-error"; panel.Children.Add(error);
        var confirm = Ui.Button("确认合并", () =>
        {
            try { _service.ImportRecords(plan, choices, images); dialog.Close(); RefreshPage(); }
            catch (Exception ex) { error.Text = ex.Message; }
        }, accent: true); confirm.Tag = "file-import-confirm";
        panel.Children.Add(Ui.Row(Ui.Button("取消", dialog.Close), confirm));
        return dialog;
    }
    private static string DescribeRecord(JsonNode row)
    {
        var state = row["State"]?.ToString() switch { "Completed" => "已完成", "Cancelled" => "已取消", "Open" => "待处理", _ => "" };
        var text = $"{row["Title"]}\n{row["Notes"]}\n日期：{row["ScheduledDate"] ?? row["StartDate"] ?? row["Day"]}  {state}";
        if (row["CompletedOn"] is { } completed) text += " · 完成于 " + completed;
        if (row["IsImportant"] is { } important) text += "\n重要标记：" + (important.ToString() == "true" ? "是" : "否");
        if (row["Repeat"] is { } repeat) text += "\n重复规则：" + (repeat.ToString() switch { "Daily" => "每天", "Weekly" => "每周", "Monthly" => "每月", "Yearly" => "每年", "LunarYearly" => "每年农历", _ => "不重复" });
        if (row["EndDate"] is { } end) text += " · 截止 " + end;
        if (row["Importance"] is { } level) text += "\n提醒档位：" + (level.ToString() switch { "Important" => "重要", "VeryImportant" => "非常重要", _ => "普通" });
        if (row["Kind"] is { } kind) text += "\n历史操作：" + (kind.ToString() switch { "Viewed" => "看过", "Completed" => "完成", "Cancelled" => "取消", "Ended" => "结束长期任务", _ => kind.ToString() });
        if (row["Reverted"] is { } reverted) text += "\n操作已撤销：" + (reverted.ToString() == "true" ? "是" : "否");
        return text + $"\n图片：{(row["ImageIds"] as JsonArray)?.Count ?? 0}";
    }
}

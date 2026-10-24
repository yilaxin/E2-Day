using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DailyRecord.Core;
using Microsoft.Win32;
using ActivityKind = DailyRecord.Core.ActivityKind;

namespace DailyRecord.App;

public partial class MainWindow
{
    private void BuildHistory()
    {
        Header("完成与查看，都有迹可循", "历史记录", "按日期回看，或搜索所有日期的标题与备注。事项显示当前状态，操作记录保留当时的信息。");
        HeaderActions.Children.Add(Ui.Button("导出备份", ExportBackup));
        var date = new DatePicker { SelectedDate = _historyDate.ToDateTime(TimeOnly.MinValue), Width = 170,
            DisplayDateStart = new DateTime(1901, 1, 1), DisplayDateEnd = new DateTime(2100, 12, 31), Margin = new Thickness(0, 0, 10, 0) };
        // A Calendar popup has no scrolling viewport of its own. Stop its bubbling
        // wheel after Calendar handles it, before it reaches the page ScrollViewer.
        var popupCalendarStyle = new Style(typeof(Calendar), date.CalendarStyle ?? Application.Current.TryFindResource(typeof(Calendar)) as Style);
        popupCalendarStyle.Setters.Add(new EventSetter(Mouse.MouseWheelEvent, new MouseWheelEventHandler((_, e) => e.Handled = true)));
        date.CalendarStyle = popupCalendarStyle;
        var search = new TextBox { Text = _search, ToolTip = "搜索所有日期的标题和备注", Margin = new Thickness(0, 0, 10, 0) };
        var archived = new CheckBox { Content = "含已取消", IsChecked = _includeArchived, VerticalAlignment = VerticalAlignment.Center };
        var controls = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(date); Grid.SetColumn(search, 1); controls.Children.Add(search); Grid.SetColumn(archived, 2); controls.Children.Add(archived);
        PageBody.Children.Add(Ui.Text("选择日期  /  输入关键词搜索", 11, Ui.Muted)); PageBody.Children.Add(controls);
        var results = new StackPanel(); PageBody.Children.Add(results);
        void RenderResults()
        {
            StopPageScrolling();
            RetirePanelChildren(results);
            bool Matches(string title, string notes) => title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase) || notes.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);
            var searching = !string.IsNullOrWhiteSpace(_search);
            var tasks = searching ? _service.State.Tasks.Where(t => Matches(t.Title, t.Notes) ||
                _service.State.Activities.Any(a => a.TaskId == t.Id && !a.Reverted && Matches(a.Title, a.Notes))).ToList()
                : _service.ForDate(_historyDate);
            if (!_includeArchived) tasks = tasks.Where(t => t.State != TaskState.Cancelled).ToList();
            var records = searching ? _service.State.Activities.Where(a => !a.Reverted && Matches(a.Title, a.Notes)).OrderByDescending(a => a.At).ToList()
                : _service.ActivitiesFor(_historyDate);
            var completed = _service.State.Tasks.Count(t => t.State == TaskState.Completed && t.CompletedOn == _historyDate);
            var intro = Ui.Text(searching ? $"找到 {tasks.Count} 个事项、{records.Count} 条操作记录" : $"{_historyDate:yyyy 年 M 月 d 日} · 实际完成 {completed} 项", 15, bold: true);
            intro.Margin = new Thickness(0, 0, 0, 14); results.Children.Add(intro);
            if (tasks.Count == 0) results.Children.Add(GlassCardBorder.Create(Ui.Text(searching ? "没有匹配的事项，可以换一个关键词。" : "这一天没有个人事项记录。", 13, Ui.Muted)));
            else foreach (var task in TaskService.Sort(tasks)) results.Children.Add(TaskRow(task, refresh: RenderResults));
            if (records.Count > 0)
            {
                results.Children.Add(Ui.Label(searching ? "匹配的历史操作" : "当日操作记录"));
                foreach (var record in records) results.Children.Add(ActivityRow(record));
            }
        }
        date.SelectedDateChanged += (_, _) => { if (date.SelectedDate is { } selected) { _historyDate = DateOnly.FromDateTime(selected); RenderResults(); } };
        search.TextChanged += (_, _) => { _search = search.Text; RenderResults(); };
        archived.Checked += (_, _) => { _includeArchived = true; RenderResults(); };
        archived.Unchecked += (_, _) => { _includeArchived = false; RenderResults(); };
        RenderResults();
    }

    private Border ActivityRow(ActivityRecord record)
    {
        var kind = record.Kind switch { ActivityKind.Completed => "完成任务", ActivityKind.Viewed => "看过提醒", ActivityKind.Ended => "结束长期任务", _ => "取消归档" };
        var heading = Ui.Row(Ui.Badge(kind, record.Kind == ActivityKind.Completed), Ui.Text($"{record.Day:M/d}  {record.At.ToLocalTime():HH:mm}", 10, Ui.Muted));
        var title = Ui.Text((record.IsImportant ? "★ " : "") + record.Title, 13, bold: record.IsImportant); title.Margin = new Thickness(0, 9, 0, 5);
        var panel = Ui.Stack(heading, title, Ui.Text($"当时原定日期：{record.ScheduledDate:yyyy-MM-dd}", 10, Ui.Muted));
        if (record.Notes.Length > 0) { var note = Ui.Text(record.Notes, 11, Ui.Muted); note.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(note); }
        if (record.ImageIds.Count > 0) panel.Children.Add(NoteGallery(record.ImageIds));
        return GlassCardBorder.Create(panel, new Thickness(16));
    }

    private void BuildSettings()
    {
        Header("让工具适合你的习惯", "偏好设置", "记录保存在这台电脑上，任务和日历可离线使用。");
        BuildAccountSettings();
        BuildFileExchange();
        var normal = new TextBox { Text = _service.State.Settings.NormalDays.ToString(), Width = 90 };
        var important = new TextBox { Text = _service.State.Settings.ImportantDays.ToString(), Width = 90 };
        var very = new TextBox { Text = _service.State.Settings.VeryImportantDays.ToString(), Width = 90 };
        var reminder = Ui.Stack(Ui.Text("提前提醒", 18, bold: true), Ui.Text("按自然日计算。每日任务的星标只影响显示，不改变这里的提醒天数。", 12, Ui.Muted));
        foreach (var pair in new[] { ("普通事件", normal), ("重要事件", important), ("非常重要事件", very) })
        {
            var label = Ui.Text(pair.Item1, 13); label.Width = 130;
            var row = Ui.Row(label, pair.Item2, Ui.Text("  天前开始提醒", 12, Ui.Muted)); row.Margin = new Thickness(0, 14, 0, 0); reminder.Children.Add(row);
        }
        var save = Ui.Button("保存提醒设置", () => Run(() =>
        {
            if (!int.TryParse(normal.Text, out var n) || !int.TryParse(important.Text, out var i) || !int.TryParse(very.Text, out var v))
                throw new ArgumentException("请填写 0 至 365 的整数。");
            _service.UpdateSettings(n, i, v);
        }), accent: true);
        save.HorizontalAlignment = HorizontalAlignment.Left; save.Margin = new Thickness(0, 18, 0, 0); reminder.Children.Add(save);
        PageBody.Children.Add(GlassCardBorder.Create(reminder));

        var data = Ui.Stack(Ui.Text("数据与备份", 18, bold: true), Ui.Text("自动保存；应用运行当天更新一份备份，保留最近 30 份。恢复前会备份当前记录。", 12, Ui.Muted));
        var path = Ui.Text(_smoke ? "示例数据目录（隔离）" : _store.DataDirectory, 11, Ui.Muted); path.Margin = new Thickness(0, 12, 0, 12); data.Children.Add(path);
        var actions = Ui.Row(Ui.Button("导出备份", ExportBackup, accent: true), Ui.Button("从备份恢复", ImportBackup), Ui.Button("打开备份文件夹", () =>
        {
            Directory.CreateDirectory(_store.BackupDirectory);
            try { Process.Start(new ProcessStartInfo(_store.BackupDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件夹"); }
        }));
        data.Children.Add(actions); PageBody.Children.Add(GlassCardBorder.Create(data));

        var holiday = Ui.Stack(Ui.Text("中国大陆节假日", 18, bold: true),
            Ui.Text("法定放假与调休来自年度安排。尚未公布或未更新的年份会明确标示。", 12, Ui.Muted));
        var year = new TextBox { Text = _year.ToString(), Width = 90 };
        var feedback = Ui.Text(_holidays.HasYear(_year) ? $"{_year} 年数据已在本地，可离线使用。" : $"{_year} 年放假调休数据尚未更新。", 11, Ui.Muted);
        feedback.Margin = new Thickness(0, 10, 0, 0);
        Button? update = null;
        update = Ui.Button("联网更新年度数据", async () =>
        {
            if (!int.TryParse(year.Text, out var target)) { feedback.Text = "请填写有效年份。"; return; }
            update!.IsEnabled = false; feedback.Text = "正在更新…";
            try { await _holidays.UpdateYearAsync(target, _lifetime.Token); feedback.Text = $"{target} 年数据已更新。"; }
            catch { feedback.Text = "更新失败，该年度可能尚未公布；已有数据已保留。"; }
            finally { update.IsEnabled = true; }
        });
        var updateRow = Ui.Row(year, update); updateRow.Margin = new Thickness(0, 14, 0, 0); holiday.Children.Add(updateRow); holiday.Children.Add(feedback);
        PageBody.Children.Add(GlassCardBorder.Create(holiday));

        var news = Ui.Stack(Ui.Text("AI 资讯", 18, bold: true), Ui.Text("中文优先，英文补充；使用来源提供的标题与简介，不需要密钥或付费。", 12, Ui.Muted));
        foreach (var source in NewsService.Sources)
        { var label = Ui.Text($"{source.Name}  ·  {source.Language}", 11, Ui.Muted); label.Margin = new Thickness(0, 7, 0, 0); news.Children.Add(label); }
        var refresh = Ui.Button("刷新资讯", () => _ = RefreshNewsAsync(true)); refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Margin = new Thickness(0, 13, 0, 0); news.Children.Add(refresh); PageBody.Children.Add(GlassCardBorder.Create(news));
        var appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "2.1.0";
        PageBody.Children.Add(Ui.Text($"E²-Day {appVersion} · Explore Everyday Day · 探索每一天", 11, Ui.Muted));
    }

    private void ExportBackup()
    {
        var dialog = new SaveFileDialog { Title = "导出 E²-Day 备份", Filter = "E²-Day 备份 (*.json)|*.json", FileName = $"E²-Day备份-{_service.Today:yyyy-MM-dd}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { _store.ExportPortableBackup(dialog.FileName, _service.State); MessageBox.Show(this, "通用备份已导出，可在电脑或手机恢复。", "导出完成"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }
    private void ImportBackup()
    {
        var dialog = new OpenFileDialog { Title = "选择 E²-Day 备份", Filter = "E²-Day 备份 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var validated = SqliteStore.ReadBackup(dialog.FileName);
            if (MessageBox.Show(this, $"此备份包含 {validated.Events.Count} 个事件、{validated.Tasks.Count} 个任务。\n\n恢复会替换当前记录；当前记录会先另存为恢复前备份。\n是否恢复？", "恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Run(() => _service.RestoreBackup(dialog.FileName));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "备份未恢复"); }
    }
}

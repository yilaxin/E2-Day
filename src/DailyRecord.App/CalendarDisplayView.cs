using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private TextBlock? _yearViewCaption;
    private TextBlock? _monthViewCaption;
    private FrameworkElement? _yearTaskLegend;
    private FrameworkElement? _monthTaskLegend;
    private static string CalendarModeName(CalendarViewMode mode) => mode switch
    { CalendarViewMode.HideLongTerm => "隐藏长期任务", CalendarViewMode.Plain => "纯日历", _ => "全部安排" };
    private FrameworkElement CalendarDisplayControls(bool month)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var toggle = Ui.Button("显示／隐藏任务标记", () => RunColorChange(() => _service.SetCalendarView(
            _service.State.Settings.CalendarView == CalendarViewMode.Plain ? _service.State.Settings.PreviousCalendarView : CalendarViewMode.Plain)));
        toggle.Tag = month ? "month-calendar-toggle" : "calendar-toggle"; toggle.FontSize = 12;
        toggle.ToolTip = "一键进入纯日历，再点恢复之前的安排视图；只改变显示，不删除任务";
        var choose = Ui.Button("显示内容 ▾", () => { }); choose.Tag = month ? "month-calendar-view-menu" : "calendar-view-menu"; choose.FontSize = 12;
        var menu = new ContextMenu();
        foreach (var mode in Enum.GetValues<CalendarViewMode>())
        {
            var selected = mode;
            var item = new MenuItem { Header = CalendarModeName(mode), Tag = "calendar-mode:" + mode };
            item.Click += (_, _) => RunColorChange(() => _service.SetCalendarView(selected)); menu.Items.Add(item);
        }
        menu.Opened += (_, _) => { foreach (var item in menu.Items.OfType<MenuItem>()) {item.IsCheckable = true; item.IsChecked = Equals(item.Tag, "calendar-mode:" + _service.State.Settings.CalendarView);} };
        choose.ContextMenu = menu; choose.Click += (_, _) => { menu.PlacementTarget = choose; menu.IsOpen = true; };
        var caption = Ui.Text("", 11, Ui.Muted); caption.Tag = month ? "month-calendar-view-caption" : "calendar-view-caption";
        if (month) _monthViewCaption = caption; else _yearViewCaption = caption;
        panel.Children.Add(toggle); panel.Children.Add(choose); panel.Children.Add(caption); return panel;
    }
    private void UpdateCalendarDisplayLabels(bool month)
    {
        var caption = month ? _monthViewCaption : _yearViewCaption;
        if (caption is not null) caption.Text = "当前：" + CalendarModeName(_service.State.Settings.CalendarView);
        var legend = month ? _monthTaskLegend : _yearTaskLegend;
        if (legend is not null) legend.Visibility = _service.State.Settings.CalendarView == CalendarViewMode.Plain ? Visibility.Collapsed : Visibility.Visible;
    }
}

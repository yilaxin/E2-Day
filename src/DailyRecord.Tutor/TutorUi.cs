using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DailyRecord.App;

internal static class Ui
{
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.FindResource(key);
    public static readonly Brush Ink = ThemeBrush("InkBrush");
    public static readonly Brush Muted = ThemeBrush("MutedBrush");
    public static readonly Brush Accent = ThemeBrush("AccentBrush");
    public static readonly Brush Line = ThemeBrush("LineBrush");
    public static readonly Brush Page = ThemeBrush("PageBrush");
    public static readonly Brush Surface = ThemeBrush("SurfaceBrush");
    public static readonly Brush SubtleSurface = ThemeBrush("SubtleSurfaceBrush");
    public static readonly Brush NeutralFill = ThemeBrush("NeutralFillBrush");
    public static readonly Brush Selected = ThemeBrush("SelectedBrush");
    public static readonly Brush OnAccentMuted = ThemeBrush("OnAccentMutedBrush");
    public static readonly Brush BlankDay = ThemeBrush("BlankDayBrush");
    public static readonly Brush Amber = ThemeBrush("ImportantTextBrush");
    public static readonly Brush ImportantFill = ThemeBrush("ImportantFillBrush");
    public static readonly Brush ImportantBadge = ThemeBrush("ImportantBadgeBrush");
    public static readonly Brush ImportantLine = ThemeBrush("ImportantLineBrush");

    public static TextBlock Text(string text, double size = 14, Brush? color = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ink,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };
    public static Button Button(string text, Action action, bool accent = false, bool quiet = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        if (accent) button.Style = (Style)Application.Current.FindResource("AccentButton");
        else if (quiet) button.Style = (Style)Application.Current.FindResource("QuietButton");
        button.Click += (_, _) => action();
        return button;
    }
    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var glass = Application.Current.TryFindResource("GlassCard") as Style;
        var card = new GlassCardBorder
        {
            Child = child, Background = Surface, BorderThickness = new Thickness(1), Style = glass,
            CornerRadius = new CornerRadius(glass is null ? 14 : 18), Padding = padding ?? new Thickness(20),
            Margin = new Thickness(0, 0, 0, 14)
        };
        if (glass is null) card.BorderBrush = Line;
        return card;
    }
    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static Border Badge(string text, bool important = false) => new()
    {
        Child = Text(text, 10, important ? Amber : Muted), Padding = new Thickness(7, 3, 7, 3),
        CornerRadius = new CornerRadius(4), Background = important ? ImportantBadge : NeutralFill,
        Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
    };
    public static TextBlock Label(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12,
        Foreground = Ink, Margin = new Thickness(0, 16, 0, 7)
    };
    public static string Importance(DailyRecord.Core.EventImportance importance) => importance switch
    {
        DailyRecord.Core.EventImportance.Important => "重要事件",
        DailyRecord.Core.EventImportance.VeryImportant => "非常重要事件",
        _ => "普通事件"
    };
    public static string Repeat(DailyRecord.Core.RepeatRule repeat) => repeat switch
    {
        DailyRecord.Core.RepeatRule.Weekly => "每周",
        DailyRecord.Core.RepeatRule.Monthly => "每月",
        DailyRecord.Core.RepeatRule.Yearly => "每年（公历）",
        DailyRecord.Core.RepeatRule.LunarYearly => "每年（农历）",
        DailyRecord.Core.RepeatRule.Daily => "每天（长期任务）",
        _ => "不重复"
    };
}

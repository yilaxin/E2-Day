using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DailyRecord.App;

// Soft vector layers avoid a full-size shader render target for every glass card.
internal sealed class GlassCardBorder : Border
{
    private static readonly Brush[] ShadowLayers = new byte[] { 1, 1, 1, 2, 2, 3, 4, 5 }.Select(alpha =>
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x6B, 0x52, 0x8E)); brush.Freeze(); return (Brush)brush;
    }).ToArray();
    protected override void OnRender(DrawingContext dc)
    {
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            for (var i = 0; i < ShadowLayers.Length; i++)
            {
                var spread = ShadowLayers.Length - i - 1;
                dc.DrawRoundedRectangle(ShadowLayers[i], null,
                    new Rect(2 - spread, 2 - spread, RenderSize.Width + 2 * spread, RenderSize.Height + 2 * spread),
                    CornerRadius.TopLeft + spread, CornerRadius.TopLeft + spread);
            }
        base.OnRender(dc);
    }
    internal static Border Create(UIElement child, Thickness? padding = null) => new GlassCardBorder
    {
        Child = child, Background = Ui.Surface, BorderThickness = new Thickness(1),
        Style = Application.Current.TryFindResource("GlassCard") as Style, Effect = null,
        CornerRadius = new CornerRadius(18), Padding = padding ?? new Thickness(20), Margin = new Thickness(0, 0, 0, 14)
    };
}

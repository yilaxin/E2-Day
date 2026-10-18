using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DailyRecord.App;

internal static class TutorAppearance
{
    internal static Viewbox Backdrop()
    {
        var canvas = new Canvas { Width = 1360, Height = 940, ClipToBounds = true };
        Add(850, 720, 790, -225, "#DCEDA0D2", "#00EDA0D2");
        Add(800, 690, -220, 420, "#C6A8B4F2", "#00A8B4F2");
        Add(950, 620, 450, 650, "#C8F09BBE", "#00F09BBE");
        return new Viewbox { Child = canvas, Stretch = Stretch.Fill, IsHitTestVisible = false, Focusable = false };
        void Add(double width, double height, double left, double top, string center, string edge)
        {
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(center), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(edge), 1));
            var ellipse = new Ellipse { Width = width, Height = height, Fill = brush };
            Canvas.SetLeft(ellipse, left); Canvas.SetTop(ellipse, top); canvas.Children.Add(ellipse);
        }
    }
}

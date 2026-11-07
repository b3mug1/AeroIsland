using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;

namespace AeroIsland.App;

public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, Math.Clamp(value, 0, 1));
    }

    public Brush TrackBrush { get; set; } = Brushes.DimGray;
    public Brush ProgressBrush { get; set; } = Brushes.White;
    public double StrokeWidth { get; set; } = 3;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - StrokeWidth / 2 - 1);
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var size = new Size(radius, radius);
        var trackPen = new Pen(TrackBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0) return;
        var progressPen = new Pen(ProgressBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (progress >= 0.9999)
        {
            drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var angle = -Math.PI / 2 + progress * Math.PI * 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, size, 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, progressPen, geometry);
    }
}

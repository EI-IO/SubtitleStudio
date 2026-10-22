using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SubtitleStudio.App.Controls;

/// <summary>紧凑的环形进度：窗口窄的时候替代长条进度条。</summary>
public sealed class RingProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RingProgress, double>(nameof(Value));

    private static readonly IPen TrackPen = new Pen(new SolidColorBrush(Color.Parse("#2E2E38")), 3);
    private static readonly IPen ArcPen = new Pen(new SolidColorBrush(Color.Parse("#4C8DFF")), 3)
    {
        LineCap = PenLineCap.Round
    };

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static RingProgress()
    {
        AffectsRender<RingProgress>(ValueProperty);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 6)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2 - 2;

        context.DrawEllipse(null, TrackPen, center, radius, radius);

        var fraction = Math.Clamp(Value, 0, 1);
        if (fraction <= 0.001)
            return;

        // 从 12 点方向顺时针画一段弧
        var start = -Math.PI / 2;
        var end = start + Math.PI * 2 * fraction;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var from = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
            var to = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));

            ctx.BeginFigure(from, false);
            ctx.ArcTo(
                to,
                new Size(radius, radius),
                0,
                fraction > 0.5,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, ArcPen, geometry);
    }
}

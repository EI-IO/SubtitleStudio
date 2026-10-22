using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SubtitleStudio.App.Controls;

/// <summary>透明视频预览用的棋盘背景。</summary>
public sealed class Checkerboard : Control
{
    private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#202024"));
    private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#2A2A30"));

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        const double cell = 16;

        context.FillRectangle(Dark, new Rect(0, 0, w, h));
        for (var y = 0; y * cell < h; y++)
        {
            for (var x = 0; x * cell < w; x++)
            {
                if ((x + y) % 2 != 0)
                    continue;
                context.FillRectangle(Light, new Rect(x * cell, y * cell, cell, cell));
            }
        }
    }
}

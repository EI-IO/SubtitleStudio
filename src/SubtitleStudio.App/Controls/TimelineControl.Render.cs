using System.Globalization;
using Avalonia;
using Avalonia.Media;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Controls;

/// <summary>时间轴的绘制部分：刻度尺、波形、字幕块、播放头。</summary>
public sealed partial class TimelineControl
{
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1B1B1F"));
    private static readonly IBrush RulerBackground = new SolidColorBrush(Color.Parse("#24242A"));
    private static readonly IBrush RowBackground = new SolidColorBrush(Color.Parse("#1E1E24"));
    private static readonly IBrush RowSelectedBackground = new SolidColorBrush(Color.Parse("#252530"));
    private static readonly IBrush CueLaneBackground = new SolidColorBrush(Color.Parse("#191920"));
    private static readonly IBrush CueLaneSelected = new SolidColorBrush(Color.Parse("#2B2B3A"));
    private static readonly IBrush WaveBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush WaveSelectedBrush = new SolidColorBrush(Color.Parse("#46B85A"));
    private static readonly IBrush CueBrush = new SolidColorBrush(Color.Parse("#3D6FD8"));
    private static readonly IBrush CueSelectedBrush = new SolidColorBrush(Color.Parse("#E0A030"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#E8E8E8"));
    private static readonly IBrush DimText = new SolidColorBrush(Color.Parse("#8A8A93"));
    private static readonly IBrush ChipBackground = new SolidColorBrush(Color.Parse("#CC16161A"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#4C8DFF"));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#E05A5A"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#32323A")), 1);
    private static readonly IPen RowPen = new Pen(new SolidColorBrush(Color.Parse("#2B2B33")), 1);
    private static readonly IPen PlayheadPen = new Pen(new SolidColorBrush(Color.Parse("#FF5A5A")), 1.5);
    private static readonly IPen CueBorderPen = new Pen(new SolidColorBrush(Color.Parse("#8FB4FF")), 1);
    private static readonly Typeface Face = new("Microsoft YaHei");

    /// <summary>刻度标签之间至少留这么多像素，太挤就换更大的步长。</summary>
    private const double MinLabelGap = 76;

    private static readonly double[] TickSteps = { 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        // 命中矩形随每次绘制重建：删掉音轨后旧条目不留存
        _hitRects.Clear();

        context.FillRectangle(Background, new Rect(0, 0, w, h));

        var state = _hooked;
        if (state?.Media is null || state.Tracks.Count == 0)
        {
            var hint = new FormattedText(Services.Loc.T("Timeline.EmptyHint"),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 14, DimText);
            context.DrawText(hint, new Point((w - hint.Width) / 2, h / 2 - 10));
            return;
        }

        DrawRuler(context, state, w);

        for (var i = 0; i < state.Tracks.Count; i++)
        {
            var top = RowTop(i);
            if (top > h)
                break;

            var track = state.Tracks[i];
            DrawTrackRow(context, state, track, top, w, ReferenceEquals(track, state.SelectedTrack));
        }

        DrawPlayhead(context, state, h);
        DrawDragHint(context);
    }

    /// <summary>拖动字幕块时在刻度尺下沿浮一个时间标签，拖到哪一眼看清。</summary>
    private void DrawDragHint(DrawingContext context)
    {
        if (_dragCue is null || _mode is not (DragMode.MoveCue or DragMode.ResizeStart or DragMode.ResizeEnd))
            return;

        var text = new FormattedText(
            $"{FormatTimePrecise(_dragCue.Start)} → {FormatTimePrecise(_dragCue.End)}",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, TextBrush);
        var mid = (TimeToX(_dragCue.Start) + TimeToX(_dragCue.End)) / 2;
        var x = Math.Clamp(mid - text.Width / 2 - 7, 2, Math.Max(2, Bounds.Width - text.Width - 16));
        var chip = new Rect(x, RulerHeight + 3, text.Width + 14, text.Height + 6);

        context.DrawRectangle(ChipBackground, null, chip, 4, 4);
        context.DrawText(text, new Point(chip.X + 7, chip.Y + 3));
    }

    private static string FormatTimePrecise(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }

    private void DrawRuler(DrawingContext context, AppState state, double w)
    {
        context.FillRectangle(RulerBackground, new Rect(0, 0, w, RulerHeight));

        var duration = state.Media?.DurationSeconds ?? 0;
        var step = PickStep();
        var first = Math.Floor(Scroll / step) * step;
        var lastLabelRight = double.NegativeInfinity;
        var gridBottom = Math.Min(Bounds.Height, RowTop(state.Tracks.Count) - RowGap);

        for (var t = first; t <= Scroll + w / Pps + step; t += step)
        {
            if (t < 0)
                continue;

            var x = TimeToX(t);
            if (x < -20 || x > w + 20)
                continue;

            context.DrawLine(GridPen, new Point(x, RulerHeight - 5), new Point(x, gridBottom));

            var label = new FormattedText(FormatTime(t), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Face, 10, DimText);
            context.DrawText(label, new Point(x + 3, 3));
            lastLabelRight = x + 3 + label.Width;
        }

        DrawDurationLabel(context, duration, w, lastLabelRight);

        context.DrawLine(GridPen, new Point(0, RulerHeight), new Point(w, RulerHeight));
    }

    /// <summary>右端再标一次总时长；和最后一个刻度贴太近就不画。</summary>
    private void DrawDurationLabel(DrawingContext context, double duration, double w, double lastLabelRight)
    {
        if (duration <= 0)
            return;

        var label = new FormattedText(FormatTime(duration), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Face, 10, DimText);
        var x = Math.Min(w - label.Width - 4, TimeToX(duration) - label.Width);

        if (x > lastLabelRight + 8)
            context.DrawText(label, new Point(x, 3));
    }

    private void DrawTrackRow(DrawingContext context, AppState state, TrackItem track, double top,
        double w, bool selected)
    {
        var laneWidth = Math.Max(0, w - GutterWidth);
        var waveRect = new Rect(0, top, laneWidth, WaveHeight);
        context.FillRectangle(selected ? RowSelectedBackground : RowBackground, waveRect);

        if (selected)
            context.FillRectangle(AccentBrush, new Rect(0, top, 2.5, WaveHeight + CueHeight));

        DrawWaveform(context, track, waveRect, selected);

        // 字幕条紧贴波形下方
        var laneRect = new Rect(0, top + WaveHeight, laneWidth, CueHeight);
        context.FillRectangle(selected ? CueLaneSelected : CueLaneBackground, laneRect);
        context.DrawLine(RowPen, new Point(0, laneRect.Y), new Point(laneWidth, laneRect.Y));
        context.DrawLine(RowPen, new Point(0, laneRect.Bottom), new Point(laneWidth, laneRect.Bottom));

        DrawCues(context, state, track, laneRect, selected);

        var chipRight = DrawTrackChip(context, track, top, selected);

        // 勾选框：勾上的轨道才参与导出
        var check = new Rect(chipRight + 6, top + 3, 13, 13);
        DrawExportCheck(context, check, track.ExportEnabled, selected);

        // 右侧贴边：导出 + 删除两个按钮。固定 20×20，整体在本行（波形 + 字幕条）里垂直居中；
        // 轨道少的时候行高会被拉高，按钮不能跟着拉长，否则会变成细长条。
        const double buttonSize = 20;
        const double gap = 4;
        var rowHeight = WaveHeight + CueHeight;
        var buttonX = w - GutterWidth + (GutterWidth - buttonSize) / 2;
        var stackTop = top + Math.Max(0, (rowHeight - (buttonSize * 2 + gap)) / 2);
        var exportButton = new Rect(buttonX, stackTop, buttonSize, buttonSize);
        var deleteButton = new Rect(buttonX, stackTop + buttonSize + gap, buttonSize, buttonSize);

        if (ReferenceEquals(_hoverTrack, track))
        {
            DrawAudioExportButton(context, exportButton, selected);
            DrawDeleteButton(context, deleteButton, selected);
        }

        _hitRects[track] = (check, exportButton, deleteButton);
    }

    private void DrawExportCheck(DrawingContext context, Rect rect, bool isChecked, bool selected)
    {
        context.DrawRectangle(null, selected ? CueBorderPen : RowPen, rect, 3, 3);

        if (!isChecked)
            return;

        var inner = rect.Deflate(3);
        context.DrawRectangle(AccentBrush, null, inner, 2, 2);
    }

    private void DrawAudioExportButton(DrawingContext context, Rect rect, bool selected)
    {
        context.DrawRectangle(selected ? RowSelectedBackground : RowBackground, null, rect, 3, 3);
        context.DrawRectangle(null, RowPen, rect, 3, 3);

        var pen = new Pen(selected ? AccentBrush : DimText, 1.2);
        context.DrawGeometry(null, pen, ExportIconGeometry(rect.Deflate(4.5)));
    }

    /// <summary>垃圾桶：盖子 + 桶身，用线条画（整条音轨删除，红色以示区别）。</summary>
    private void DrawDeleteButton(DrawingContext context, Rect rect, bool selected)
    {
        context.DrawRectangle(selected ? RowSelectedBackground : RowBackground, null, rect, 3, 3);
        context.DrawRectangle(null, new Pen(DangerBrush, 1), rect, 3, 3);

        var brush = selected ? DangerBrush : new SolidColorBrush(Color.Parse("#C46A6A"));
        var pen = new Pen(brush, 1.2);
        var r = rect.Deflate(4);
        var lidY = r.Y + 2.5;
        var bodyTop = lidY + 1.5;

        // 盖子
        context.DrawLine(pen, new Point(r.X - 0.5, lidY), new Point(r.Right + 0.5, lidY));
        // 提手
        context.DrawLine(pen, new Point(r.X + r.Width * 0.3, lidY - 2), new Point(r.X + r.Width * 0.7, lidY - 2));
        // 桶身（上宽下窄）
        context.DrawLine(pen, new Point(r.X + 1, bodyTop), new Point(r.X + 1.6, r.Bottom));
        context.DrawLine(pen, new Point(r.Right - 1, bodyTop), new Point(r.Right - 1.6, r.Bottom));
        context.DrawLine(pen, new Point(r.X + 1.6, r.Bottom), new Point(r.Right - 1.6, r.Bottom));
    }

    /// <summary>「方框 + 向上箭头」的导出图标，用几何画出来（不占图片资源）。</summary>
    private static StreamGeometry ExportIconGeometry(Rect r)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        var x0 = r.X;
        var x1 = r.Right;
        var y0 = r.Y;
        var y1 = r.Bottom;
        var height = y1 - y0;
        var frameTop = y0 + height * 0.42;

        // 开口朝上的方框
        ctx.BeginFigure(new Point(x0, frameTop), false);
        ctx.LineTo(new Point(x0, y1));
        ctx.LineTo(new Point(x1, y1));
        ctx.LineTo(new Point(x1, frameTop));
        ctx.EndFigure(false);

        // 向上箭头
        var cx = (x0 + x1) / 2;
        var w = (x1 - x0) * 0.24;
        var shaft = (x1 - x0) * 0.09;
        var headBottom = y0 + height * 0.42;

        ctx.BeginFigure(new Point(cx, y0), false);
        ctx.LineTo(new Point(cx + w, headBottom));
        ctx.LineTo(new Point(cx + shaft, headBottom));
        ctx.LineTo(new Point(cx + shaft, y1 - height * 0.08));
        ctx.LineTo(new Point(cx - shaft, y1 - height * 0.08));
        ctx.LineTo(new Point(cx - shaft, headBottom));
        ctx.LineTo(new Point(cx - w, headBottom));
        ctx.EndFigure(true);

        return geometry;
    }

    /// <summary>左上角的「音轨 N」小胶囊，返回它的右边界。</summary>
    private double DrawTrackChip(DrawingContext context, TrackItem track, double top, bool selected)
    {
        var text = new FormattedText(track.Info.Number, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Face, 10.5, selected ? TextBrush : DimText);
        var chip = new Rect(3, top + 2, text.Width + 10, 15);

        context.DrawRectangle(ChipBackground, null, chip, 3, 3);
        context.DrawText(text, new Point(chip.X + 5, chip.Y + 1.5));
        return chip.Right;
    }

    /// <summary>按屏幕像素列聚合峰值，画出对称的波形。</summary>
    private void DrawWaveform(DrawingContext context, TrackItem track, Rect rect, bool selected)
    {
        var wave = track.Waveform;
        if (wave is null || wave.Min.Length == 0)
            return;

        var top = rect.Y + 4;
        var bottom = rect.Bottom - 3;
        if (bottom <= top)
            return;

        var mid = (top + bottom) / 2;
        var amp = (bottom - top) / 2;
        var ppsWave = wave.PeaksPerSecond;
        var pen = new Pen(selected ? WaveSelectedBrush : WaveBrush, 1);

        for (var px = 0; px < (int)rect.Width; px++)
        {
            var i0 = (int)(XToTime(px) * ppsWave);
            var i1 = (int)Math.Ceiling(XToTime(px + 1) * ppsWave);
            if (i1 <= 0 || i0 >= wave.Min.Length)
                continue;

            i0 = Math.Max(0, i0);
            i1 = Math.Min(wave.Min.Length, Math.Max(i0 + 1, i1));

            var lo = 0f;
            var hi = 0f;
            for (var i = i0; i < i1; i++)
            {
                if (wave.Min[i] < lo) lo = wave.Min[i];
                if (wave.Max[i] > hi) hi = wave.Max[i];
            }

            var y0 = mid - hi * amp;
            var y1 = mid - lo * amp;
            context.DrawLine(pen, new Point(px, y0), new Point(px, Math.Max(y1, y0 + 1)));
        }
    }

    private void DrawCues(DrawingContext context, AppState state, TrackItem track, Rect lane, bool trackSelected)
    {
        foreach (var cue in track.Cues)
        {
            var x0 = TimeToX(cue.Start);
            var x1 = TimeToX(cue.End);
            if (x1 < 0 || x0 > lane.Width)
                continue;

            // 超出可见范围时允许画到边缘外一点，滚动时不会突然消失
            var left = Math.Max(-4, x0);
            var right = Math.Min(lane.Width + 4, x1);
            var width = Math.Max(5, right - left);
            var rect = new Rect(left, lane.Y + 2, width, lane.Height - 4);
            var selected = trackSelected && ReferenceEquals(cue, state.SelectedCue);

            context.DrawRectangle(selected ? CueSelectedBrush : CueBrush, CueBorderPen, rect, 3, 3);

            if (width > 24)
            {
                var text = new FormattedText(cue.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Face, 10.5, TextBrush)
                {
                    MaxTextWidth = Math.Max(6, width - 8),
                    MaxTextHeight = rect.Height - 4,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                context.DrawText(text, new Point(left + 4, rect.Y + 2));
            }
        }
    }

    private void DrawPlayhead(DrawingContext context, AppState state, double h)
    {
        var x = TimeToX(state.CurrentTime);
        if (x < -2 || x > Bounds.Width + 2)
            return;

        context.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, h));

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(new Point(x - 5, 0), true);
            ctx.LineTo(new Point(x + 5, 0));
            ctx.LineTo(new Point(x, 8));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(PlayheadPen.Brush, null, head);
    }

    private double PickStep()
    {
        foreach (var step in TickSteps)
        {
            if (step * Pps >= MinLabelGap)
                return step;
        }

        return TickSteps[^1];
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}

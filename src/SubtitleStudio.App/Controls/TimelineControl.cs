using Avalonia;
using Avalonia.Controls;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Controls;

/// <summary>
/// 时间轴：上方刻度，下面每条音轨一行（波形 + 紧贴其下的字幕块）。
/// 滚轮滚动、Ctrl+滚轮缩放、点击切轨、拖动字幕块改时间。
/// 绘制见 <c>TimelineControl.Render.cs</c>，交互见 <c>TimelineControl.Input.cs</c>。
/// </summary>
public sealed partial class TimelineControl : Control
{
    public static readonly StyledProperty<AppState?> StateProperty =
        AvaloniaProperty.Register<TimelineControl, AppState?>(nameof(State));

    /// <summary>刻度尺高度。</summary>
    private const double RulerHeight = 22;

    /// <summary>波形区的最小高度。</summary>
    private const double MinWaveHeight = 30;

    /// <summary>波形区的最大高度（再高就只是浪费纵向空间了）。</summary>
    private const double MaxWaveHeight = 96;

    /// <summary>紧贴波形下方的字幕条高度。</summary>
    private const double CueHeight = 22;

    /// <summary>音轨之间的空隙。</summary>
    private const double RowGap = 6;

    private const double MinPps = 8;
    private const double MaxPps = 1200;

    /// <summary>一条音轨的最小纵向高度：波形 + 字幕条 + 间隙。</summary>
    private const double MinStride = MinWaveHeight + CueHeight + RowGap;

    /// <summary>字幕块边缘多宽的范围内算「拉伸」而不是「移动」。</summary>
    private const double EdgeGrab = 5;

    /// <summary>右侧固定的音频导出按钮占的宽度。</summary>
    private const double GutterWidth = 34;

    private AppState? _hooked;

    /// <summary>鼠标停在哪条轨道上（音频导出按钮只在悬浮的那条轨上显示）。</summary>
    private TrackItem? _hoverTrack;

    /// <summary>每行勾选框与音频按钮的命中区域，随绘制一起更新。</summary>
    private readonly Dictionary<TrackItem, (Rect Check, Rect Export, Rect Delete)> _hitRects = new();

    public TimelineControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public AppState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
            Hook(change.GetNewValue<AppState?>());
    }

    private void Hook(AppState? state)
    {
        if (_hooked is not null)
            _hooked.PropertyChanged -= OnStateChanged;

        _hooked = state;
        if (_hooked is null)
            return;

        _hooked.PropertyChanged += OnStateChanged;
        InvalidateVisual();
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.CurrentTime) or nameof(AppState.ScrollSeconds)
            or nameof(AppState.PixelsPerSecond) or nameof(AppState.Waveform) or nameof(AppState.Cues)
            or nameof(AppState.SelectedCue) or nameof(AppState.Media) or nameof(AppState.Style)
            or nameof(AppState.SelectedTrack) or nameof(AppState.Tracks)
            or nameof(AppState.MediaTitle))
        {
            InvalidateVisual();
        }
    }

    private double Pps => Math.Clamp(_hooked?.PixelsPerSecond ?? 100, MinPps, MaxPps);

    private double Scroll => _hooked?.ScrollSeconds ?? 0;

    private double TimeToX(double seconds) => (seconds - Scroll) * Pps;

    private double XToTime(double x) => x / Pps + Scroll;

    /// <summary>当前行高：把可用高度均分给每条音轨，太多轨也留一个下限。</summary>
    private double RowStride
    {
        get
        {
            var count = _hooked?.Tracks.Count ?? 0;
            if (count <= 0)
                return MinStride;

            var available = Bounds.Height - RulerHeight - RowGap;
            return Math.Max(MinStride, available / count);
        }
    }

    /// <summary>波形高度跟着行高走，但有上下限。</summary>
    private double WaveHeight =>
        Math.Clamp(RowStride - CueHeight - RowGap, MinWaveHeight, MaxWaveHeight);

    /// <summary>行没铺满整块时，整体在竖直方向居中。</summary>
    private double RowTopOffset
    {
        get
        {
            var count = _hooked?.Tracks.Count ?? 0;
            if (count <= 0)
                return 0;

            var used = count * RowStride;
            var available = Bounds.Height - RulerHeight - RowGap;
            return Math.Max(0, (available - used) / 2);
        }
    }

    private double RowTop(int index) => RulerHeight + RowTopOffset + index * RowStride;

    /// <summary>纵向坐标落在第几条音轨；不在任何音轨上返回 -1。</summary>
    private int RowFromY(double y)
    {
        var top = RulerHeight + RowTopOffset;
        if (y < top)
            return -1;

        var stride = RowStride;
        var index = (int)((y - top) / stride);
        var offset = y - top - index * stride;
        return offset <= WaveHeight + CueHeight ? index : -1;
    }

    /// <summary>纵向坐标是否落在某条音轨的字幕条上（而不是波形上）。</summary>
    private bool InCueStrip(double y)
    {
        var top = RulerHeight + RowTopOffset;
        var stride = RowStride;
        var offset = y - top;
        offset -= Math.Floor(offset / stride) * stride;
        return offset > WaveHeight && offset <= WaveHeight + CueHeight;
    }
}

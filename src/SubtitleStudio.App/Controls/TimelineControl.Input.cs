using Avalonia;
using Avalonia.Input;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Controls;

/// <summary>时间轴的交互部分：切轨、跳转、平移、缩放、拖字幕块。</summary>
public sealed partial class TimelineControl
{
    private enum DragMode
    {
        None,
        Seek,
        Pan,
        MoveCue,
        ResizeStart,
        ResizeEnd
    }

    private DragMode _mode = DragMode.None;
    private Point _dragOrigin;
    private double _dragValue;
    private CueItem? _dragCue;
    private double _cueStart0;
    private double _cueEnd0;

    /// <summary>拖动字幕块时的活动范围：不能压到相邻字幕（按下时算好，拖动中别的块不动）。</summary>
    private double _cueMinStart;
    private double _cueMaxEnd;

    /// <summary>当前音轨上被点中的字幕块；顺带告诉调用方是不是抓在边缘。</summary>
    private CueItem? HitTestCue(AppState state, double x, out bool nearEdgeLeft, out bool nearEdgeRight)
    {
        nearEdgeLeft = nearEdgeRight = false;

        foreach (var cue in state.Cues)
        {
            var x0 = TimeToX(cue.Start);
            var x1 = TimeToX(cue.End);
            if (x < x0 - 3 || x > x1 + 3)
                continue;

            var width = Math.Max(6, x1 - x0);
            nearEdgeLeft = x <= x0 + EdgeGrab && width > EdgeGrab * 2;
            nearEdgeRight = x >= x1 - EdgeGrab && width > EdgeGrab * 2;
            return cue;
        }

        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var state = _hooked;
        if (state?.Media is null || state.Tracks.Count == 0)
            return;

        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        _dragOrigin = pos;
        Focus();

        // 中键 / Alt+左键：平移时间轴
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            _mode = DragMode.Pan;
            _dragValue = state.ScrollSeconds;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // 点哪条音轨就切到哪条：左侧列表和预览跟着换
        var row = RowFromY(pos.Y);
        if (row >= 0 && row < state.Tracks.Count)
            state.SelectTrack(state.Tracks[row]);


        // 勾选框 / 右侧音频导出按钮优先响应（这两个是自绘的，命中矩形在上次 Render 里记下来）
        if (row >= 0 && row < state.Tracks.Count
            && _hitRects.TryGetValue(state.Tracks[row], out var rects))
        {
            if (rects.Check.Contains(pos))
            {
                state.Tracks[row].ExportEnabled = !state.Tracks[row].ExportEnabled;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (rects.Export.Contains(pos))
            {
                _ = ExportAudioAsync(state, state.Tracks[row]);
                e.Handled = true;
                return;
            }

            if (rects.Delete.Contains(pos))
            {
                _ = DeleteTrackAsync(state, state.Tracks[row]);
                e.Handled = true;
                return;
            }
        }

        if (row >= 0 && InCueStrip(pos.Y) && HitTestCue(state, pos.X, out var nearLeft, out var nearRight) is { } cue)
        {
            state.SelectedCue = cue;
            _dragCue = cue;
            _cueStart0 = cue.Start;
            _cueEnd0 = cue.End;

            // 相邻字幕的边界：拖过去会重叠，所以在这里就把活动范围夹死
            _cueMinStart = state.Cues
                .Where(c => !ReferenceEquals(c, cue) && c.Start < cue.Start)
                .Select(c => c.End)
                .DefaultIfEmpty(0)
                .Max();
            _cueMaxEnd = state.Cues
                .Where(c => !ReferenceEquals(c, cue) && c.Start > cue.Start)
                .Select(c => (double?)c.Start)
                .Min() ?? double.MaxValue;

            _mode = nearRight ? DragMode.ResizeEnd : nearLeft ? DragMode.ResizeStart : DragMode.MoveCue;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _mode = DragMode.Seek;
        state.SeekTo(XToTime(pos.X));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var state = _hooked;
        if (state is null)
            return;

        // 悬浮在哪条轨道，决定右边那个导出按钮露不露脸
        UpdateHover(state, e.GetPosition(this).Y);

        if (_mode == DragMode.None)
            return;

        var pos = e.GetPosition(this);
        switch (_mode)
        {
            case DragMode.Seek:
                state.SeekTo(XToTime(pos.X));
                break;

            case DragMode.Pan:
                state.ScrollSeconds = Math.Max(0, _dragValue - (pos.X - _dragOrigin.X) / Pps);
                break;

            case DragMode.MoveCue when _dragCue is not null:
            {
                var duration = _cueEnd0 - _cueStart0;
                var start = Math.Clamp(
                    _cueStart0 + (pos.X - _dragOrigin.X) / Pps,
                    _cueMinStart,
                    Math.Max(_cueMinStart, _cueMaxEnd - duration));
                _dragCue.SetBounds(start, start + duration);
                state.SchedulePreview();
                break;
            }

            case DragMode.ResizeStart when _dragCue is not null:
                _dragCue.SetBounds(Math.Clamp(XToTime(pos.X), _cueMinStart, _cueEnd0 - 0.2), _cueEnd0);
                state.SchedulePreview();
                break;

            case DragMode.ResizeEnd when _dragCue is not null:
                _dragCue.SetBounds(_cueStart0, Math.Clamp(XToTime(pos.X), _cueStart0 + 0.2, _cueMaxEnd));
                state.SchedulePreview();
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _mode = DragMode.None;
        _dragCue = null;
        e.Pointer.Capture(null);
    }

    /// <summary>删除整条音轨（连同它的字幕），先二次确认。</summary>
    private async Task DeleteTrackAsync(AppState state, TrackItem track)
    {
        try
        {
            var ok = await Dialogs.ConfirmAsync(
                Dialogs.OwnerOf(this),
                Loc.T("Tracks.Delete.Title"),
                Loc.T("Tracks.Delete.Message", track.Info.Number, track.Cues.Count),
                Loc.T("Tracks.Delete.Yes"),
                Loc.T("Tracks.Delete.Cancel"));
            if (!ok)
                return;

            var name = track.Info.Number;
            var remaining = state.RemoveTrack(track);
            InvalidateVisual();

            if (remaining == 0)
                state.Status = Loc.T("Status.TrackDeleted", name);
            else
                state.Status = Loc.T("Status.TrackDeletedRemaining", name, remaining);
        }
        catch (Exception ex)
        {
            state.ReportError(Loc.T("Error.DeleteTrack", ex.Message));
        }
    }

    private static async Task ExportAudioAsync(AppState state, TrackItem track)
    {
        try
        {
            await state.ExportTrackAudioAsync(track);
        }
        catch (Exception ex)
        {
            state.ReportError(Loc.T("Error.ExportAudio", ex.Message));
        }
    }

    /// <summary>记录鼠标正在哪条轨道上，变化时重画（导出按钮跟着出现/消失）。</summary>
    private void UpdateHover(AppState state, double y)
    {
        var row = RowFromY(y);
        TrackItem? hovered = null;
        if (row >= 0 && row < state.Tracks.Count)
            hovered = state.Tracks[row];

        if (ReferenceEquals(hovered, _hoverTrack))
            return;

        _hoverTrack = hovered;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoverTrack is null)
            return;

        _hoverTrack = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var state = _hooked;
        if (state is null)
            return;

        var pos = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // 以光标下的时间点为锚缩放
            var anchorTime = XToTime(pos.X);
            var factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
            var newPps = Math.Clamp(Pps * factor, MinPps, MaxPps);

            state.PixelsPerSecond = newPps;
            state.ScrollSeconds = Math.Max(0, anchorTime - pos.X / newPps);
        }
        else
        {
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            state.ScrollSeconds = Math.Max(0, state.ScrollSeconds - delta * 120 / Pps);
        }

        e.Handled = true;
        InvalidateVisual();
    }
}

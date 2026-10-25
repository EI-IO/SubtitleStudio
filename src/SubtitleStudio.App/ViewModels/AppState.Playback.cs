using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SubtitleStudio.App.Services;

namespace SubtitleStudio.App.ViewModels;

/// <summary>预览播放：勾选了哪几条轨道，就混哪几条。</summary>
public sealed partial class AppState
{
    private readonly AudioPlayback _playback = new();
    private DispatcherTimer? _playTimer;

    /// <summary>解码任务的取消源：换文件 / 停止 / 重新播放时中断上一次解码。</summary>
    private CancellationTokenSource? _prepareCts;

    /// <summary>超过这个时长只解开头一段做预览播放，避免临时 wav 占用几个 GB。</summary>
    private const double MaxPreviewDecodeSeconds = 30 * 60;

    [ObservableProperty] private bool _isPlaying;

    /// <summary>播放按钮上的字。</summary>
    [ObservableProperty] private string _playText = Loc.T("Play.Play");

    /// <summary>切歌/换文件时把声音停掉，并中断可能还在跑的解码。</summary>
    public void StopPlayback()
    {
        try
        {
            _prepareCts?.Cancel();
            _playback.Stop();
        }
        catch
        {
            // 设备异常忽略
        }

        _playTimer?.Stop();
        IsPlaying = false;
        PlayText = Loc.T("Play.Play");
    }

    /// <summary>释放解码出来的临时文件（换文件、退出时）。</summary>
    public void ResetPlayback()
    {
        StopPlayback();
        _playback.Dispose();
    }

    /// <summary>勾选音轨变化后，之前混好的 wav 已经不对了：停掉播放并丢弃，下次重新解码。</summary>
    public void InvalidatePlaybackMix()
    {
        // 解码还在进行时也要打断：那次解出来的是旧勾选
        if (!_playback.IsReady && !IsPlaying && _prepareCts is null)
            return;

        var wasActive = IsPlaying || _prepareCts is not null;
        StopPlayback();
        _playback.ReleasePrepared();

        // 只有真的打断了播放/解码才提示；暂停时丢掉缓存不用打扰用户
        if (wasActive)
            Status = Loc.T("Status.PlaySelectionChanged");
    }

    public async Task TogglePlayAsync()
    {
        if (Media is null)
            return;

        CancellationTokenSource? prepareCts = null;

        try
        {
            if (_playback.IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
                PlayText = Loc.T("Play.Play");
                Status = Loc.T("Status.Paused");
                return;
            }

            if (!_playback.IsReady)
            {
                var indexes = Tracks.Where(t => t.ExportEnabled).Select(t => t.Info.Index).ToList();
                if (indexes.Count == 0)
                {
                    Status = Loc.T("Status.NoTrackForPlayback");
                    return;
                }

                BusyText = Loc.T("Busy.Decoding");
                Status = Loc.T("Busy.Decoding");

                // 换一个取消源：上一次播放/换文件遗留的解码直接打断
                _prepareCts?.Cancel();
                _prepareCts?.Dispose();
                prepareCts = new CancellationTokenSource();
                _prepareCts = prepareCts;

                // 超长媒体只解前 30 分钟，临时 wav 不至于吃掉几个 GB
                var capped = Duration > MaxPreviewDecodeSeconds;
                try
                {
                    await _playback.PrepareAsync(Media.Path, indexes, prepareCts.Token,
                        capped ? MaxPreviewDecodeSeconds : null);

                    // 取消可能刚好发生在 ffmpeg 收尾之后：这份混音是按旧勾选解的，不能拿来播
                    if (prepareCts.IsCancellationRequested)
                    {
                        _playback.ReleasePrepared();
                        if (ReferenceEquals(_prepareCts, prepareCts))
                            BusyText = string.Empty;
                        return;
                    }
                }
                finally
                {
                    // 只有自己还是当前任务才清空，别把后一次解码的取消源顶掉
                    if (ReferenceEquals(_prepareCts, prepareCts))
                        _prepareCts = null;
                    prepareCts.Dispose();
                }

                BusyText = string.Empty;

                if (CurrentTime > 0 && CurrentTime < _playback.Duration)
                    _playback.Position = CurrentTime;

                _playback.Play();
                IsPlaying = true;
                PlayText = Loc.T("Play.Pause");
                Status = capped
                    ? Loc.T("Status.PlayingCapped", Loc.T("Status.Playing"), MaxPreviewDecodeSeconds / 60)
                    : Loc.T("Status.Playing");
                StartPlaybackTimer();
                return;
            }

            // 暂停中拖过红线的话，从红线处接着放（没拖过时两者本来就相等，同步无副作用）
            if (Math.Abs(_playback.Position - CurrentTime) > 0.05 && CurrentTime < _playback.Duration)
                _playback.Position = CurrentTime;

            _playback.Play();
            IsPlaying = true;
            PlayText = Loc.T("Play.Pause");
            Status = Loc.T("Status.Playing");
            StartPlaybackTimer();
        }
        catch (OperationCanceledException)
        {
            // 解码被换文件/停止打断，不算错误；被后一次解码顶替时别清掉它的提示
            if (_prepareCts is null || ReferenceEquals(_prepareCts, prepareCts))
                BusyText = string.Empty;
        }
        catch (Exception ex)
        {
            ReportError(Loc.T("Status.PlayFailed", ex.Message));
            StopPlayback();
        }
    }

    private void StartPlaybackTimer()
    {
        _playTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _playTimer.Tick -= OnPlaybackTick;
        _playTimer.Tick += OnPlaybackTick;
        _playTimer.Start();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!_playback.IsPlaying)
        {
            _playTimer?.Stop();
            IsPlaying = false;
            PlayText = Loc.T("Play.Play");

            if (_playback.IsReady && _playback.Position >= _playback.Duration - 0.1)
                Status = Loc.T("Status.PlayEnded");
            return;
        }

        var position = _playback.Position;
        if (Math.Abs(position - CurrentTime) > 0.02)
        {
            CurrentTime = position;
            SchedulePreview();
        }
    }
}

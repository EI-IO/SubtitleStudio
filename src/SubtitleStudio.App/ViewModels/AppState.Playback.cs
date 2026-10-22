using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SubtitleStudio.App.Services;

namespace SubtitleStudio.App.ViewModels;

/// <summary>预览播放：勾选了哪几条轨道，就混哪几条。</summary>
public sealed partial class AppState
{
    private readonly AudioPlayback _playback = new();
    private DispatcherTimer? _playTimer;

    [ObservableProperty] private bool _isPlaying;

    /// <summary>播放按钮上的字。</summary>
    [ObservableProperty] private string _playText = "播放";

    /// <summary>切歌/换文件时把声音停掉。</summary>
    public void StopPlayback()
    {
        try
        {
            _playback.Stop();
        }
        catch
        {
            // 设备异常忽略
        }

        _playTimer?.Stop();
        IsPlaying = false;
        PlayText = "播放";
    }

    /// <summary>释放解码出来的临时文件（换文件、退出时）。</summary>
    public void ResetPlayback()
    {
        StopPlayback();
        _playback.Dispose();
    }

    public async Task TogglePlayAsync()
    {
        if (Media is null)
            return;

        try
        {
            if (_playback.IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
                PlayText = "播放";
                Status = "已暂停";
                return;
            }

            if (!_playback.IsReady)
            {
                var indexes = Tracks.Where(t => t.ExportEnabled).Select(t => t.Info.Index).ToList();
                if (indexes.Count == 0)
                {
                    Status = "没有勾选任何音轨，勾上一条再播放";
                    return;
                }

                BusyText = "解码音频…";
                Status = "解码音频…";
                await _playback.PrepareAsync(Media.Path, indexes, CancellationToken.None);
                BusyText = string.Empty;

                if (CurrentTime > 0)
                    _playback.Position = CurrentTime;
            }

            _playback.Play();
            IsPlaying = true;
            PlayText = "暂停";
            Status = "播放中（只放勾选的音轨）";
            StartPlaybackTimer();
        }
        catch (Exception ex)
        {
            ReportError("播放失败：" + ex.Message);
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
            PlayText = "播放";

            if (_playback.IsReady && _playback.Position >= _playback.Duration - 0.1)
                Status = "播放结束";
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

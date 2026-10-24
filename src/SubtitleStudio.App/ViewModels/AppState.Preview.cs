using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Export;

namespace SubtitleStudio.App.ViewModels;

/// <summary>预览与视图缩放：字幕外观改动、播放头移动都通过这里重画预览。</summary>
public sealed partial class AppState
{
    /// <summary>渲染节流：连续拖动时只在停下来 80ms 后重画一次。</summary>
    private DispatcherTimer? _previewTimer;

    private SubtitleLayerRenderer? _renderer;

    /// <summary>渲染器的画布尺寸；换了分辨率不同的视频要重建渲染器。</summary>
    private (int W, int H) _rendererSize;

    public void Initialize()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer!.Stop();
            RenderPreview();
        };
        // 渲染器不在建了：RenderPreview 会按视频分辨率懒建/重建

        // 从磁盘读到的设置要落到界面上（上次选的后端、本地模型）
        // 自定义提供商可能已被删（配置在别的机器上改过），指着空气就回退到本地
        if (Settings.Backend == Core.Asr.AsrBackend.Custom && Settings.ActiveCustomProvider() is null)
            Settings.Backend = Core.Asr.AsrBackend.Local;
        Backend = Settings.Backend;
        RefreshModels();
        OnPropertyChanged(nameof(ModelLabel));
        AudioInfo = DescribeBackend();

        // 上次退出时没下完的模型接着续传（.part 还在），进度在设置窗口里看
        var resumed = ModelDownloads.ResumeIncomplete();
        if (resumed > 0)
            Status = Loc.T("Status.ResumeDownloads", resumed);

        RenderPreview();
    }

    partial void OnCurrentTimeChanged(double value) => SchedulePreview();

    partial void OnPreviewImageChanged(Bitmap? oldValue, Bitmap? newValue) => oldValue?.Dispose();

    /// <summary>字幕样式变了，重画预览。</summary>
    public void InvalidateStyle()
    {
        OnPropertyChanged(nameof(ExportLayoutHint));
        SchedulePreview();
    }

    public void SchedulePreview()
    {
        if (_previewTimer is null)
            return;

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        // 画布跟着视频分辨率走：尺寸变了就重建渲染器（Initialize 里那只建一次，换视频后对不上）
        var size = (PreviewWidth, PreviewHeight);
        if (_renderer is null || _rendererSize != size)
        {
            _renderer = new SubtitleLayerRenderer(size.Item1, size.Item2);
            _rendererSize = size;
        }

        try
        {
            var png = _renderer.RenderToPng(CueModels(), Style, CurrentTime);
            using var stream = new MemoryStream(png);
            PreviewImage = new Bitmap(stream);
        }
        catch
        {
            // 预览失败不应打断编辑
        }
    }

    /// <summary>把整条时间轴缩放到可视宽度。</summary>
    public void FitZoom(double viewWidth = 900)
    {
        if (Duration <= 0.1)
        {
            PixelsPerSecond = 100;
            return;
        }

        PixelsPerSecond = Math.Clamp((viewWidth - 24) / Duration, 8, 600);
        ScrollSeconds = 0;
    }

    /// <summary>跳到某个时间点，并选中该处的字幕；正在播放时声音也会跟着跳过去。</summary>
    public void SeekTo(double seconds)
    {
        var max = Math.Max(0, Duration - 0.01);
        CurrentTime = Math.Clamp(seconds, 0, max);

        // 播放中拖红线必须同步播放位置，否则播放定时器每 80ms 就把播放头拉回原处
        if (_playback.IsPlaying && CurrentTime < _playback.Duration)
            _playback.Position = CurrentTime;

        // 只认「入点已过、未过出点」的那条，避免跳进空隙时选中上一条
        SelectedCue = Cues.FirstOrDefault(c => CurrentTime >= c.Start && CurrentTime <= c.End);
    }
}

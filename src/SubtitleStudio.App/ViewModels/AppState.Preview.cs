using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SubtitleStudio.Core.Export;

namespace SubtitleStudio.App.ViewModels;

/// <summary>预览与视图缩放：字幕外观改动、播放头移动都通过这里重画预览。</summary>
public sealed partial class AppState
{
    /// <summary>渲染节流：连续拖动时只在停下来 80ms 后重画一次。</summary>
    private DispatcherTimer? _previewTimer;

    private SubtitleLayerRenderer? _renderer;

    public void Initialize()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer!.Stop();
            RenderPreview();
        };
        _renderer = new SubtitleLayerRenderer(PreviewWidth, PreviewHeight);

        // 从磁盘读到的设置要落到界面上（上次选的后端、本地模型）
        Backend = Settings.Backend;
        RefreshModels();
        OnPropertyChanged(nameof(ModelLabel));
        AudioInfo = Backend switch
        {
            Core.Asr.AsrBackend.Local => "本地 Whisper · " + (SelectedModel?.Display ?? "自动"),
            Core.Asr.AsrBackend.SiliconFlow => "硅基流动 · " + ShortModel(Settings.SiliconFlowModel),
            _ => "天翼星辰 · 短语音识别"
        };

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
        if (_renderer is null)
            return;

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

    /// <summary>跳到某个时间点，并选中该处的字幕。</summary>
    public void SeekTo(double seconds)
    {
        var max = Math.Max(0, Duration - 0.01);
        CurrentTime = Math.Clamp(seconds, 0, max);

        // 只认「入点已过、未过出点」的那条，避免跳进空隙时选中上一条
        SelectedCue = Cues.FirstOrDefault(c => CurrentTime >= c.Start && CurrentTime <= c.End);
    }
}

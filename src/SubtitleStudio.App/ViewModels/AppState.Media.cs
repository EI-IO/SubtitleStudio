using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.App.ViewModels;

/// <summary>打开文件、建立音轨列表、后台生成波形。</summary>
public sealed partial class AppState
{
    /// <summary>打开文件对话框 → 加载（识别由用户点「开始识别」启动）。</summary>
    public async Task OpenFileAsync()
    {
        var path = PickMediaFile is null ? null : await PickMediaFile();
        if (!string.IsNullOrEmpty(path))
            await LoadMediaAsync(path);
    }

    public async Task LoadMediaAsync(string path)
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            BusyText = Loc.T("Busy.ReadingMedia");
            Progress = 0;
            ClearError();

            // 换文件先把正在放的声音停掉、解码缓存丢掉
            ResetPlayback();

            var info = await MediaProbe.ProbeAsync(path);
            Media = info;
            ResetTracks();
            FitZoom();
            SchedulePreview();

            if (!info.HasAudio)
            {
                Status = Loc.T("Status.NoAudio");
                return;
            }

            BuildTracks(info);
            SelectedTrack = Tracks[0];
            // 新文件不自动识别：由用户点「开始识别」来启动（只跑勾选的音轨）
            HasRecognized = false;
            Status = Loc.T("Status.MediaLoaded", DescribeMedia(info), Tracks.Count);

            // 波形是后台任务，不挡住界面
            IsBusy = false;
            BusyText = string.Empty;
            foreach (var track in Tracks)
                _ = LoadWaveformAsync(path, track, info.DurationSeconds);
        }
        catch (Exception ex)
        {
            Status = Loc.T("Status.OpenFailed", ex.Message);
            ReportError(Loc.T("Status.OpenFailed", ex.Message));
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>「文件名 · 分辨率/纯音频 · 时长」；Core 里的是中文，这里按当前语言拼。</summary>
    private static string DescribeMedia(MediaInfo info)
    {
        var size = info.HasVideo ? $"{info.Width}x{info.Height}" : Loc.T("Media.PureAudio");
        return Loc.T("Media.Summary", info.DisplayName, size, info.DurationSeconds);
    }

    private void ResetTracks()
    {
        Tracks.Clear();
        SelectedTrack = null;
        CurrentTime = 0;
        SelectedCue = null;
    }

    /// <summary>按探测到的音频流建音轨；probe 没给流列表时用主音轨兜底。</summary>
    private void BuildTracks(MediaInfo info)
    {
        foreach (var stream in info.AudioStreams)
            Tracks.Add(new TrackItem(stream));

        if (Tracks.Count == 0)
            Tracks.Add(new TrackItem(new AudioStreamInfo(info.AudioStreamIndex, 1, "audio", 0, null, null)));
    }

    private async Task LoadWaveformAsync(string path, TrackItem track, double duration)
    {
        try
        {
            var wave = await Task.Run(() => WaveformBuilder.BuildAsync(
                path, duration, peaksPerSecond: 100, progress: null,
                cancellationToken: default, audioStreamIndex: track.Info.Index));

            track.Waveform = wave;
            if (ReferenceEquals(track, SelectedTrack))
                OnPropertyChanged(nameof(Waveform));

            OnPropertyChanged(nameof(Tracks));
        }
        catch
        {
            // 波形是锦上添花，失败不影响主流程
        }
    }
}

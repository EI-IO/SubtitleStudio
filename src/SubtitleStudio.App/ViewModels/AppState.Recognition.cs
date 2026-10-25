using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;
using SubtitleStudio.Core.Subtitles;

namespace SubtitleStudio.App.ViewModels;

/// <summary>识别流程：逐条音轨抽音轨 → 识别 → 断句 → 填入该轨。</summary>
public sealed partial class AppState
{
    private CancellationTokenSource? _recognitionCts;
    private volatile bool _stopRequested;

    /// <summary>
    /// 请求停止识别。识别跑在 CLI 子进程里，所以这里可以直接杀掉进程立即停止，
    /// 不必等当前音轨跑完。
    /// </summary>
    public void CancelRecognition()
    {
        if (!IsBusy)
            return;

        _stopRequested = true;
        BusyText = Loc.T("Status.Stopping");
        Status = Loc.T("Status.Stopping");
        _recognitionCts?.Cancel();
    }

    /// <summary>逐条识别勾选的音轨（没勾选的跳过，原有字幕保留），每条音轨各自得到一组字幕。</summary>
    public async Task RunRecognitionAsync()
    {
        if (Media is null || IsBusy)
            return;

        // 只跑勾选的音轨：多轨素材里经常有背景音乐轨，识别它们纯属浪费时间
        var tracks = Tracks.Where(t => t.ExportEnabled).ToList();
        if (tracks.Count == 0)
        {
            Status = Loc.T("Status.RecognitionNoTracks");
            return;
        }

        var isLocal = Backend == AsrBackend.Local;
        var modelPath = isLocal ? LocalModelPath() : null;

        if (isLocal && modelPath is null)
        {
            Status = Loc.T("Status.NoModel");
            AudioInfo = Loc.T("AudioInfo.LocalMissing");
            return;
        }

        if (!isLocal && !Settings.IsReady(Backend))
        {
            // 没填凭证：先问一次要不要去设置（用户可能当场就填好）
            if (RequestCredentialAsync is not null && await RequestCredentialAsync(Backend))
            {
                isLocal = Backend == AsrBackend.Local;
                modelPath = isLocal ? LocalModelPath() : null;
            }
            else
            {
                Status = Loc.T("Status.NeedCredential", MissingHintText(Backend));
                return;
            }
        }

        try
        {
            IsBusy = true;
            Progress = 0;
            _stopRequested = false;
            _recognitionCts?.Dispose();
            _recognitionCts = new CancellationTokenSource();
            var token = _recognitionCts.Token;

            AudioInfo = isLocal
                ? Loc.T("AudioInfo.LocalWhisper", ModelCatalog.DisplayName(modelPath!))
                : ModelLabel;

            for (var i = 0; i < tracks.Count; i++)
            {
                if (_stopRequested)
                    break;

                await RecognizeTrackAsync(tracks[i], i, tracks.Count, isLocal, modelPath, token);
            }

            if (_stopRequested)
            {
                Progress = 0;
                Status = Loc.T("Status.RecognitionStopped");
                // 中途停下不算「识别过」：按钮回到「开始识别」，下次从勾选的轨重新跑
                HasRecognized = false;
                Summarize(tracks, quiet: true);
                return;
            }

            Progress = 1;
            HasRecognized = true;
            Summarize(tracks);
        }
        catch (OperationCanceledException)
        {
            Status = Loc.T("Status.RecognitionStopped");
            HasRecognized = false;
            Summarize(tracks, quiet: true);
        }
        catch (Exception ex)
        {
            Status = Loc.T("Status.RecognitionFailed", ex.Message);
            ReportError(Loc.T("Status.RecognitionFailed", TlsDiagnostics.Describe(ex)));
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>单条音轨的完整识别流程；失败只影响这一条。</summary>
    private async Task RecognizeTrackAsync(TrackItem track, int index, int total,
        bool isLocal, string? modelPath, CancellationToken token)
    {
        var media = Media!;
        var slice = 1.0 / total;
        var baseProgress = index * slice;
        var temp = TempWorkspace.CreateFilePath(media.Path, ".wav");

        try
        {
            track.Status = Loc.T("Track.Extracting");
            track.Progress = 0;
            BusyText = Loc.T("Busy.ExtractingTrack", track.LabelShort);

            var extractProgress = new Progress<double>(p =>
            {
                track.Progress = p * 0.25;
                Progress = baseProgress + p * 0.25 * slice;
            });

            await AudioExtractor.ExtractWavAsync(
                media.Path, temp, media.DurationSeconds, extractProgress,
                cancellationToken: token, audioStreamIndex: track.Info.Index,
                denoise: Settings.Denoise);

            token.ThrowIfCancellationRequested();

            track.Status = Loc.T("Track.Recognizing");
            BusyText = Loc.T("Busy.RecognizingWith", track.LabelShort,
                isLocal ? Path.GetFileName(modelPath!) : ModelLabel);

            var asrProgress = new Progress<AsrProgress>(p =>
            {
                track.Progress = 0.25 + p.Fraction * 0.75;
                Progress = baseProgress + (0.25 + p.Fraction * 0.75) * slice;
                if (!string.IsNullOrEmpty(p.Message))
                    BusyText = $"{track.LabelShort}: {p.Message}";
            });

            // 本地识别在 CLI 子进程里跑，取消令牌能直接杀掉进程（真停止）；
            // 云端识别本来就是 HTTP 请求，也能被令牌打断。
            var result = await TranscribeAsync(temp, isLocal, modelPath, asrProgress, token);

            // 先过滤幻觉，再补标点，最后断句
            var cleaned = HallucinationFilter.Apply(result.Segments);
            var punctuated = PunctuationRestorer.Restore(cleaned);
            var cues = CueSegmenter.Segment(punctuated, Style.MaxCharsPerLine);
            FillTrack(track, cues);
            track.Language = result.Language;
            var dropped = result.Segments.Count - cleaned.Count;
            track.Status = cues.Count == 0
                ? Loc.T("Track.None")
                : dropped > 0
                    ? Loc.T("Track.DoneFiltered", cues.Count, result.Language, dropped)
                    : Loc.T("Track.Done", cues.Count, result.Language);
            track.Progress = 1;
        }
        catch (OperationCanceledException)
        {
            track.Status = Loc.T("Track.Stopped");
            track.Progress = 0;
            throw;
        }
        catch (Exception ex)
        {
            // 取消时原生库收尾可能抛别的异常，不算失败
            if (token.IsCancellationRequested)
            {
                track.Status = Loc.T("Track.Stopped");
                track.Progress = 0;
                throw new OperationCanceledException(token);
            }

            track.Status = Loc.T("Track.Failed", ex.Message);
            ReportError($"{track.LabelShort}: {TlsDiagnostics.Describe(ex)}");
        }
        finally
        {
            TempWorkspace.TryDelete(temp);
        }
    }

    private async Task<AsrResult> TranscribeAsync(string wavPath,
        bool isLocal, string? modelPath, IProgress<AsrProgress> progress, CancellationToken token)
    {
        if (Backend == AsrBackend.TeleAi)
        {
            // 短语音识别：HTTPS + Bearer API Key，长音频自动切片
            return await TeleAiClient.TranscribeAsync(
                Settings.TeleAiApiKey,
                wavPath,
                Settings.TeleAiBaseUrl,
                progress,
                token);
        }

        if (Backend == AsrBackend.SiliconFlow)
        {
            using var remote = new RemoteAsrProvider(
                Loc.T("Menu.SiliconFlow"),
                Settings.SiliconFlowBaseUrl,
                Settings.SiliconFlowModel,
                Settings.SiliconFlowApiKey);
            return await remote.TranscribeAsync(wavPath, new AsrOptions(), progress, token);
        }

        if (Backend == AsrBackend.Custom)
        {
            // 自定义提供商走的也是 OpenAI 兼容的 /audio/transcriptions，与硅基流动同一条通路
            var provider = Settings.ActiveCustomProvider()
                ?? throw new InvalidOperationException(Loc.T("Label.CustomProviderMissing"));
            using var remote = new RemoteAsrProvider(
                provider.Name,
                provider.BaseUrl,
                provider.Model,
                provider.ApiKey);
            return await remote.TranscribeAsync(wavPath, new AsrOptions(), progress, token);
        }

        // 走子进程：真能随时杀掉
        var result = await CliBridge.TranscribeAsync(wavPath, modelPath!, null, progress, token);
        var segments = result.Segments
            .Select(s => new AsrSegment(s.Text, s.Start, s.End))
            .ToList();
        return new AsrResult(result.Language ?? "auto", segments);
    }

    /// <summary>全部音轨跑完后，选中第一条有字幕的轨道并写一句总结。</summary>
    private void Summarize(List<TrackItem> tracks, bool quiet = false)
    {
        var ok = tracks.Count(t => t.Cues.Count > 0);
        var totalCues = tracks.Sum(t => t.Cues.Count);
        var first = tracks.FirstOrDefault(t => t.Cues.Count > 0) ?? tracks[0];

        SelectedTrack = first;

        if (quiet)
            return;

        Status = Loc.T("Status.TrackSummary", first.LabelShort, first.Cues.Count, first.Language ?? "?");

        if (tracks.Count > 1)
            Status = Loc.T("Status.TrackSummaryMulti", Status, ok, tracks.Count, totalCues);
    }

    /// <summary>新建一条字幕并接上编辑回调（识别结果和手动添加共用）。</summary>
    private CueItem NewCue(SubtitleCue model)
    {
        var item = new CueItem(model);
        item.Edited += (_, _) =>
        {
            NotifyCuesChanged();
            SchedulePreview();
        };
        return item;
    }

    /// <summary>把断句结果填进指定音轨，并接上编辑回调。</summary>
    private void FillTrack(TrackItem track, IEnumerable<SubtitleCue> cues)
    {
        track.Cues.Clear();
        foreach (var cue in cues)
            track.Cues.Add(NewCue(cue));

        if (ReferenceEquals(track, SelectedTrack))
        {
            SelectedCue = track.Cues.FirstOrDefault();
            NotifyCuesChanged();
            SchedulePreview();
        }

        // 非选中轨道的字幕也要让时间轴重画
        OnPropertyChanged(nameof(Tracks));
    }

    /// <summary>
    /// 在时间轴的空白处新增一条字幕：从点击位置开始，默认 2 秒；
    /// 后面紧挨着字幕就截到它的开始处，不覆盖已有的块。
    /// </summary>
    public CueItem? AddCueAt(double time)
    {
        var track = SelectedTrack;
        if (track is null)
            return null;

        var limit = Duration > 0 ? Duration : double.MaxValue;
        var start = Math.Clamp(time, 0, Math.Max(0, limit - 0.1));

        // 点击附近可能紧贴前后字幕：退到前一条结束处，不压到已有的块
        var previousEnd = track.Cues.Where(c => c.Start < start)
            .Select(c => c.End)
            .DefaultIfEmpty(0)
            .Max();
        start = Math.Max(start, previousEnd);

        var nextStart = track.Cues.Where(c => c.Start >= start)
            .Select(c => (double?)c.Start)
            .Min();

        // 前后只隔了不到 0.1 秒，塞不下最短字幕，放弃这次新增
        var maxEnd = nextStart ?? limit;
        if (maxEnd - start < 0.1)
            return null;

        var end = Math.Max(start + 0.1, Math.Min(start + 2, maxEnd));

        var item = NewCue(new SubtitleCue { Start = start, End = end });

        // 列表和导出都按时间顺序读 Cues，插入时保持有序
        var index = 0;
        while (index < track.Cues.Count && track.Cues[index].Start <= start)
            index++;
        track.Cues.Insert(index, item);

        SelectedCue = item;
        NotifyCuesChanged();
        SchedulePreview();
        return item;
    }

    /// <summary>删掉一条字幕（时间轴右键菜单），返回是否真的删掉了。</summary>
    public bool RemoveCue(CueItem cue)
    {
        foreach (var track in Tracks)
        {
            if (!track.Cues.Remove(cue))
                continue;

            if (ReferenceEquals(SelectedCue, cue))
                SelectedCue = track.Cues.FirstOrDefault(c => c.Start >= cue.Start) ?? track.Cues.LastOrDefault();

            NotifyCuesChanged();
            SchedulePreview();
            return true;
        }

        return false;
    }

    /// <summary>覆盖当前选中音轨的字幕。</summary>
    public void ReplaceCues(IEnumerable<SubtitleCue> cues)
    {
        if (SelectedTrack is not null)
            FillTrack(SelectedTrack, cues);
    }

    public void ClearCues()
    {
        if (SelectedTrack is null)
            return;

        SelectedTrack.Cues.Clear();
        SelectedCue = null;
        SelectedTrack.Status = Loc.T("Track.Cleared");
        NotifyCuesChanged();
        Status = Loc.T("Status.Cleared", SelectedTrack.LabelShort);
        SchedulePreview();
    }

    /// <summary>选中某条音轨（时间轴上点击轨道时调用）。</summary>
    public void SelectTrack(TrackItem track)
    {
        if (!ReferenceEquals(SelectedTrack, track))
            SelectedTrack = track;
    }

    /// <summary>从时间轴上删掉整条音轨（连同它的字幕），返回剩余轨道数。</summary>
    public int RemoveTrack(TrackItem track)
    {
        if (!Tracks.Remove(track))
            return Tracks.Count;

        if (ReferenceEquals(SelectedTrack, track))
        {
            SelectedTrack = Tracks.FirstOrDefault();
            SelectedCue = SelectedTrack?.Cues.FirstOrDefault();
        }

        // 刚删掉的音轨可能已经混进播放缓存里
        InvalidatePlaybackMix();
        NotifyCuesChanged();
        SchedulePreview();
        return Tracks.Count;
    }

    /// <summary>当前音轨的字幕模型视图，预览与导出共用。</summary>
    public IReadOnlyList<SubtitleCue> CueModels() => Cues.Select(c => c.Model).ToList();

    /// <summary>多音轨时给导出文件名加上轨号后缀。</summary>
    private string TrackSuffix() =>
        Tracks.Count > 1 && SelectedTrack is { } track ? $"-t{track.Number}" : string.Empty;
}

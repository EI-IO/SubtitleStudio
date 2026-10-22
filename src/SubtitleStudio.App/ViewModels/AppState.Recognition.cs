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
        BusyText = "正在停止…";
        Status = "正在停止…";
        _recognitionCts?.Cancel();
    }

    /// <summary>逐条音轨识别，每条音轨各自得到一组字幕。</summary>
    public async Task RunRecognitionAsync()
    {
        if (Media is null || IsBusy)
            return;

        var tracks = Tracks.ToList();
        if (tracks.Count == 0)
            return;

        var isLocal = Backend == AsrBackend.Local;
        var modelPath = isLocal ? LocalModelPath() : null;

        if (isLocal && modelPath is null)
        {
            Status = "没有找到模型：把 ggml-*.bin 放进 models 文件夹";
            AudioInfo = "本地 · 缺少模型";
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
                Status = Settings.MissingHint(Backend) + "，点标题栏的「设置」填一下";
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
                ? "本地 Whisper · " + ModelCatalog.DisplayName(modelPath!)
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
                Status = "识别已停止（已完成的音轨保留）";
                Summarize(tracks, quiet: true);
                return;
            }

            Progress = 1;
            Summarize(tracks);
        }
        catch (OperationCanceledException)
        {
            Status = "识别已停止（已完成的音轨保留）";
            Summarize(tracks, quiet: true);
        }
        catch (Exception ex)
        {
            Status = "识别失败：" + ex.Message;
            ReportError("识别失败：" + TlsDiagnostics.Describe(ex));
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
            track.Status = "抽取音轨…";
            track.Progress = 0;
            BusyText = $"{track.Info.Number}：抽取音轨…";

            var extractProgress = new Progress<double>(p =>
            {
                track.Progress = p * 0.25;
                Progress = baseProgress + p * 0.25 * slice;
            });

            await AudioExtractor.ExtractWavAsync(
                media.Path, temp, media.DurationSeconds, extractProgress,
                cancellationToken: token, audioStreamIndex: track.Info.Index);

            token.ThrowIfCancellationRequested();

            track.Status = "识别中…";
            BusyText = isLocal
                ? $"{track.Info.Number}：识别中（{Path.GetFileName(modelPath!)}）…"
                : $"{track.Info.Number}：识别中（{ModelLabel}）…";

            var asrProgress = new Progress<AsrProgress>(p =>
            {
                track.Progress = 0.25 + p.Fraction * 0.75;
                Progress = baseProgress + (0.25 + p.Fraction * 0.75) * slice;
                if (!string.IsNullOrEmpty(p.Message))
                    BusyText = $"{track.Info.Number}：{p.Message}";
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
                ? "没有识别到语音"
                : dropped > 0
                    ? $"完成 · {cues.Count} 条 · {result.Language}（过滤 {dropped} 段幻觉）"
                    : $"完成 · {cues.Count} 条 · {result.Language}";
            track.Progress = 1;
        }
        catch (OperationCanceledException)
        {
            track.Status = "已停止";
            track.Progress = 0;
            throw;
        }
        catch (Exception ex)
        {
            // 取消时原生库收尾可能抛别的异常，不算失败
            if (token.IsCancellationRequested)
            {
                track.Status = "已停止";
                track.Progress = 0;
                throw new OperationCanceledException(token);
            }

            track.Status = "失败：" + ex.Message;
            ReportError($"{track.Info.Number}：{TlsDiagnostics.Describe(ex)}");
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
                "硅基流动",
                Settings.SiliconFlowBaseUrl,
                Settings.SiliconFlowModel,
                Settings.SiliconFlowApiKey);
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

        Status = $"{first.Info.Number} · {first.Cues.Count} 条字幕 · 语言 {first.Language ?? "?"}";

        if (tracks.Count > 1)
            Status += $"（共 {ok}/{tracks.Count} 条音轨、{totalCues} 条字幕）";
    }

    /// <summary>把断句结果填进指定音轨，并接上编辑回调。</summary>
    private void FillTrack(TrackItem track, IEnumerable<SubtitleCue> cues)
    {
        track.Cues.Clear();
        foreach (var cue in cues)
        {
            var item = new CueItem(cue);
            item.Edited += (_, _) =>
            {
                NotifyCuesChanged();
                SchedulePreview();
            };
            track.Cues.Add(item);
        }

        if (ReferenceEquals(track, SelectedTrack))
        {
            SelectedCue = track.Cues.FirstOrDefault();
            NotifyCuesChanged();
            SchedulePreview();
        }

        // 非选中轨道的字幕也要让时间轴重画
        OnPropertyChanged(nameof(Tracks));
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
        SelectedTrack.Status = "已清空";
        NotifyCuesChanged();
        Status = $"{SelectedTrack.Info.Number} · 已清空字幕";
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

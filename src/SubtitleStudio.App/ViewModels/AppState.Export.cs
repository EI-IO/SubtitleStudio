using SubtitleStudio.App.Services;
using SubtitleStudio.Core.Export;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Subtitles;

namespace SubtitleStudio.App.ViewModels;

/// <summary>导出：透明字幕视频（按勾选的音轨一轨一段）、字幕文件、单轨音频。</summary>
public sealed partial class AppState
{
    private static string? _lastExportDir;

    /// <summary>勾选了「导出」且真的识别出字幕的音轨。</summary>
    private List<TrackItem> ExportTargets() =>
        Tracks.Where(t => t.ExportEnabled && t.Cues.Count > 0).ToList();

    public async Task ExportOverlayAsync(OverlayFormat format)
    {
        if (IsExporting)
            return;

        var targets = ExportTargets();
        if (targets.Count == 0)
        {
            Status = Tracks.Count == 0
                ? Loc.T("Export.NoCues")
                : Loc.T("Export.NoTracks");
            return;
        }

        var dir = await PickExportFolderAsync();
        if (dir is null)
            return;

        var quality = Quality.Value;
        var multi = Tracks.Count > 1;
        var fps = Media?.Fps ?? 30;
        var duration = Math.Max(0.5, Duration);
        var written = new List<string>();

        try
        {
            IsExporting = true;
            ExportProgress = 0;

            for (var i = 0; i < targets.Count; i++)
            {
                var track = targets[i];
                var cues = track.Cues.Select(c => c.Model).ToList();
                if (cues.Count == 0)
                    continue;

                // 每段视频的尺寸只跟这条轨道的字幕有关：宽度贴最长一行，高度贴行数
                var layout = OverlayLayout.Measure(cues, Style, quality);
                var name = ExportStem() + (multi ? $"-t{track.Number}" : string.Empty) +
                           "-overlay" + OverlayVideoExporter.ExtensionOf(format);
                var output = Path.Combine(dir, name);

                ExportText = multi
                    ? Loc.T("Export.PreparingTrack", track.LabelShort)
                    : Loc.T("Export.Preparing");
                var index = i;
                var progress = new Progress<ExportProgress>(p =>
                {
                    // 导出可能在收尾阶段遇到窗口关闭，界面属性更新不能把程序带崩
                    try
                    {
                        ExportProgress = (index + p.Fraction) / targets.Count;
                        if (!string.IsNullOrEmpty(p.Message))
                            ExportText = multi ? $"{track.LabelShort}: {p.Message}" : p.Message;
                    }
                    catch
                    {
                        // 忽略
                    }
                });

                var request = new OverlayExportRequest(
                    output, format, layout.Width, layout.Height, fps, duration, cues, Style)
                {
                    RenderScale = layout.Scale,
                    CenterVertically = true
                };

                await OverlayVideoExporter.ExportAsync(request, progress);
                written.Add(Path.GetFileName(output));
            }

            ExportProgress = 1;
            ExportText = Loc.T("Export.Done");
            Status = written.Count == 1
                ? Loc.T("Export.SavedOne", Path.Combine(dir, written[0]))
                : Loc.T("Export.SavedMany", written.Count, string.Join(", ", written));
        }
        catch (Exception ex)
        {
            Status = Loc.T("Export.Failed", ex.Message);
            ReportError(Loc.T("Export.Failed", ex.Message));
            ExportText = string.Empty;
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task ExportSubtitlesAsync(string extension)
    {
        var targets = ExportTargets();
        if (targets.Count == 0)
        {
            Status = Loc.T("Export.NoCues");
            return;
        }

        var dir = await PickExportFolderAsync();
        if (dir is null)
            return;

        var ext = extension.TrimStart('.').ToLowerInvariant();
        var multi = Tracks.Count > 1;
        var written = new List<string>();

        try
        {
            foreach (var track in targets)
            {
                var cues = track.Cues.Select(c => c.Model).ToList();
                if (cues.Count == 0)
                    continue;

                var content = ext switch
                {
                    "ass" => SubtitleWriters.WriteAss(cues, Style),
                    "vtt" => SubtitleWriters.WriteVtt(cues),
                    _ => SubtitleWriters.WriteSrt(cues)
                };

                var name = ExportStem() + (multi ? $"-t{track.Number}" : string.Empty) + $".{ext}";
                var path = Path.Combine(dir, name);
                SubtitleWriters.Save(content, path);
                written.Add(Path.GetFileName(path));
            }

            Status = written.Count == 0
                ? Loc.T("Export.NothingToWrite")
                : Loc.T("Export.SavedOne", string.Join(", ", written));
        }
        catch (Exception ex)
        {
            Status = Loc.T("Export.Failed", CrashReporter.Flatten(ex));
            ReportError(Status);
        }
    }

    /// <summary>把单条音轨原样导出成音频文件（默认 m4a）。</summary>
    public async Task ExportTrackAudioAsync(TrackItem track, string extension = AudioExporter.DefaultExtension)
    {
        if (Media is null || track.IsExportingAudio)
            return;

        var dir = await PickExportFolderAsync();
        if (dir is null)
            return;

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var name = ExportStem() + (Tracks.Count > 1 ? $"-t{track.Number}" : string.Empty) + ext;
        var output = Path.Combine(dir, name);

        try
        {
            track.AudioProgress = 0;
            track.Status = Loc.T("Track.ExportingAudio");
            Status = Loc.T("Export.Audioing", track.LabelShort);

            var progress = new Progress<double>(p =>
            {
                try
                {
                    track.AudioProgress = p;
                }
                catch
                {
                    // 忽略
                }
            });

            await AudioExporter.ExportTrackAsync(Media.Path, output, track.Info.Index,
                Math.Max(0.5, Duration), progress);

            track.AudioProgress = -1;
            track.Status = Loc.T("Track.AudioExported");
            Status = Loc.T("Export.AudioSaved", track.LabelShort, output);
        }
        catch (Exception ex)
        {
            track.AudioProgress = -1;
            track.Status = Loc.T("Track.AudioExportFailed");
            Status = Loc.T("Export.AudioFailed", track.LabelShort, ex.Message);
            ReportError(Loc.T("Export.AudioFailed", track.LabelShort, ex.Message));
        }
    }

    /// <summary>导出文件名主干：媒体名（不含扩展名）。</summary>
    private string ExportStem()
    {
        var stem = Path.GetFileNameWithoutExtension(Media?.Path ?? "subtitle");
        return string.IsNullOrWhiteSpace(stem) ? "subtitle" : stem;
    }

    private async Task<string?> PickExportFolderAsync()
    {
        // 自动化 / 命令行场景可以直接指定导出目录（界面里仍然弹选择框）
        var forced = Environment.GetEnvironmentVariable("SUBTITLESTUDIO_EXPORT_DIR");

        string? dir;
        if (!string.IsNullOrWhiteSpace(forced))
        {
            dir = forced;
        }
        else if (PickFolder is not null)
        {
            dir = await PickFolder();
        }
        else
        {
            return _lastExportDir;
        }

        if (string.IsNullOrWhiteSpace(dir))
            return null;

        dir = EnsureWritableDirectory(dir);
        _lastExportDir = dir;
        return dir;
    }

    /// <summary>
    /// 目录写不了（受控文件夹、OneDrive 重定向、权限不足）就按候选列表自动换一个，
    /// 而不是直接失败；候选都不行才抛。
    /// </summary>
    private string EnsureWritableDirectory(string dir)
    {
        if (CanWrite(dir))
            return dir;

        // 先问清楚为什么写不进去：是权限（受控文件夹 / ACL）还是别的
        var cause = ExportDiagnostics.Diagnose(dir);

        var stem = ExportStem();
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SubtitleStudio", "exports", stem),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "SubtitleStudio", stem),
            Path.Combine(AppContext.BaseDirectory, "exports", stem),
            Path.Combine(Path.GetTempPath(), "subtitle-studio", "exports", stem)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !CanWrite(candidate))
                continue;

            var message = Loc.T("Export.DirNotWritable", dir, cause.Explain, candidate);
            Status = Loc.T("Export.AutoMoved", candidate);
            ReportError(message, cause.IsPermission);
            return candidate;
        }

        throw new IOException(Loc.T("Export.NoWritableDir", dir, cause.Explain));
    }

    private string? _lastWriteError;

    private bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".ss-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            _lastWriteError = ex.Message;
            return false;
        }
    }
}

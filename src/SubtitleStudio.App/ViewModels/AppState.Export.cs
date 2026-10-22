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
                ? "还没有字幕可以导出"
                : "没有勾选要导出的音轨（勾选的音轨也可能还没字幕）";
            return;
        }

        var dir = await PickExportFolderAsync();
        if (dir is null)
            return;

        var quality = Quality?.Value ?? OverlayQuality.Hd;
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

                ExportText = multi ? $"{track.Info.Number}：准备导出…" : "准备导出…";
                var index = i;
                var progress = new Progress<ExportProgress>(p =>
                {
                    // 导出可能在收尾阶段遇到窗口关闭，界面属性更新不能把程序带崩
                    try
                    {
                        ExportProgress = (index + p.Fraction) / targets.Count;
                        if (!string.IsNullOrEmpty(p.Message))
                            ExportText = multi ? $"{track.Info.Number}：{p.Message}" : p.Message;
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
            ExportText = "导出完成";
            Status = written.Count == 1
                ? "已导出：" + Path.Combine(dir, written[0])
                : $"已导出 {written.Count} 段：" + string.Join("、", written);
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
            ReportError("导出失败：" + ex.Message);
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
            Status = "还没有字幕可以导出";
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
                ? "没有字幕可写"
                : "已导出：" + string.Join("、", written);
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + CrashReporter.Flatten(ex);
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
            track.Status = "导出音频…";
            Status = $"{track.Info.Number}：导出音频…";

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
            track.Status = "音频已导出";
            Status = $"{track.Info.Number} 音频已导出：" + output;
        }
        catch (Exception ex)
        {
            track.AudioProgress = -1;
            track.Status = "导出音频失败";
            Status = $"{track.Info.Number} 导出音频失败：" + ex.Message;
            ReportError($"{track.Info.Number} 导出音频失败：{ex.Message}");
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

            var message = $"导出目录不可写：{dir}\n原因：{cause.Explain}\n已自动改存到：{candidate}";
            Status = $"已自动改存到 {candidate}";
            ReportError(message, cause.IsPermission);
            return candidate;
        }

        throw new IOException($"导出目录不可写：{dir}（{cause.Explain}），也没有找到可用的替代目录");
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

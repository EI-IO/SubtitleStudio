using System.Diagnostics;
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Core.Export;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;
using SubtitleStudio.Core.Subtitles;

namespace SubtitleStudio.Cli;

/// <summary>命令行主流程：探测 → 抽音轨 → 识别 → 断句 → 写字幕/透明视频。</summary>
internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {        var args2 = Args.Parse(args);

        if (args2.Has("help") || args is { Length: 0 })
        {
            Console.WriteLine("""
                SubtitleStudio CLI
                  --input <file>        输入音频/视频
                  --out <prefix>        输出前缀（默认与输入同名）
                  --track <n>           只处理第 n 条音轨（默认处理全部音轨）
                  --model <path>        whisper ggml 模型（默认自动在 models/ 中查找）
                  --format <list>       导出字幕格式，逗号分隔：srt,ass,vtt（默认 srt,ass,vtt）
                  --lang <code>         语言，默认自动检测
                  --maxchars <n>        每行最大字数（默认 18）
                  --overlay <fmt>       额外导出透明字幕视频：webm | mov | mp4（用第一条音轨的字幕）
                  --width/--height      画布尺寸（默认 1920x1080）
                  --fps <n>             帧率（默认 30）
                  --waveform            顺便生成每条音轨的波形（调试用）
                  --asr-json <path>     只做识别：读 --input 的 wav，把片段写成 JSON（供 GUI 用）
                  --models              列出 models/ 里已有的模型
                  --download <name>     下载模型（如 base / medium / large-v3-turbo）
                  --settings            查看当前设置（配合 --set-backend / --set-key 可写回）
                """);
            return 0;
        }

        if (args2.Has("settings"))
        {
            var current = AsrSettingsStore.Load();
            Console.WriteLine($"[settings] {AsrSettingsStore.FilePath}");
            Console.WriteLine($"  Backend      = {current.Backend}");
            Console.WriteLine($"  LocalModel   = {(string.IsNullOrWhiteSpace(current.LocalModelFile) ? "(自动)" : current.LocalModelFile)}");
            Console.WriteLine($"  SiliconFlow  = {current.SiliconFlowBaseUrl}  model={current.SiliconFlowModel}  key={(string.IsNullOrWhiteSpace(current.SiliconFlowApiKey) ? "(空)" : "已填 " + current.SiliconFlowApiKey.Length + " 字符")}");
            Console.WriteLine($"  TeleAi       = apiKey={(string.IsNullOrWhiteSpace(current.TeleAiApiKey) ? "(空)" : "已填")}  baseUrl={current.TeleAiBaseUrl}");

            var changed = false;
            if (args2.Get("set-backend") is { Length: > 0 } backendText
                && Enum.TryParse<AsrBackend>(backendText, true, out var backend))
            {
                current.Backend = backend;
                changed = true;
            }

            if (args2.Get("set-key") is { Length: > 0 } keyText)
            {
                current.SiliconFlowApiKey = keyText;
                changed = true;
            }

            if (changed)
            {
                AsrSettingsStore.Save(current);
                Console.WriteLine($"[saved] {AsrSettingsStore.FilePath}");
            }

            return 0;
        }

        if (args2.Get("download") is { Length: > 0 } downloadName)
        {
            var suggestion = ModelCatalog.Suggestions.FirstOrDefault(s =>
                s.FileName.Equals(downloadName, StringComparison.OrdinalIgnoreCase)
                || s.Display.Equals(downloadName, StringComparison.OrdinalIgnoreCase)
                || s.FileName.Equals("ggml-" + downloadName + ".bin", StringComparison.OrdinalIgnoreCase));

            if (suggestion is null)
            {
                Console.Error.WriteLine($"没有叫 {downloadName} 的模型。可选：{string.Join(", ", ModelCatalog.Suggestions.Select(s => s.Display))}");
                return 2;
            }

            Console.WriteLine($"[download] {suggestion.Display}（{suggestion.SizeLabel}）→ {ModelCatalog.DefaultModelDirectory}");
            var lastPercent = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                var percent = (int)(p.Fraction * 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    Console.WriteLine($"  {p.SourceName}  {p.Received / 1048576.0:F1} MB  {percent}%");
                }
            });

            try
            {
                var path = await ModelDownloader.DownloadAsync(suggestion, null, progress);
                Console.WriteLine($"[ok] {path}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"下载失败：{ex.Message}");
                return 1;
            }
        }

        if (args2.Has("models"))
        {
            var dir = ModelCatalog.DefaultModelDirectory;
            Console.WriteLine($"[models] {dir}");
            var installed = ModelCatalog.List(dir);
            if (installed.Count == 0)
                Console.WriteLine("  （还没装模型）");
            foreach (var m in installed)
                Console.WriteLine($"  {m.FileName,-34} {m.SizeLabel,8}  {m.Display}");

            Console.WriteLine();
            Console.WriteLine("可下载的模型（放到上面的目录即可）：");
            foreach (var s in ModelCatalog.Suggestions)
            {
                var mark = installed.Any(i => i.FileName == s.FileName) ? "✓" : " ";
                Console.WriteLine($"  {mark} {s.FileName,-36} {s.SizeLabel,8}  {s.Display,-18} {s.Note}");
            }

            Console.WriteLine();
            Console.WriteLine("  下载地址：https://huggingface.co/ggerganov/whisper.cpp/tree/main");
            return 0;
        }

        // 只做识别：GUI 把这一步丢进子进程，这样「停止」可以直接杀掉进程。
        if (args2.Get("asr-json") is { Length: > 0 } jsonPath)
            return await RunAsrJsonAsync(args2, jsonPath);

        var input = args2.Get("input");
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            Console.Error.WriteLine("缺少 --input 或文件不存在");
            return 2;
        }

        var sw = Stopwatch.StartNew();
        var info = await MediaProbe.ProbeAsync(input);
        Console.WriteLine($"[media] {info.Describe()}  音轨={info.AudioStreams.Count}  fps={info.Fps:0.###}");
        foreach (var s in info.AudioStreams)
            Console.WriteLine($"        {s.Describe()}");

        var allTracks = info.AudioStreams.Count > 0
            ? info.AudioStreams.ToList()
            : new List<AudioStreamInfo> { new(info.AudioStreamIndex, 1, "audio", 0, null, null) };

        var selection = allTracks;
        if (int.TryParse(args2.Get("track"), out var trackNo))
        {
            if (trackNo < 1 || trackNo > allTracks.Count)
            {
                Console.Error.WriteLine($"--track {trackNo} 超出范围（共 {allTracks.Count} 条音轨）");
                return 2;
            }

            selection = new List<AudioStreamInfo> { allTracks[trackNo - 1] };
        }

        var outPrefix = args2.Get("out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!,
            Path.GetFileNameWithoutExtension(input));

        var modelPath = args2.Get("model") ?? ModelCatalog.FindModel();
        if (modelPath is null)
        {
            Console.Error.WriteLine("找不到模型文件，请用 --model 指定，或把 ggml-*.bin 放进 models/");
            return 3;
        }

        var maxChars = int.TryParse(args2.Get("maxchars"), out var mc) ? mc : 18;        var formats = (args2.Get("format") ?? "srt,ass,vtt").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var style = CueStyle.Default;

        string Suffix(AudioStreamInfo t) => allTracks.Count > 1 ? $"-t{t.Ordinal}" : string.Empty;

        var firstCues = new List<SubtitleCue>();

        for (var i = 0; i < selection.Count; i++)
        {
            var track = selection[i];
            Console.WriteLine($"\n=== {track.Number}（流 #{track.Index}）===");
            var tempWav = TempWorkspace.CreateFilePath(input, ".wav");
            try
            {
                Console.WriteLine("[1/4] 抽取音轨 ...");
                await AudioExtractor.ExtractWavAsync(input, tempWav, info.DurationSeconds,
                    new Progress<double>(p => Console.Write($"\r      {p * 100:0}%")),
                    cancellationToken: default, audioStreamIndex: track.Index);
                Console.WriteLine();

                Console.WriteLine($"[2/4] 识别中（{Path.GetFileName(modelPath)}）...");
                AsrResult result;
                using (var provider = new WhisperLocalProvider(modelPath))
                {
                    result = await provider.TranscribeAsync(tempWav, new AsrOptions(args2.Get("lang")),
                        new Progress<AsrProgress>(p =>
                        {
                            if (p.Fraction < 1)
                                Console.Write($"\r      {p.Fraction * 100:0}%");
                        }));
                }

                Console.WriteLine($"\n      语言={result.Language}  片段={result.Segments.Count}");

                var cleaned = HallucinationFilter.Apply(result.Segments);
                if (cleaned.Count != result.Segments.Count)
                    Console.WriteLine($"      幻觉过滤：丢弃 {result.Segments.Count - cleaned.Count} 段");
                var punctuated = PunctuationRestorer.Restore(cleaned);
                var cues = CueSegmenter.Segment(punctuated, maxChars);
                Console.WriteLine($"[3/4] 断句完成：{cues.Count} 条");
                foreach (var cue in cues.Take(10))
                    Console.WriteLine($"      {cue.Start:0.00} - {cue.End:0.00}  {cue.Text}");
                if (cues.Count > 10)
                    Console.WriteLine($"      ... 共 {cues.Count} 条");

                if (cues.Count == 0)
                {
                    Console.WriteLine("[4/4] 这条轨没有可用字幕（多是静音或纯环境音），跳过写文件");
                    continue;
                }

                Console.WriteLine("[4/4] 写入字幕 ...");
                foreach (var f in formats)
                {
                    var ext = f.Trim().ToLowerInvariant();
                    var path = outPrefix + Suffix(track) + "." + ext;
                    var content = ext switch
                    {
                        "ass" or "ssa" => SubtitleWriters.WriteAss(cues, style),
                        "vtt" => SubtitleWriters.WriteVtt(cues),
                        _ => SubtitleWriters.WriteSrt(cues)
                    };
                    SubtitleWriters.Save(content, path);
                    Console.WriteLine($"      -> {path}");
                }

                if (i == 0)
                    firstCues = cues;
            }
            finally
            {
                TempWorkspace.TryDelete(tempWav);
            }
        }

        var overlay = args2.Get("overlay");
        if (!string.IsNullOrWhiteSpace(overlay))
        {
            var format = overlay.ToLowerInvariant() switch
            {
                "mov" => OverlayFormat.MovProRes4444,
                "mp4" => OverlayFormat.Mp4H264,
                _ => OverlayFormat.WebmVp9Alpha
            };
            var width = int.TryParse(args2.Get("width"), out var w) ? w : 1920;
            var height = int.TryParse(args2.Get("height"), out var h) ? h : 1080;
            var fps = double.TryParse(args2.Get("fps"), out var fp) ? fp : 30;

            var overlayPath = outPrefix + "-overlay" + OverlayVideoExporter.ExtensionOf(format);
            Console.WriteLine($"\n[5/5] 导出透明字幕视频 -> {overlayPath}");
            await OverlayVideoExporter.ExportAsync(
                new OverlayExportRequest(overlayPath, format, width, height, fps, info.DurationSeconds, firstCues, style),
                new Progress<ExportProgress>(p =>
                {
                    if (p.Fraction < 1) Console.Write($"\r      {p.Message}");
                }));
            Console.WriteLine($"\n      ok ({new FileInfo(overlayPath).Length / 1024.0 / 1024.0:0.0} MB)");
        }

        if (args2.Has("waveform"))
        {
            foreach (var track in selection)
            {
                var wave = await WaveformBuilder.BuildAsync(input, info.DurationSeconds,
                    cancellationToken: default, audioStreamIndex: track.Index);
                Console.WriteLine($"[waveform] {track.Number} peaks={wave.Min.Length} ({wave.PeaksPerSecond}/s)");
            }
        }

        Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.0}s");
        return 0;

    }

    /// <summary>
    /// 只跑识别，把结果写成 JSON。GUI 用子进程调这个模式，
    /// 好处是「停止」直接 Kill 进程，不必等当前音轨跑完。
    /// </summary>
    private static async Task<int> RunAsrJsonAsync(Args args, string jsonPath)
    {
        var wav = args.Get("input");
        if (string.IsNullOrWhiteSpace(wav) || !File.Exists(wav))
        {
            Console.Error.WriteLine("缺少 --input（wav 文件）");
            return 2;
        }

        if (!string.Equals(Path.GetExtension(wav), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("「--asr-json」要求 --input 是已经抽好的 wav（这是 App 内部用的通道）；"
                                    + "要识别视频请直接用 --input 视频 --out 前缀。");
            return 2;
        }

        var modelPath = args.Get("model") ?? ModelCatalog.FindModel();
        if (modelPath is null || !File.Exists(modelPath))
        {
            Console.Error.WriteLine("找不到模型文件");
            return 3;
        }

        var result = default(AsrResult);
        using (var provider = new WhisperLocalProvider(modelPath))
        {
            result = await provider.TranscribeAsync(wav, new AsrOptions(args.Get("lang")),
                new Progress<AsrProgress>(p =>
                {
                    if (p.Fraction < 1)
                    {
                        Console.Out.WriteLine($"PROGRESS {p.Fraction:0.000}");
                        Console.Out.Flush();
                    }
                }));
        }

        var segments = result.Segments.Select(s => new AsrJsonSegment(s.Text, s.Start, s.End)).ToList();
        var payload = new AsrJsonPayload(result.Language, segments);
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(jsonPath,
                System.Text.Json.JsonSerializer.Serialize(payload, AsrJsonContext.Default.AsrJsonPayload));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"写识别结果失败：{jsonPath}（{ex.Message}）");
            return 3;
        }

        Console.Out.WriteLine($"DONE {segments.Count}");
        return 0;
    }
}

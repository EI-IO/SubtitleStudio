using System.Diagnostics;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace SubtitleStudio.Core.Asr;

/// <summary>基于 whisper.cpp（Whisper.net）的本地识别。</summary>
public sealed class WhisperLocalProvider : IAsrProvider, IDisposable
{
    private readonly string _modelPath;
    private readonly int _threads;
    private WhisperFactory? _factory;

    public WhisperLocalProvider(string modelPath, int? threads = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("找不到 Whisper 模型文件", modelPath);

        _modelPath = modelPath;
        _threads = threads ?? Math.Clamp(Environment.ProcessorCount / 2, 2, 12);
    }

    public string Name => $"本地 Whisper ({ModelName})";

    private string ModelName => Path.GetFileNameWithoutExtension(_modelPath).Replace("ggml-", string.Empty);

    public async Task<AsrResult> TranscribeAsync(
        string wavPath,
        AsrOptions options,
        IProgress<AsrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _factory ??= WhisperFactory.FromPath(_modelPath);

        var builder = _factory.CreateBuilder()
            .WithThreads(_threads)
            .WithSegmentEventHandler(segment => { });

        if (!string.IsNullOrWhiteSpace(options.Language) &&
            !string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguage(options.Language);
        }

        if (!string.IsNullOrWhiteSpace(options.InitialPrompt))
            builder = builder.WithPrompt(options.InitialPrompt);

        using var processor = builder.Build();

        var segments = new List<AsrSegment>();
        var language = options.Language ?? "auto";
        var sw = Stopwatch.StartNew();

        await using var stream = File.OpenRead(wavPath);

        await foreach (var result in processor.ProcessAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = (result.Text ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                language = string.IsNullOrEmpty(result.Language) ? language : result.Language;
                segments.Add(new AsrSegment(
                    text,
                    result.Start.TotalSeconds,
                    result.End.TotalSeconds));
            }

            if (stream.Length > 0)
            {
                var fraction = Math.Clamp(stream.Position / (double)stream.Length, 0, 1);
                progress?.Report(new AsrProgress(fraction, $"识别中… {fraction * 100:0}%"));
            }
        }

        sw.Stop();
        progress?.Report(new AsrProgress(1, $"识别完成，用时 {sw.Elapsed.TotalSeconds:0.0}s"));
        return new AsrResult(language, segments);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}

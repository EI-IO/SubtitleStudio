namespace SubtitleStudio.Core.Asr;

/// <summary>带时间戳的识别单元（词/字）。</summary>
public sealed record AsrWord(string Text, double Start, double End, float Probability = 0f);

/// <summary>识别出的一段话。</summary>
public sealed record AsrSegment(string Text, double Start, double End, IReadOnlyList<AsrWord>? Words = null);

/// <summary>一次完整识别的结果。</summary>
public sealed record AsrResult(string Language, IReadOnlyList<AsrSegment> Segments);

public sealed record AsrOptions(
    string? Language = null,
    string? InitialPrompt = null,
    int BeamSize = 5,
    bool TranslateToEnglish = false);

public sealed record AsrProgress(double Fraction, string Message);

/// <summary>语音识别提供方（本地模型 / 云端 API 共用）。</summary>
public interface IAsrProvider
{
    string Name { get; }

    /// <summary>输入 16kHz / 单声道 / 16bit PCM 的 wav 文件。</summary>
    Task<AsrResult> TranscribeAsync(
        string wavPath,
        AsrOptions options,
        IProgress<AsrProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

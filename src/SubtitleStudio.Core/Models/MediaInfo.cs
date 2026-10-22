namespace SubtitleStudio.Core.Models;

/// <summary>媒体文件的基础信息。</summary>
public sealed record MediaInfo(
    string Path,
    double DurationSeconds,
    int Width,
    int Height,
    double Fps,
    bool HasAudio,
    int AudioStreamIndex)
{
    /// <summary>所有音轨，按文件里的顺序排列（<see cref="AudioStreamInfo.Ordinal"/> 从 1 开始）。</summary>
    public IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; } = Array.Empty<AudioStreamInfo>();

    public bool HasVideo => Width > 0 && Height > 0;

    public string DisplayName => System.IO.Path.GetFileName(Path);

    public string Describe()
    {
        var size = HasVideo ? $"{Width}x{Height}" : "纯音频";
        return $"{DisplayName} · {size} · {DurationSeconds:0.0}s";
    }
}

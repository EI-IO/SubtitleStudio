using System.Runtime.Serialization;

namespace SubtitleStudio.Core.Models;

/// <summary>一条字幕（时间单位为秒）。</summary>
public sealed class SubtitleCue
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = string.Empty;

    [IgnoreDataMember]
    public double Duration => Math.Max(0d, End - Start);

    public SubtitleCue Clone() => new() { Start = Start, End = End, Text = Text };

    public override string ToString() => $"{Start:F2}-{End:F2} {Text}";
}

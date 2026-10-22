using System.Collections.Generic;

namespace SubtitleStudio.Cli;

/// <summary>识别结果里的一段话（跨进程传递用）。</summary>
internal sealed record AsrJsonSegment(string Text, double Start, double End);

/// <summary>识别结果（跨进程传递用）。</summary>
internal sealed record AsrJsonPayload(string Language, List<AsrJsonSegment> Segments);

[System.Text.Json.Serialization.JsonSerializable(typeof(AsrJsonPayload))]
internal sealed partial class AsrJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

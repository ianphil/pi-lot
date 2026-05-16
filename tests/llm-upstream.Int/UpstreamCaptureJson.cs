using System.Text.Json;

namespace LlmUpstream.Int;

internal static class UpstreamCaptureJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(UpstreamCaptureDocument document) =>
        JsonSerializer.Serialize(document, Options) + Environment.NewLine;
}

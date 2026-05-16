using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace LlmUpstream.Int;

internal sealed class UpstreamCaptureDocument
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("base_url")]
    public required string BaseUrl { get; init; }

    [JsonPropertyName("request")]
    public required UpstreamRequestCapture Request { get; init; }

    [JsonPropertyName("response")]
    public required UpstreamResponseCapture Response { get; init; }
}

internal sealed class UpstreamRequestCapture
{
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("headers")]
    public required SortedDictionary<string, string[]> Headers { get; init; }

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Body { get; init; }
}

internal sealed class UpstreamResponseCapture
{
    [JsonPropertyName("status_code")]
    public required int StatusCode { get; init; }

    [JsonPropertyName("reason_phrase")]
    public string? ReasonPhrase { get; init; }

    [JsonPropertyName("headers")]
    public required SortedDictionary<string, string[]> Headers { get; init; }

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Body { get; init; }

    [JsonPropertyName("sse_events")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UpstreamSseEventCapture>? SseEvents { get; init; }
}

internal sealed class UpstreamSseEventCapture
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("data")]
    public required JsonNode[] Data { get; init; }

    [JsonPropertyName("raw")]
    public required string Raw { get; init; }
}

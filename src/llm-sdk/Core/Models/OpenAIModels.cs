using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

// ── OpenAI-compatible model list response ────────────────────────────────────

public sealed class OpenAIModelListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "list";

    [JsonPropertyName("data")]
    public required ModelInfo[] Data { get; init; }
}

// ── Error response ───────────────────────────────────────────────────────────

public sealed class OpenAIErrorResponse
{
    [JsonPropertyName("error")]
    public required OpenAIError Error { get; init; }
}

public sealed class OpenAIError
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

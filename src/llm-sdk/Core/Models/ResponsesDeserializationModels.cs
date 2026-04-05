using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

// ── OpenAI Responses API types (for /responses-only models) ──────────────────

public sealed class ResponsesApiResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("output")]
    public ResponseOutput[]? Output { get; init; }

    [JsonPropertyName("usage")]
    public ResponsesUsageInfo? Usage { get; init; }

    [JsonPropertyName("incomplete_details")]
    public ResponseIncompleteDetails? IncompleteDetails { get; init; }
}

public sealed class ResponseOutput
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public ResponseContent[]? Content { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

public sealed class ResponseContent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("annotations")]
    public object[]? Annotations { get; init; }
}

public sealed class ResponsesUsageInfo
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

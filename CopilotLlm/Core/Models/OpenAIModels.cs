using System.Text.Json.Serialization;

namespace CopilotLlm.Core.Models;

// ── OpenAI-compatible model list response ────────────────────────────────────

public sealed class OpenAIModelListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "list";

    [JsonPropertyName("data")]
    public required OpenAIModelInfo[] Data { get; init; }
}

public sealed class OpenAIModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "model";

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[]? SupportedEndpoints { get; init; }

    [JsonPropertyName("proxy_supported_endpoints")]
    public string[]? ProxySupportedEndpoints { get; init; }
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

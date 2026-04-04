using System.Text.Json.Serialization;

namespace CopilotLlm.Core.Models;

// ── Copilot API types ────────────────────────────────────────────────────────

public sealed class CopilotModelsResponse
{
    [JsonPropertyName("data")]
    public CopilotModelInfo[]? Data { get; init; }
}

public sealed class CopilotModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("object")]
    public string? Object { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[]? SupportedEndpoints { get; init; }

    [JsonPropertyName("capabilities")]
    public CopilotModelCapabilities? Capabilities { get; init; }
}

public sealed class CopilotModelCapabilities
{
    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("limits")]
    public CopilotModelLimits? Limits { get; init; }
}

public sealed class CopilotModelLimits
{
    [JsonPropertyName("max_context_window_tokens")]
    public int? MaxContextWindowTokens { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("max_prompt_tokens")]
    public int? MaxPromptTokens { get; init; }
}

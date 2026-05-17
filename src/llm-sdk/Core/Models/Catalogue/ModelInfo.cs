using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

public sealed record class ModelInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "model";

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("vendor")]
    public string? Vendor { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("preview")]
    public bool? Preview { get; init; }

    [JsonPropertyName("model_picker_category")]
    public string? ModelPickerCategory { get; init; }

    [JsonPropertyName("model_picker_enabled")]
    public bool? ModelPickerEnabled { get; init; }

    [JsonPropertyName("policy")]
    public ModelPolicy? Policy { get; init; }

    [JsonPropertyName("supported_endpoints")]
    public string[] SupportedEndpoints { get; init; } = [];

    [JsonPropertyName("proxy_supported_endpoints")]
    public string[] ProxySupportedEndpoints { get; init; } = [];

    [JsonPropertyName("capabilities")]
    public ModelCapabilities? Capabilities { get; init; }

    [JsonPropertyName("token_limits")]
    public ModelTokenLimits? TokenLimits { get; init; }

    [JsonPropertyName("pricing")]
    public ModelPricing? Pricing { get; init; }

    [JsonIgnore]
    public int? ContextWindow => TokenLimits?.MaxContextWindowTokens ?? Capabilities?.Limits?.MaxContextWindowTokens;

    [JsonIgnore]
    public int? MaxOutputTokens => TokenLimits?.MaxOutputTokens ?? Capabilities?.Limits?.MaxOutputTokens;

    [JsonIgnore]
    public bool SupportsVision => Capabilities?.Supports?.Vision ?? false;

    [JsonIgnore]
    public bool SupportsReasoning =>
        Capabilities?.Supports?.AdaptiveThinking == true ||
        Capabilities?.Supports?.ReasoningEffort is { Length: > 0 };

    [JsonIgnore]
    public IReadOnlyList<ThinkingLevel> SupportedThinkingLevels =>
        Capabilities?.Supports?.ReasoningEffort?
            .Select(static level => Enum.TryParse<ThinkingLevel>(level, ignoreCase: true, out var parsed) ? parsed : (ThinkingLevel?)null)
            .Where(static level => level.HasValue)
            .Select(static level => level!.Value)
            .ToArray() ?? [];

    [JsonIgnore]
    public bool SupportsResponses => SupportedEndpoints.Contains("/responses", StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool SupportsChatCompletions => SupportedEndpoints.Contains("/chat/completions", StringComparer.OrdinalIgnoreCase);

    public static ModelInfo Unknown(string id) => new()
    {
        Id = id,
        Name = id,
        DisplayName = id,
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<ThinkingLevel>))]
public enum ThinkingLevel
{
    Minimal,
    Low,
    Medium,
    High,
    XHigh,
}

public record ModelPricing
{
    [JsonPropertyName("input_per_million_tokens")]
    public decimal InputPerMillionTokens { get; init; }

    [JsonPropertyName("output_per_million_tokens")]
    public decimal OutputPerMillionTokens { get; init; }

    [JsonPropertyName("cache_read_per_million_tokens")]
    public decimal CacheReadPerMillionTokens { get; init; }

    [JsonPropertyName("cache_write_per_million_tokens")]
    public decimal CacheWritePerMillionTokens { get; init; }
}

public sealed record UsagePricing : ModelPricing;

using System.Text.Json.Serialization;

namespace LlmSdk.Core.Models;

public sealed record ModelInfo(
    string Id,
    string DisplayName,
    int? ContextWindow,
    int? MaxOutputTokens,
    bool SupportsVision,
    bool SupportsReasoning,
    IReadOnlyList<ThinkingLevel> SupportedThinkingLevels,
    ModelPricing? Pricing);

[JsonConverter(typeof(JsonStringEnumConverter<ThinkingLevel>))]
public enum ThinkingLevel
{
    Low,
    Medium,
    High,
}

public record ModelPricing
{
    public decimal InputPerMillionTokens { get; init; }
    public decimal OutputPerMillionTokens { get; init; }
    public decimal CacheReadPerMillionTokens { get; init; }
    public decimal CacheWritePerMillionTokens { get; init; }
}

public sealed record UsagePricing : ModelPricing;

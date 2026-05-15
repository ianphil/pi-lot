namespace LlmSdk.Core.Models;

public static class UsageMath
{
    public static Usage Add(Usage a, Usage b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return new Usage(
            a.InputTokens + b.InputTokens,
            a.OutputTokens + b.OutputTokens,
            a.CacheReadTokens + b.CacheReadTokens,
            a.CacheWriteTokens + b.CacheWriteTokens,
            a.Cost is not null && b.Cost is not null ? a.Cost + b.Cost : null);
    }

    public static decimal? CalculateCost(Usage usage, OpenAIModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Pricing is null)
        {
            return null;
        }

        return CalculatePerMillionTokenCost(usage.InputTokens, model.Pricing.InputPerMillionTokens) +
               CalculatePerMillionTokenCost(usage.OutputTokens, model.Pricing.OutputPerMillionTokens) +
               CalculatePerMillionTokenCost(usage.CacheReadTokens, model.Pricing.CacheReadPerMillionTokens) +
               CalculatePerMillionTokenCost(usage.CacheWriteTokens, model.Pricing.CacheWritePerMillionTokens);
    }

    public static Usage? FromResponseUsage(ResponseUsage? usage) =>
        usage is null
            ? null
            : new Usage(
                usage.InputTokens,
                usage.OutputTokens,
                usage.InputTokensDetails.CachedTokens);

    public static Usage? FromUsageInfo(UsageInfo? usage) =>
        usage is null
            ? null
            : new Usage(usage.PromptTokens, usage.CompletionTokens);

    private static decimal CalculatePerMillionTokenCost(long tokens, decimal pricePerMillionTokens) =>
        tokens / 1_000_000m * pricePerMillionTokens;
}

public sealed class UsagePricing
{
    public decimal InputPerMillionTokens { get; init; }
    public decimal OutputPerMillionTokens { get; init; }
    public decimal CacheReadPerMillionTokens { get; init; }
    public decimal CacheWritePerMillionTokens { get; init; }
}

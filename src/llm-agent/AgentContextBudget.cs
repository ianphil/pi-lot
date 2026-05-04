using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using Microsoft.ML.Tokenizers;

namespace LlmAgent;

public static class AgentContextBudget
{
    private static readonly Lazy<Tokenizer> Tokenizer = new(static () => TiktokenTokenizer.CreateForModel("gpt-4o"));

    public static async Task<AgentContextBudgetResult?> EvaluateAsync(
        ILlmSdkClient client,
        CreateResponseRequest request,
        AgentContextBudgetOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return null;
        }

        if (options.WarningThresholdRatio <= 0 || options.WarningThresholdRatio >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Warning threshold must be greater than 0 and less than 1.");
        }

        if (options.ErrorThresholdRatio <= options.WarningThresholdRatio || options.ErrorThresholdRatio > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Error threshold must be greater than warning threshold and less than or equal to 1.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model)
            ? null
            : request.Model;
        if (model is null)
        {
            return null;
        }

        var models = await client.ListModelsAsync(cancellationToken);
        var modelInfo = models.FirstOrDefault(candidate => string.Equals(candidate.Id, model, StringComparison.Ordinal));
        var limits = modelInfo?.TokenLimits;
        if (limits is null)
        {
            return null;
        }

        var budgetTokens = GetPromptBudgetTokens(limits, request, options);
        if (budgetTokens is null or <= 0)
        {
            return null;
        }

        var estimatedTokens = EstimateTokens(request);
        var usageRatio = estimatedTokens / (double)budgetTokens.Value;
        var level = usageRatio >= options.ErrorThresholdRatio
            ? AgentContextBudgetLevel.Error
            : usageRatio >= options.WarningThresholdRatio
                ? AgentContextBudgetLevel.Warning
                : AgentContextBudgetLevel.None;

        return new AgentContextBudgetResult(
            model,
            estimatedTokens,
            budgetTokens.Value,
            usageRatio,
            level,
            limits);
    }

    public static int EstimateTokens(CreateResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, JsonDefaults.Web);
        return Tokenizer.Value.CountTokens(json);
    }

    public static void ThrowIfExceeded(AgentContextBudgetResult? result)
    {
        if (result?.Level is AgentContextBudgetLevel.Error)
        {
            throw new AgentContextBudgetExceededException(result);
        }
    }

    private static int? GetPromptBudgetTokens(
        ModelTokenLimits limits,
        CreateResponseRequest request,
        AgentContextBudgetOptions options)
    {
        if (limits.MaxPromptTokens.HasValue)
        {
            return limits.MaxPromptTokens.Value;
        }

        if (!limits.MaxContextWindowTokens.HasValue)
        {
            return null;
        }

        var reservedOutputTokens = request.MaxOutputTokens ?? options.ReservedOutputTokens;
        return limits.MaxContextWindowTokens.Value - reservedOutputTokens;
    }
}

public sealed record AgentContextBudgetOptions
{
    public static AgentContextBudgetOptions Default { get; } = new();

    public bool Enabled { get; init; } = true;
    public double WarningThresholdRatio { get; init; } = 0.60;
    public double ErrorThresholdRatio { get; init; } = 0.90;
    public int ReservedOutputTokens { get; init; } = 4096;
}

public enum AgentContextBudgetLevel
{
    None,
    Warning,
    Error,
}

public sealed record AgentContextBudgetResult(
    string Model,
    int EstimatedTokens,
    int BudgetTokens,
    double UsageRatio,
    AgentContextBudgetLevel Level,
    ModelTokenLimits TokenLimits)
{
    public string Message
        => Level switch
        {
            AgentContextBudgetLevel.Warning => $"Context estimate is {EstimatedTokens:N0} tokens, which is {UsageRatio:P0} of the {Model} prompt budget ({BudgetTokens:N0} tokens).",
            AgentContextBudgetLevel.Error => $"Context estimate is {EstimatedTokens:N0} tokens, exceeding the conservative {UsageRatio:P0} usage limit for the {Model} prompt budget ({BudgetTokens:N0} tokens). Reduce the conversation before sending.",
            _ => $"Context estimate is {EstimatedTokens:N0} tokens, which is {UsageRatio:P0} of the {Model} prompt budget ({BudgetTokens:N0} tokens).",
        };
}

public sealed class AgentContextBudgetExceededException : InvalidOperationException
{
    public AgentContextBudgetExceededException(AgentContextBudgetResult result)
        : base(result.Message)
    {
        Result = result;
    }

    public AgentContextBudgetResult Result { get; }
}

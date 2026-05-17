using LlmSdk.Core.Models;

namespace LlmSdk.Core.Services;

public static class ThinkingLevelClamp
{
    private static readonly ThinkingLevel[] DescendingLevels =
    [
        ThinkingLevel.XHigh,
        ThinkingLevel.High,
        ThinkingLevel.Medium,
        ThinkingLevel.Low,
        ThinkingLevel.Minimal,
    ];

    public static ThinkingLevel? Clamp(ThinkingLevel requested, ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var supported = SupportedLevels(model);
        if (supported.Count == 0)
        {
            return null;
        }

        var requestedIndex = Array.IndexOf(DescendingLevels, requested);
        if (requestedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), requested, null);
        }

        for (var i = requestedIndex; i < DescendingLevels.Length; i++)
        {
            if (supported.Contains(DescendingLevels[i]))
            {
                return DescendingLevels[i];
            }
        }

        return null;
    }

    public static IReadOnlyList<ThinkingLevel> SupportedLevels(ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return DescendingLevels
            .Reverse()
            .Where(model.SupportedThinkingLevels.Contains)
            .ToArray();
    }
}

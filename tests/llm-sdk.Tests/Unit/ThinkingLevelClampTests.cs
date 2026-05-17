using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ThinkingLevelClampTests
{
    private static readonly ThinkingLevel[] OrderedLevels =
    [
        ThinkingLevel.Minimal,
        ThinkingLevel.Low,
        ThinkingLevel.Medium,
        ThinkingLevel.High,
        ThinkingLevel.XHigh,
    ];

    public static IEnumerable<object?[]> ClampCases()
    {
        for (var mask = 0; mask < 1 << OrderedLevels.Length; mask++)
        {
            var supported = OrderedLevels
                .Where((_, index) => (mask & (1 << index)) != 0)
                .ToArray();

            foreach (var requested in OrderedLevels)
            {
                yield return [requested, supported, ExpectedClamp(requested, supported)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ClampCases))]
    public void Clamp_ReturnsNearestLowerSupportedLevelForEverySupportedLevelCombination(
        ThinkingLevel requested,
        ThinkingLevel[] supported,
        ThinkingLevel? expected)
    {
        Assert.Equal(expected, ThinkingLevelClamp.Clamp(requested, CreateModel(supported)));
    }

    [Theory]
    [InlineData(ThinkingLevel.Minimal, ThinkingLevel.Minimal)]
    [InlineData(ThinkingLevel.Low, ThinkingLevel.Low)]
    [InlineData(ThinkingLevel.Medium, ThinkingLevel.Medium)]
    [InlineData(ThinkingLevel.High, ThinkingLevel.High)]
    [InlineData(ThinkingLevel.XHigh, ThinkingLevel.XHigh)]
    public void Clamp_WhenExactLevelIsSupported_ReturnsRequestedLevel(ThinkingLevel requested, ThinkingLevel expected)
    {
        var model = CreateModel(ThinkingLevel.Minimal, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High, ThinkingLevel.XHigh);

        Assert.Equal(expected, ThinkingLevelClamp.Clamp(requested, model));
    }

    [Theory]
    [InlineData(ThinkingLevel.XHigh, ThinkingLevel.Medium)]
    [InlineData(ThinkingLevel.High, ThinkingLevel.Medium)]
    [InlineData(ThinkingLevel.Medium, ThinkingLevel.Medium)]
    [InlineData(ThinkingLevel.Low, ThinkingLevel.Low)]
    public void Clamp_WhenRequestedLevelIsUnsupported_ReturnsNearestLowerSupportedLevel(
        ThinkingLevel requested,
        ThinkingLevel expected)
    {
        var model = CreateModel(ThinkingLevel.Low, ThinkingLevel.Medium);

        Assert.Equal(expected, ThinkingLevelClamp.Clamp(requested, model));
    }

    [Fact]
    public void Clamp_WhenNoLowerLevelIsSupported_ReturnsNull()
    {
        var model = CreateModel(ThinkingLevel.Medium, ThinkingLevel.High);

        Assert.Null(ThinkingLevelClamp.Clamp(ThinkingLevel.Low, model));
    }

    [Fact]
    public void Clamp_WhenModelHasNoSupportedThinkingLevels_ReturnsNull()
    {
        Assert.Null(ThinkingLevelClamp.Clamp(ThinkingLevel.High, ModelInfo.Unknown("unknown")));
    }

    private static ModelInfo CreateModel(params ThinkingLevel[] levels) => new()
    {
        Id = "reasoning-model",
        Capabilities = new ModelCapabilities
        {
            Supports = new ModelSupports
            {
                ReasoningEffort = levels.Select(static level => level.ToString()).ToArray(),
            },
        },
    };

    private static ThinkingLevel? ExpectedClamp(ThinkingLevel requested, ThinkingLevel[] supported)
    {
        for (var i = Array.IndexOf(OrderedLevels, requested); i >= 0; i--)
        {
            if (supported.Contains(OrderedLevels[i]))
            {
                return OrderedLevels[i];
            }
        }

        return null;
    }
}

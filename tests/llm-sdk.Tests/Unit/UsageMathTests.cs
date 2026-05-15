using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class UsageMathTests
{
    [Fact]
    public void Add_CombinesTokenCountsAndPreservesNullCostUntilBothCostsExist()
    {
        var usage = UsageMath.Add(
            new Usage(10, 5, CacheReadTokens: 2, Cost: 0.10m),
            new Usage(4, 6, CacheWriteTokens: 3));

        Assert.Equal(new Usage(14, 11, CacheReadTokens: 2, CacheWriteTokens: 3), usage);
    }

    [Fact]
    public void Add_WithBothCosts_AddsCosts()
    {
        var usage = UsageMath.Add(new Usage(1, 2, Cost: 0.03m), new Usage(3, 4, Cost: 0.07m));

        Assert.Equal(0.10m, usage.Cost);
    }

    [Fact]
    public void CalculateCost_WithPricing_ComputesPerMillionTokenCost()
    {
        var model = new OpenAIModelInfo
        {
            Id = "sample",
            Pricing = new UsagePricing
            {
                InputPerMillionTokens = 2m,
                OutputPerMillionTokens = 10m,
                CacheReadPerMillionTokens = 0.5m,
                CacheWritePerMillionTokens = 4m,
            },
        };

        var cost = UsageMath.CalculateCost(new Usage(1_000_000, 500_000, 100_000, 25_000), model);

        Assert.Equal(7.15m, cost);
    }

    [Fact]
    public void CalculateCost_WithoutPricing_ReturnsNull()
    {
        var cost = UsageMath.CalculateCost(new Usage(1, 2), new OpenAIModelInfo { Id = "sample" });

        Assert.Null(cost);
    }
}

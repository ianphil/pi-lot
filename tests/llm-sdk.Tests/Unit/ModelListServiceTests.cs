using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Infrastructure;
using LlmSdk.Tests.Fakes;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ModelListServiceTests
{
    [Fact]
    public async Task GetModelsAsync_WithKnownModel_AddsCataloguePricing()
    {
        var service = CreateService(new ModelDescriptor
        {
            Id = "gpt-4o",
            Name = "GPT-4o from upstream",
            SupportedEndpoints = ["/responses"],
        });

        var response = await service.GetModelsAsync();

        var model = Assert.Single(response.Data);
        Assert.Equal("gpt-4o", model.Id);
        Assert.NotNull(model.Pricing);
        Assert.Equal(2.50m, model.Pricing.InputPerMillionTokens);
        Assert.Equal(10m, model.Pricing.OutputPerMillionTokens);
    }

    [Fact]
    public async Task GetModelInfoAsync_WithKnownModel_PrefersUpstreamTokenLimits()
    {
        var service = CreateService(new ModelDescriptor
        {
            Id = "gpt-4o",
            Name = "GPT-4o from upstream",
            SupportedEndpoints = ["/responses"],
            TokenLimits = new ModelTokenLimits
            {
                MaxContextWindowTokens = 64000,
                MaxOutputTokens = 8000,
            },
        });

        var models = await service.GetModelInfoAsync();

        var model = Assert.Single(models);
        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal("GPT-4o from upstream", model.DisplayName);
        Assert.Equal(64000, model.ContextWindow);
        Assert.Equal(8000, model.MaxOutputTokens);
        Assert.True(model.SupportsVision);
        Assert.NotNull(model.Pricing);
    }

    [Fact]
    public async Task GetModelInfoAsync_WithUnknownModel_UsesConservativeDefaults()
    {
        var service = CreateService(new ModelDescriptor
        {
            Id = "unknown-model",
            SupportedEndpoints = ["/responses"],
        });

        var models = await service.GetModelInfoAsync();

        var model = Assert.Single(models);
        Assert.Equal("unknown-model", model.Id);
        Assert.Equal("unknown-model", model.DisplayName);
        Assert.Null(model.ContextWindow);
        Assert.Null(model.MaxOutputTokens);
        Assert.False(model.SupportsVision);
        Assert.False(model.SupportsReasoning);
        Assert.Empty(model.SupportedThinkingLevels);
        Assert.Null(model.Pricing);
    }

    private static ModelListService CreateService(params ModelDescriptor[] models)
    {
        return new ModelListService(
            new FakeModelProvider { Models = models },
            new EmbeddedModelCatalogue());
    }
}

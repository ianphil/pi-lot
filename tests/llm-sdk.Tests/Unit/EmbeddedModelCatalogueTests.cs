using LlmSdk.Core.Models;
using LlmSdk.Infrastructure;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class EmbeddedModelCatalogueTests
{
    [Fact]
    public void ResourceStream_Exists()
    {
        using var stream = EmbeddedModelCatalogue.OpenResourceStream();

        Assert.NotNull(stream);
    }

    [Fact]
    public void Get_WithKnownModel_ReturnsCapabilitiesAndPricing()
    {
        var catalogue = new EmbeddedModelCatalogue();

        var model = catalogue.Get("gpt-4o");

        Assert.NotNull(model);
        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal("GPT-4o", model.DisplayName);
        Assert.Equal(128000, model.ContextWindow);
        Assert.Equal(16384, model.MaxOutputTokens);
        Assert.True(model.SupportsVision);
        Assert.False(model.SupportsReasoning);
        Assert.Empty(model.SupportedThinkingLevels);
        Assert.NotNull(model.Pricing);
        Assert.Equal(2.50m, model.Pricing.InputPerMillionTokens);
        Assert.Equal(10m, model.Pricing.OutputPerMillionTokens);
    }

    [Fact]
    public void Get_WithUnknownModel_ReturnsConservativeUnknownModel()
    {
        var catalogue = new EmbeddedModelCatalogue();

        var model = catalogue.Get("future-model");

        Assert.NotNull(model);
        Assert.Equal("future-model", model.Id);
        Assert.Equal("future-model", model.DisplayName);
        Assert.Null(model.ContextWindow);
        Assert.Null(model.MaxOutputTokens);
        Assert.False(model.SupportsVision);
        Assert.False(model.SupportsReasoning);
        Assert.Empty(model.SupportedThinkingLevels);
        Assert.Null(model.Pricing);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var catalogue = new EmbeddedModelCatalogue();

        var model = catalogue.Get("GPT-4O");

        Assert.NotNull(model);
        Assert.Equal("gpt-4o", model.Id);
    }

    [Fact]
    public void All_ReturnsEmbeddedModels()
    {
        var catalogue = new EmbeddedModelCatalogue();

        var models = catalogue.All();

        Assert.True(models.Count >= 8);
        Assert.Contains(models, static model => model.Id == "gpt-4o");
    }
}

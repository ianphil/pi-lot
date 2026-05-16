using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSdk.Int;

public sealed class ModelInfoSdkTests
{
    private readonly ITestOutputHelper _output;

    public ModelInfoSdkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task GetModelAsync_WithLiveApi_ReturnsModelInfo()
    {
        await using var provider = SdkIntTestHost.CreateAuthenticatedProvider();
        var client = provider.GetRequiredService<ILlmSdkClient>();

        var models = await client.ListModelsAsync();
        Assert.NotEmpty(models);

        var modelId = models[0].Id;
        var model = await client.GetModelAsync(modelId);

        _output.WriteLine(JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        Assert.Equal(modelId, model.Id);
        Assert.NotEmpty(model.SupportedEndpoints);
        Assert.NotEmpty(model.ProxySupportedEndpoints);
    }

    [Fact]
    public async Task GetModelAsync_WithFakeApi_ReturnsModelInfo()
    {
        await using var provider = SdkIntTestHost.CreateFakeApiProvider(new ModelInfo
        {
            Id = "fake-gpt-5.5",
            Object = "model",
            OwnedBy = "fake-llm",
            Name = "Fake GPT 5.5",
            Vendor = "Fake LLM",
            Version = "fake-gpt-5.5",
            Preview = false,
            SupportedEndpoints = ["/responses", "/chat/completions"],
            Capabilities = new ModelCapabilities
            {
                Object = "model_capabilities",
                Family = "fake-gpt",
                Type = "chat",
                Tokenizer = "o200k_base",
                Supports = new ModelSupports
                {
                    Streaming = true,
                    StructuredOutputs = true,
                    ToolCalls = true,
                    Vision = true,
                },
                Limits = new ModelLimits
                {
                    MaxContextWindowTokens = 128000,
                    MaxOutputTokens = 16000,
                    MaxPromptTokens = 112000,
                },
            },
            TokenLimits = new ModelTokenLimits
            {
                MaxContextWindowTokens = 128000,
                MaxOutputTokens = 16000,
                MaxPromptTokens = 112000,
            },
        });
        var client = provider.GetRequiredService<ILlmSdkClient>();

        var models = await client.ListModelsAsync();
        Assert.NotEmpty(models);

        var modelId = models[0].Id;
        var model = await client.GetModelAsync(modelId);

        _output.WriteLine(JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonDefaults.Web)
        {
            WriteIndented = true,
        }));

        Assert.Equal(modelId, model.Id);
        Assert.NotEmpty(model.SupportedEndpoints);
        Assert.NotEmpty(model.ProxySupportedEndpoints);
    }

}

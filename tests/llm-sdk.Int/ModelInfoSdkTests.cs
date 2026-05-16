using System.Text.Json;
using LlmSdk;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        await using var provider = CreateAuthenticatedProvider();
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
        await using var provider = CreateFakeApiProvider();
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

    private static ServiceProvider CreateAuthenticatedProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");
        return provider;
    }

    private static ServiceProvider CreateFakeApiProvider()
    {
        var fakeApi = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
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
                },
            ],
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmSdk();
        services.RemoveAll<IModelProvider>();
        services.AddSingleton<IModelProvider>(fakeApi);
        return services.BuildServiceProvider();
    }

    private sealed class FakeModelProvider : IModelProvider
    {
        public ModelInfo[] Models { get; init; } = [];

        public Task<ModelInfo[]> FetchModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Models);

        public Task<ProxyHttpResult> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProxyHttpResult> SendChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProxyHttpResult> SendResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProxyStreamResult> StreamChatCompletionsAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProxyStreamResult> StreamResponsesAsync(CreateResponseRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

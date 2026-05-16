using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace llm_ui.Tests;

public sealed class UiEndpointTests
{
    [Fact]
    public async Task Models_ReturnsDefaultModelAndAvailableModels()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            Models =
            [
                new ModelInfo { Id = "gpt-5.4", Name = "GPT 5.4" },
                new ModelInfo { Id = "claude-haiku-4.5", Name = "Claude Haiku 4.5" },
            ],
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<UiModelsResponse>("/api/models");

        Assert.NotNull(response);
        Assert.Equal("gpt-5.4", response.DefaultModel);
        Assert.Contains(response.Models, model => model.Id == "gpt-5.4");
        Assert.Contains(response.Models, model => model.Id == "claude-haiku-4.5");
    }

    [Fact]
    public async Task Models_IncludesTokenLimitsFromSdkModels()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4",
                    Name = "GPT 5.4",
                    TokenLimits = new ModelTokenLimits
                    {
                        MaxContextWindowTokens = 128000,
                        MaxPromptTokens = 120000,
                        MaxOutputTokens = 16000,
                    },
                },
            ],
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<UiModelsResponse>("/api/models");

        var model = Assert.Single(response?.Models ?? []);
        Assert.NotNull(model.TokenLimits);
        Assert.Equal(128000, model.TokenLimits.MaxContextWindowTokens);
        Assert.Equal(120000, model.TokenLimits.MaxPromptTokens);
        Assert.Equal(16000, model.TokenLimits.MaxOutputTokens);
    }

    [Fact]
    public async Task Chat_WithEditedAssistantContext_StreamsDeltaAndSendsAssistantContext()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            StreamEvents =
            [
                new OutputTextDeltaEvent("response.output_text.delta", 1, "pong", 0, 0, "msg_1"),
            ],
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new UiChatRequest(
            "gpt-5.4",
            """
            ## System

            Be terse.

            ## User

            Ping?

            ## Assistant

            Previous answer.
            """,
            null));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"type\":\"delta\"", body, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"pong\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
        Assert.NotNull(fakeClient.LastCreateResponseStreamRequest);
        Assert.Equal("gpt-5.4", fakeClient.LastCreateResponseStreamRequest.Model);
        Assert.Equal("Be terse.", fakeClient.LastCreateResponseStreamRequest.Instructions);

        var input = fakeClient.LastCreateResponseStreamRequest.Input;
        Assert.Equal("user", input[0].GetProperty("role").GetString());
        Assert.Equal("assistant", input[1].GetProperty("role").GetString());
        Assert.Equal("output_text", input[1].GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Chat_WhenContextBudgetExceedsWarningThreshold_StreamsWarning()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4",
                    TokenLimits = new ModelTokenLimits { MaxPromptTokens = 1000 },
                },
            ],
            StreamEvents =
            [
                new OutputTextDeltaEvent("response.output_text.delta", 1, "pong", 0, 0, "msg_1"),
            ],
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();
        var largePrompt = string.Concat(Enumerable.Repeat("word ", 700));

        var response = await client.PostAsJsonAsync("/api/chat", new UiChatRequest(
            "gpt-5.4",
            $"## User\n\n{largePrompt}",
            null));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"type\":\"warning\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"delta\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_WhenContextBudgetExceedsErrorThreshold_StreamsErrorBeforeUpstreamRequest()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4",
                    TokenLimits = new ModelTokenLimits { MaxPromptTokens = 1 },
                },
            ],
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new UiChatRequest(
            "gpt-5.4",
            """
            ## User

            Ping?
            """,
            null));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"type\":\"error\"", body, StringComparison.Ordinal);
        Assert.Contains("Reduce the conversation before sending.", body, StringComparison.Ordinal);
        Assert.Null(fakeClient.LastCreateResponseStreamRequest);
    }

    [Fact]
    public async Task Chat_WhenSdkStreamThrows_StreamsErrorEvent()
    {
        var fakeClient = new FakeLlmSdkClient
        {
            StreamException = new InvalidOperationException("Upstream stream failed."),
        };
        using var factory = CreateFactory(fakeClient);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new UiChatRequest(
            "gpt-5.4",
            """
            ## User

            Ping?
            """,
            null));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"error\"", body, StringComparison.Ordinal);
        Assert.Contains("Upstream stream failed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type\":\"done\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_WithUnsupportedMarkdownSection_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeLlmSdkClient());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new UiChatRequest(
            null,
            """
            ## Tool

            tool output
            """,
            null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeLlmSdkClient fakeClient)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILlmSdkClient>(fakeClient);
            }));

    private sealed class FakeLlmSdkClient : ILlmSdkClient
    {
        public IReadOnlyList<ModelInfo> Models { get; init; } = [];
        public IReadOnlyList<ResponseStreamEvent> StreamEvents { get; init; } = [];
        public Exception? StreamException { get; init; }
        public CreateResponseRequest? LastCreateResponseStreamRequest { get; private set; }

        public Task<Response> CreateResponseAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Response> CreateResponseAsync(string? model, string input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
            CreateResponseRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastCreateResponseStreamRequest = request;
            if (StreamException is not null)
            {
                throw StreamException;
            }

            foreach (var streamEvent in StreamEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }
        }

        public IAsyncEnumerable<ResponseStreamEvent> CreateResponseStreamAsync(
            string? model,
            string input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChatCompletionResponse> CreateChatCompletionAsync(string? model, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
            string? model,
            string message,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Models);

        public Task<ModelInfo> GetModelAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) ?? ModelInfo.Unknown(id));
    }
}

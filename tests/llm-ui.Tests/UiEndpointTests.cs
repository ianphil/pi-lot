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
                new OpenAIModelInfo { Id = "gpt-5.4", Name = "GPT 5.4" },
                new OpenAIModelInfo { Id = "claude-haiku-4.5", Name = "Claude Haiku 4.5" },
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
        public IReadOnlyList<OpenAIModelInfo> Models { get; init; } = [];
        public IReadOnlyList<ResponseStreamEvent> StreamEvents { get; init; } = [];
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

        public Task<IReadOnlyList<OpenAIModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Models);
    }
}

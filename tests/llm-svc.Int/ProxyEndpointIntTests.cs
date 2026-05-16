using System.Net.Http.Json;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Proxy;
using LlmSvc.Int.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LlmSvc.Int;

public sealed class ProxyEndpointIntTests
{
    private readonly ITestOutputHelper _output;

    public ProxyEndpointIntTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task PostResponses_WithFakeApi_ReturnsResponseAndForwardsProxyOptions()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "fake-gpt-5.5",
                    SupportedEndpoints = ["/responses"],
                },
            ],
            ResponsesResult = new(JsonSerializer.Serialize(new Response
            {
                Id = "resp_fake",
                Model = "fake-gpt-5.5",
                Output =
                [
                    new ResponseMessageItem
                    {
                        Id = "msg_fake",
                        Content = [new ResponseOutputTextPart { Text = "hello from fake service" }],
                    },
                ],
            }, JsonDefaults.Web), 200),
        };
        await using var factory = SvcIntWebApplicationFactory.CreateFake(provider);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "fake-gpt-5.5",
                input = "Reply with exactly: hello",
            }),
        };
        request.Headers.Add("X-LLM-Request-Id", "svc-int-request");
        request.Headers.Add("X-LLM-Correlation-Id", "svc-int-correlation");
        request.Headers.Add("X-LLM-Metadata-test", "svc-int");
        request.Headers.Add("X-LLM-Timeout-Ms", "60000");

        var httpResponse = await client.SendAsync(request);
        var body = await httpResponse.Content.ReadAsStringAsync();
        _output.WriteLine(body);

        httpResponse.EnsureSuccessStatusCode();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.Equal("resp_fake", response?.Id);
        Assert.Equal("svc-int-request", provider.LastResponsesRequest?.RequestId);
        Assert.Equal("svc-int-correlation", provider.LastResponsesRequest?.CorrelationId);
        var metadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(provider.LastResponsesRequest?.Metadata);
        Assert.Equal("svc-int", metadata["test"]);
        Assert.Equal(60000, provider.LastResponsesRequest?.TimeoutMs);
    }

    [Fact]
    public async Task PostChatCompletions_WithFakeApi_ReturnsChatCompletion()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "fake-chat-5.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chatcmpl_fake",
                Model = "fake-chat-5.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "hello from fake chat",
                        },
                        FinishReason = "stop",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };
        await using var factory = SvcIntWebApplicationFactory.CreateFake(provider);
        using var client = factory.CreateClient();

        var httpResponse = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "fake-chat-5.5",
            messages = new[] { new { role = "user", content = "Reply with exactly: hello" } },
        });
        var body = await httpResponse.Content.ReadAsStringAsync();
        _output.WriteLine(body);

        httpResponse.EnsureSuccessStatusCode();
        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonDefaults.Web);
        Assert.Equal("chatcmpl_fake", response?.Id);
        Assert.Equal("fake-chat-5.5", provider.LastChatRequest?.Model);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task PostResponses_WithLiveApi_ReturnsResponse()
    {
        await using var factory = SvcIntWebApplicationFactory.CreateLive();
        using var client = factory.CreateClient();
        var auth = factory.Services.GetRequiredService<IAuthProvider>();
        Assert.True(auth.TryLoadCredential(), "Could not load Copilot credentials from COPILOT_TOKEN or the local credential store.");

        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "gpt-5.4-mini",
            input = "Reply with exactly: hello",
            stream = false,
        });
        var body = await httpResponse.Content.ReadAsStringAsync();
        _output.WriteLine(body);

        httpResponse.EnsureSuccessStatusCode();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.Equal("response", response?.Object);
        Assert.StartsWith("gpt-5.4-mini", response?.Model, StringComparison.Ordinal);
        Assert.NotEmpty(response?.Output ?? []);
    }
}

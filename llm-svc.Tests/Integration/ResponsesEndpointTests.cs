using System.Net.Http.Json;
using System.Text.Json;
using CopilotLlm.Core.Models;

namespace llm_svc.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ResponsesEndpointTests : IClassFixture<ResponsesWebApplicationFactory>
{
    private readonly ResponsesWebApplicationFactory _factory;

    public ResponsesEndpointTests(ResponsesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostResponses_ChatOnlyModel_TranslatesPlainTextResponse()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat_456",
            Model = "claude-haiku-4.5",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Hello from endpoint test",
                    },
                    FinishReason = "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = 5,
                CompletionTokens = 4,
                TotalTokens = 9,
            },
        }, JsonDefaults.Web), 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Hi there",
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("application/json", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);

        Assert.NotNull(response);
        Assert.Equal("response", response!.Object);
        Assert.Equal("claude-haiku-4.5", response.Model);
        var message = Assert.IsType<ResponseMessageItem>(response.Output[0]);
        var text = Assert.IsType<ResponseOutputTextPart>(message.Content[0]);
        Assert.Equal("Hello from endpoint test", text.Text);
    }

    [Fact]
    public async Task PostResponses_ChatOnlyModel_WithStreaming_TranslatesPlainTextStream()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" there\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_stream\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Hi there",
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: response.created", body);
        Assert.Contains("event: response.output_text.delta", body);
        Assert.Contains("\"text\":\"Hello there\"", body);
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task GetModels_ReportsUpstreamAndProxyEndpoints()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                Name = "Claude Haiku 4.5",
                OwnedBy = "github-copilot",
                SupportedEndpoints = ["/chat/completions", "/v1/messages"],
            },
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                Name = "GPT-5.4",
                OwnedBy = "github-copilot",
                SupportedEndpoints = ["/responses"],
            },
        ];

        using var client = _factory.CreateClient();
        var httpResponse = await client.GetAsync("/v1/models");

        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<OpenAIModelListResponse>(body, JsonDefaults.Web);

        Assert.NotNull(response);

        var claude = Assert.Single(response!.Data, model => model.Id == "claude-haiku-4.5");
        Assert.NotNull(claude.SupportedEndpoints);
        Assert.NotNull(claude.ProxySupportedEndpoints);
        Assert.Equal(["/chat/completions", "/v1/messages"], claude.SupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], claude.ProxySupportedEndpoints);

        var gpt = Assert.Single(response.Data, model => model.Id == "gpt-5.4");
        Assert.NotNull(gpt.SupportedEndpoints);
        Assert.NotNull(gpt.ProxySupportedEndpoints);
        Assert.Equal(["/responses"], gpt.SupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], gpt.ProxySupportedEndpoints);
    }

    [Fact]
    public async Task PostResponses_NativeResponsesModel_ReturnsCanonicalResponseBody()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                SupportedEndpoints = ["/responses"],
            },
        ];
        _factory.Provider.ResponsesResult = new(
            """
            {
              "id": "resp_native_123",
              "object": "response",
              "status": "completed",
              "model": "gpt-5.4",
              "output": [
                {
                  "id": "msg_native_123",
                  "type": "message",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Hello from native responses",
                      "annotations": []
                    }
                  ]
                }
              ],
              "tools": [],
              "tool_choice": "auto",
              "truncation": "disabled",
              "parallel_tool_calls": true,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "temperature": 1.0,
              "top_p": 1.0,
              "presence_penalty": 0.0,
              "frequency_penalty": 0.0,
              "top_logprobs": 0,
              "store": false,
              "background": false,
              "service_tier": "default"
            }
            """,
            200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "gpt-5.4",
            input = "Hi there",
        });

        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);

        Assert.NotNull(response);
        Assert.Equal("resp_native_123", response!.Id);
        Assert.Equal("gpt-5.4", response.Model);
        var message = Assert.IsType<ResponseMessageItem>(response.Output[0]);
        var text = Assert.IsType<ResponseOutputTextPart>(message.Content[0]);
        Assert.Equal("Hello from native responses", text.Text);
        Assert.NotNull(_factory.Provider.LastResponsesRequest);
        Assert.Equal("gpt-5.4", _factory.Provider.LastResponsesRequest!.Model);
        Assert.False(_factory.Provider.LastResponsesRequest.Stream);
    }

    [Fact]
    public async Task PostResponses_NativeResponsesModel_WithStreaming_ReturnsEventStreamBody()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                SupportedEndpoints = ["/responses"],
            },
        ];
        _factory.Provider.ResponsesStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "event: response.created\ndata: {\"type\":\"response.created\"}\n\n",
                "event: response.completed\ndata: {\"type\":\"response.completed\"}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "gpt-5.4",
            input = "Hi there",
            stream = true,
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: response.created", body);
        Assert.Contains("event: response.completed", body);
        Assert.Contains("data: [DONE]", body);
        Assert.True(_factory.Provider.LastResponsesRequest?.Stream);
    }

    [Fact]
    public async Task PostResponses_DualEndpointModel_PrefersNativeResponsesRoute()
    {
        _factory.Provider.ResetCapturedRequests();
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "gpt-5.4",
                SupportedEndpoints = ["/responses", "/chat/completions"],
            },
        ];
        _factory.Provider.ResponsesResult = new(
            """
            {
              "id": "resp_dual_123",
              "object": "response",
              "status": "completed",
              "model": "gpt-5.4",
              "output": [
                {
                  "id": "msg_dual_123",
                  "type": "message",
                  "status": "completed",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Native responses route preferred.",
                      "annotations": []
                    }
                  ]
                }
              ],
              "tools": [],
              "tool_choice": "auto",
              "truncation": "disabled",
              "parallel_tool_calls": true,
              "text": {
                "format": {
                  "type": "text"
                }
              },
              "temperature": 1.0,
              "top_p": 1.0,
              "presence_penalty": 0.0,
              "frequency_penalty": 0.0,
              "top_logprobs": 0,
              "store": false,
              "background": false,
              "service_tier": "default"
            }
            """,
            200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "gpt-5.4",
            input = "Hi there",
        });

        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);

        Assert.NotNull(response);
        Assert.Equal("resp_dual_123", response!.Id);
        Assert.NotNull(_factory.Provider.LastResponsesRequest);
        Assert.Null(_factory.Provider.LastChatRequest);
    }

    [Fact]
    public async Task PostResponses_ChatOnlyModel_ForwardsToolDefinitions()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat_tool_forward",
            Model = "claude-haiku-4.5",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "I can use tools.",
                    },
                    FinishReason = "stop",
                },
            ],
        }, JsonDefaults.Web), 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = "Hi there",
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "web_fetch",
                    description = "Fetch a web page",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            url = new
                            {
                                type = "string",
                            },
                        },
                        required = new[] { "url" },
                    },
                },
            },
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.NotNull(_factory.Provider.LastChatRequest?.Tools);
        Assert.Single(_factory.Provider.LastChatRequest!.Tools!);
        Assert.Equal("web_fetch", _factory.Provider.LastChatRequest.Tools![0].Function?.Name);
        Assert.Equal("Fetch a web page", _factory.Provider.LastChatRequest.Tools[0].Function?.Description);
    }

    [Fact]
    public async Task PostResponses_ChatOnlyModel_WithToolRoundTrip_TranslatesFunctionCallConversation()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsResult = new(JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat_final",
            Model = "claude-haiku-4.5",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "That page is about example domains.",
                    },
                    FinishReason = "stop",
                },
            ],
        }, JsonDefaults.Web), 200);

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = "fetch https://example.com and summarize it",
                        },
                    },
                },
                new
                {
                    type = "function_call",
                    call_id = "call_abc123",
                    name = "web_fetch",
                    arguments = "{\"url\":\"https://example.com\"}",
                },
                new
                {
                    type = "function_call_output",
                    call_id = "call_abc123",
                    output = "{\"title\":\"Example Domain\",\"content\":\"This domain is for illustrative examples.\"}",
                },
            },
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "web_fetch",
                    description = "Fetch a web page",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            url = new
                            {
                                type = "string",
                            },
                        },
                        required = new[] { "url" },
                    },
                },
            },
        });

        httpResponse.EnsureSuccessStatusCode();

        var messages = _factory.Provider.LastChatRequest!.Messages!;
        Assert.Equal(3, messages.Length);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        var toolCalls = Assert.IsAssignableFrom<ChatToolCall[]>(messages[1].ToolCalls);
        Assert.Single(toolCalls);
        Assert.Equal("call_abc123", toolCalls[0].Id);
        Assert.Equal("web_fetch", toolCalls[0].Function?.Name);
        Assert.Equal("tool", messages[2].Role);
        Assert.Equal("call_abc123", messages[2].ToolCallId);

        var body = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        Assert.Contains("example domains", response!.Output[0] is ResponseMessageItem msg && msg.Content[0] is ResponseOutputTextPart tp ? tp.Text : string.Empty);
    }

    [Fact]
    public async Task PostResponses_ChatOnlyModel_WithToolRoundTripStreaming_TranslatesFunctionCallConversation()
    {
        _factory.Provider.Models =
        [
            new ModelDescriptor
            {
                Id = "claude-haiku-4.5",
                SupportedEndpoints = ["/chat/completions"],
            },
        ];
        _factory.Provider.ChatCompletionsStreamResult = new(
            null,
            200,
            "text/event-stream",
            AsAsyncChunks(
                "data: {\"id\":\"chat_final\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"That page \"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_final\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"is about examples.\"},\"finish_reason\":null}]}\n\n",
                "data: {\"id\":\"chat_final\",\"model\":\"claude-haiku-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                "data: [DONE]\n\n"));

        using var client = _factory.CreateClient();
        var httpResponse = await client.PostAsJsonAsync("/v1/responses", new
        {
            model = "claude-haiku-4.5",
            stream = true,
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = "fetch https://example.com and summarize it",
                        },
                    },
                },
                new
                {
                    type = "function_call",
                    call_id = "call_abc123",
                    name = "web_fetch",
                    arguments = "{\"url\":\"https://example.com\"}",
                },
                new
                {
                    type = "function_call_output",
                    call_id = "call_abc123",
                    output = "{\"title\":\"Example Domain\",\"content\":\"This domain is for illustrative examples.\"}",
                },
            },
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "web_fetch",
                    description = "Fetch a web page",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            url = new
                            {
                                type = "string",
                            },
                        },
                        required = new[] { "url" },
                    },
                },
            },
        });

        httpResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", httpResponse.Content.Headers.ContentType?.ToString());

        var messages = _factory.Provider.LastChatRequest!.Messages!;
        Assert.Equal(3, messages.Length);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("call_abc123", messages[1].ToolCalls![0].Id);
        Assert.Equal("tool", messages[2].Role);

        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: response.output_text.delta", body);
        Assert.Contains("That page ", body);
        Assert.Contains("is about examples.", body);
        Assert.Contains("event: response.completed", body);
    }

    private static async IAsyncEnumerable<string> AsAsyncChunks(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }
}

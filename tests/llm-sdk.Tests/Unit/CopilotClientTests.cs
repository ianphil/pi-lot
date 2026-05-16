using System.Net;
using System.Text;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Infrastructure;
using LlmSdk.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
[Collection("EnvironmentTests")]
public sealed class CopilotClientTests
{
    [Fact]
    public void TryLoadCredential_WhenEnvVarIsSet_DoesNotConsultCredentialStore()
    {
        var store = new StubCredentialStore(["store-token"]);
        var client = CreateClient(_ => throw new InvalidOperationException("HTTP should not be called"), envToken: "env-token", credentialStore: store);

        Assert.True(client.IsAuthenticated);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenUpstreamReturnsUnauthorized_ReloadsCredentialFromStore()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["expired-token", "fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/models")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(token switch
            {
                "expired-token" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "fresh-token" => JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "id": "gpt-5.4-mini",
                          "name": "GPT-5.4 Mini",
                          "supported_endpoints": ["/responses"]
                        }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected bearer token '{token}'"),
            });
        }, envToken: null, credentialStore: store);

        Assert.True(await client.ValidateTokenAsync());

        Assert.Equal(2, store.CallCount);
        Assert.Equal(["expired-token", "fresh-token"], bearerTokens);
    }

    [Fact]
    public async Task FetchModelsAsync_WhenUpstreamReturnsUnauthorized_RetriesWithFreshCredential()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["expired-token", "fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/models")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(token switch
            {
                "expired-token" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "fresh-token" => JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "id": "gpt-5.4-mini",
                          "name": "GPT-5.4 Mini",
                          "supported_endpoints": ["/responses"]
                        }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected bearer token '{token}'"),
            });
        }, envToken: null, credentialStore: store);

        var models = await client.FetchModelsAsync(forceRefresh: true);

        Assert.Equal(2, store.CallCount);
        Assert.Equal(["expired-token", "fresh-token"], bearerTokens);
        Assert.Single(models);
    }

    [Fact]
    public async Task FetchModelsAsync_WithFullModelPayload_CapturesApiDetails()
    {
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/models")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            return Task.FromResult(JsonResponse(FullModelListJson));
        });

        var models = await client.FetchModelsAsync(forceRefresh: true);
        var descriptors = await ((IModelProvider)client).FetchModelsAsync(forceRefresh: true);

        var model = Assert.Single(models);
        Assert.Equal("claude-opus-4.6", model.Id);
        Assert.Equal("Anthropic", model.Vendor);
        Assert.Equal("claude-opus-4.6", model.Version);
        Assert.False(model.Preview);
        Assert.Equal("powerful", model.ModelPickerCategory);
        Assert.True(model.ModelPickerEnabled);
        Assert.Equal("enabled", model.Policy?.State);
        Assert.Equal("model_capabilities", model.Capabilities?.Object);
        Assert.Equal("claude-opus-4.6", model.Capabilities?.Family);
        Assert.Equal("chat", model.Capabilities?.Type);
        Assert.Equal("o200k_base", model.Capabilities?.Tokenizer);
        Assert.True(model.Capabilities?.Supports?.AdaptiveThinking);
        Assert.True(model.Capabilities?.Supports?.ParallelToolCalls);
        Assert.Equal(["low", "medium", "high"], model.Capabilities?.Supports?.ReasoningEffort ?? []);
        Assert.Equal(16000, model.Capabilities?.Limits?.MaxNonStreamingOutputTokens);
        Assert.Equal(3145728, model.Capabilities?.Limits?.Vision?.MaxPromptImageSize);
        Assert.Equal(["image/jpeg", "image/png", "image/webp"], model.Capabilities?.Limits?.Vision?.SupportedMediaTypes ?? []);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("Anthropic", descriptor.Vendor);
        Assert.Equal("claude-opus-4.6", descriptor.Version);
        Assert.Equal("powerful", descriptor.ModelPickerCategory);
        Assert.True(descriptor.Capabilities?.Supports?.StructuredOutputs);
        Assert.Equal(200000, descriptor.TokenLimits?.MaxContextWindowTokens);
    }

    [Fact]
    public async Task SendResponsesAsync_WhenTokenIsMissing_LoadsCredentialOnDemand()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(JsonResponse(CompletedResponseJson));
        }, envToken: null, credentialStore: store, loadCredential: false);

        var result = await client.SendResponsesAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Input = JsonDocument.Parse("""[{"type":"message","role":"user","content":[{"type":"input_text","text":"Hi"}]}]""").RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.True(client.IsAuthenticated);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(["fresh-token"], bearerTokens);
    }

    [Fact]
    public async Task SendResponsesAsync_WhenTokenIsFresh_DoesNotReloadCredential()
    {
        var bearerTokens = new List<string>();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 04, 04, 21, 00, 00, TimeSpan.Zero));
        var store = new StubCredentialStore(["loaded-token", "unexpected-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(JsonResponse(CompletedResponseJson));
        }, envToken: null, credentialStore: store, timeProvider: timeProvider);

        var result = await client.SendResponsesAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Input = JsonDocument.Parse("""[{"type":"message","role":"user","content":[{"type":"input_text","text":"Hi"}]}]""").RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(["loaded-token"], bearerTokens);
    }

    [Fact]
    public async Task SendResponsesAsync_WhenTokenIsStale_ReloadsCredential()
    {
        var bearerTokens = new List<string>();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 04, 04, 21, 00, 00, TimeSpan.Zero));
        var store = new StubCredentialStore(["stale-token", "fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(JsonResponse(CompletedResponseJson));
        }, envToken: null, credentialStore: store, timeProvider: timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(31));

        var result = await client.SendResponsesAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Input = JsonDocument.Parse("""[{"type":"message","role":"user","content":[{"type":"input_text","text":"Hi"}]}]""").RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, store.CallCount);
        Assert.Equal(["fresh-token"], bearerTokens);
    }

    [Fact]
    public async Task SendResponsesAsync_WhenUpstreamReturnsUnauthorized_RetriesWithFreshCredential()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["expired-token", "fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(token switch
            {
                "expired-token" => UnauthorizedResponse(),
                "fresh-token" => JsonResponse(CompletedResponseJson),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected bearer token '{token}'"),
            });
        }, envToken: null, credentialStore: store);

        var result = await client.SendResponsesAsync(CreateMinimalResponseRequest());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, store.CallCount);
        Assert.Equal(["expired-token", "fresh-token"], bearerTokens);
    }

    [Fact]
    public async Task SendResponsesAsync_WhenRetryAlsoReturnsUnauthorized_ReturnsSingleUnauthorizedResult()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["expired-token", "still-expired-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(UnauthorizedResponse());
        }, envToken: null, credentialStore: store);

        var result = await client.SendResponsesAsync(CreateMinimalResponseRequest());

        Assert.Equal(401, result.StatusCode);
        Assert.Equal(2, store.CallCount);
        Assert.Equal(["expired-token", "still-expired-token"], bearerTokens);
        Assert.Equal("""{"error":"unauthorized"}""", result.Body);
    }

    [Fact]
    public async Task StreamResponsesAsync_WhenUpstreamReturnsUnauthorized_RetriesWithFreshCredential()
    {
        var bearerTokens = new List<string>();
        var store = new StubCredentialStore(["expired-token", "fresh-token"]);
        var client = CreateClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/responses")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
            }

            var token = request.Headers.Authorization?.Parameter
                ?? throw new Xunit.Sdk.XunitException("Missing bearer token");
            bearerTokens.Add(token);

            return Task.FromResult(token switch
            {
                "expired-token" => UnauthorizedResponse(),
                "fresh-token" => EventStreamResponse("data: hello\n\n"),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected bearer token '{token}'"),
            });
        }, envToken: null, credentialStore: store);

        var result = await client.StreamResponsesAsync(CreateMinimalResponseRequest());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, store.CallCount);
        Assert.Equal(["expired-token", "fresh-token"], bearerTokens);
        Assert.NotNull(result.Chunks);
        Assert.Equal(["data: hello\n\n"], await ReadChunksAsync(result.Chunks!));
    }

    [Fact]
    public void TryLoadCredential_WhenStoreReturnsNull_ReturnsFalseAndClearsAuthentication()
    {
        var store = new StubCredentialStore([]);
        var client = CreateClient(
            _ => throw new InvalidOperationException("HTTP should not be called"),
            envToken: null,
            credentialStore: store,
            loadCredential: false);

        Assert.False(client.TryLoadCredential());
        Assert.False(client.IsAuthenticated);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task ChatAsync_WhenResponsesModelReceivesToolConversation_PreservesToolCallContext()
    {
        HttpRequestMessage? responsesRequest = null;

        var client = CreateClient(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/models")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": [
                            {
                              "id": "gpt-5.4-mini",
                              "name": "GPT-5.4 Mini",
                              "supported_endpoints": ["/responses"]
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            if (request.RequestUri?.AbsolutePath == "/responses")
            {
                responsesRequest = CloneRequest(request);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": "resp_123",
                          "object": "response",
                          "status": "completed",
                          "model": "gpt-5.4-mini",
                          "output": [
                            {
                              "id": "msg_123",
                              "type": "message",
                              "status": "completed",
                              "role": "assistant",
                              "content": [
                                {
                                  "type": "output_text",
                                  "text": "Done.",
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
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
        });

        var result = await client.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-5.4-mini",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "fetch https://example.com and summarize it",
                },
                new ChatMessage
                {
                    Role = "assistant",
                    ToolCalls =
                    [
                        new ChatToolCall
                        {
                            Id = "call_abc123",
                            Function = new ChatToolCallFunction
                            {
                                Name = "web_fetch",
                                Arguments = "{\"url\":\"https://example.com\"}",
                            },
                        },
                    ],
                },
                new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = "call_abc123",
                    Content = "{\"title\":\"Example Domain\"}",
                },
            ],
            Tools =
            [
                new ChatToolDefinition
                {
                    Function = new ChatToolFunctionDefinition
                    {
                        Name = "web_fetch",
                        Description = "Fetch a web page",
                        Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                    },
                },
            ],
            ToolChoice = new
            {
                type = "function",
                function = new
                {
                    name = "web_fetch",
                },
            },
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(responsesRequest);

        using var requestDocument = JsonDocument.Parse(await responsesRequest!.Content!.ReadAsStringAsync());
        var root = requestDocument.RootElement;

        Assert.Equal("gpt-5.4-mini", root.GetProperty("model").GetString());

        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(3, input.Length);

        Assert.Equal("message", input[0].GetProperty("type").GetString());
        Assert.Equal("user", input[0].GetProperty("role").GetString());
        Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("fetch https://example.com and summarize it", input[0].GetProperty("content")[0].GetProperty("text").GetString());

        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.Equal("call_abc123", input[1].GetProperty("call_id").GetString());
        Assert.Equal("web_fetch", input[1].GetProperty("name").GetString());
        Assert.Equal("{\"url\":\"https://example.com\"}", input[1].GetProperty("arguments").GetString());

        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("call_abc123", input[2].GetProperty("call_id").GetString());
        Assert.Equal("{\"title\":\"Example Domain\"}", input[2].GetProperty("output").GetString());

        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Single(tools);
        Assert.Equal("web_fetch", tools[0].GetProperty("name").GetString());

        Assert.Equal("function", root.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("web_fetch", root.GetProperty("tool_choice").GetProperty("function").GetProperty("name").GetString());

        using var bodyDocument = JsonDocument.Parse(result.Body);
        Assert.Equal("chat.completion", bodyDocument.RootElement.GetProperty("object").GetString());
        Assert.Equal("Done.", bodyDocument.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsync_WhenModelSupportsChatCompletions_PrefersNativeChatRoute()
    {
        HttpRequestMessage? chatRequest = null;

        var client = CreateClient(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/models")
            {
                return JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "id": "gpt-5.4-mini",
                          "name": "GPT-5.4 Mini",
                          "supported_endpoints": ["/chat/completions", "/responses"]
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri?.AbsolutePath == "/chat/completions")
            {
                chatRequest = CloneRequest(request);
                return JsonResponse(
                    """
                    {
                      "id": "chat_123",
                      "object": "chat.completion",
                      "model": "gpt-5.4-mini",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": "Native chat route used."
                          },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 3,
                        "completion_tokens": 4,
                        "total_tokens": 7
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
        });

        var result = await client.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-5.4-mini",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "Hello",
                },
            ],
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(chatRequest);

        using var requestDocument = JsonDocument.Parse(await chatRequest!.Content!.ReadAsStringAsync());
        var root = requestDocument.RootElement;

        Assert.Equal("gpt-5.4-mini", root.GetProperty("model").GetString());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Hello", root.GetProperty("messages")[0].GetProperty("content").GetString());

        using var bodyDocument = JsonDocument.Parse(result.Body);
        Assert.Equal("chat_123", bodyDocument.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ChatAsync_WhenModelSupportsResponsesOnly_MapsToolChoiceAndTools()
    {
        HttpRequestMessage? responsesRequest = null;

        var client = CreateClient(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/models")
            {
                return JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "id": "gpt-5.4-mini",
                          "name": "GPT-5.4 Mini",
                          "supported_endpoints": ["/responses"]
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri?.AbsolutePath == "/responses")
            {
                responsesRequest = CloneRequest(request);
                return JsonResponse(
                    """
                    {
                      "id": "resp_456",
                      "object": "response",
                      "status": "completed",
                      "model": "gpt-5.4-mini",
                      "output": [
                        {
                          "id": "msg_456",
                          "type": "message",
                          "status": "completed",
                          "role": "assistant",
                          "content": [
                            {
                              "type": "output_text",
                              "text": "Tool mapping verified.",
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
                    """);
            }

            throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
        });

        var result = await client.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-5.4-mini",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = JsonDocument.Parse("""[{"type":"input_text","text":"Hi"}]""").RootElement.Clone(),
                },
            ],
            Tools =
            [
                new ChatToolDefinition
                {
                    Function = new ChatToolFunctionDefinition
                    {
                        Name = "web_fetch",
                        Description = "Fetch a web page",
                        Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                    },
                },
                new ChatToolDefinition
                {
                    Function = new ChatToolFunctionDefinition
                    {
                        Name = "lookup_weather",
                        Description = "Look up weather",
                        Parameters = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""").RootElement.Clone(),
                    },
                },
            ],
            ToolChoice = "auto",
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(responsesRequest);

        using var requestDocument = JsonDocument.Parse(await responsesRequest!.Content!.ReadAsStringAsync());
        var root = requestDocument.RootElement;

        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);
        Assert.Equal("web_fetch", tools[0].GetProperty("name").GetString());
        Assert.Equal("Fetch a web page", tools[0].GetProperty("description").GetString());
        Assert.Equal("lookup_weather", tools[1].GetProperty("name").GetString());

        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.Equal("input_text", root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("Hi", root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());

        using var bodyDocument = JsonDocument.Parse(result.Body);
        Assert.Equal("chat.completion", bodyDocument.RootElement.GetProperty("object").GetString());
        Assert.Equal("Tool mapping verified.", bodyDocument.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    private static CopilotClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder,
        string? envToken = "test-token",
        ICopilotCredentialStore? credentialStore = null,
        bool loadCredential = true,
        TimeProvider? timeProvider = null)
    {
        var originalToken = Environment.GetEnvironmentVariable("COPILOT_TOKEN");
        Environment.SetEnvironmentVariable("COPILOT_TOKEN", envToken);

        try
        {
            var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
            {
                BaseAddress = new Uri("https://api.enterprise.githubcopilot.com"),
            };

            var client = new CopilotClient(
                NullLogger<CopilotClient>.Instance,
                new StubHttpClientFactory(httpClient),
                credentialStore ?? new StubCredentialStore([]),
                timeProvider ?? TimeProvider.System);

            if (loadCredential)
            {
                Assert.True(client.TryLoadCredential());
            }

            return client;
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_TOKEN", originalToken);
        }
    }

    private const string CompletedResponseJson =
        """
        {
          "id": "resp_123",
          "object": "response",
          "status": "completed",
          "model": "gpt-5.4-mini",
          "output": [],
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
        """;

    private const string FullModelListJson =
        """
        {
          "data": [
            {
              "capabilities": {
                "family": "claude-opus-4.6",
                "limits": {
                  "max_context_window_tokens": 200000,
                  "max_non_streaming_output_tokens": 16000,
                  "max_output_tokens": 32000,
                  "max_prompt_tokens": 168000,
                  "vision": {
                    "max_prompt_image_size": 3145728,
                    "max_prompt_images": 1,
                    "supported_media_types": [
                      "image/jpeg",
                      "image/png",
                      "image/webp"
                    ]
                  }
                },
                "object": "model_capabilities",
                "supports": {
                  "adaptive_thinking": true,
                  "max_thinking_budget": 32000,
                  "min_thinking_budget": 1024,
                  "parallel_tool_calls": true,
                  "reasoning_effort": [
                    "low",
                    "medium",
                    "high"
                  ],
                  "streaming": true,
                  "structured_outputs": true,
                  "tool_calls": true,
                  "vision": true
                },
                "tokenizer": "o200k_base",
                "type": "chat"
              },
              "id": "claude-opus-4.6",
              "model_picker_category": "powerful",
              "model_picker_enabled": true,
              "name": "Claude Opus 4.6",
              "object": "model",
              "policy": {
                "state": "enabled",
                "terms": "Enable access to Claude Opus 4.6."
              },
              "preview": false,
              "supported_endpoints": [
                "/v1/messages",
                "/chat/completions"
              ],
              "vendor": "Anthropic",
              "version": "claude-opus-4.6"
            }
          ]
        }
        """;

    private static CreateResponseRequest CreateMinimalResponseRequest() => new()
    {
        Model = "gpt-5.4-mini",
        Input = JsonDocument.Parse("""[{"type":"message","role":"user","content":[{"type":"input_text","text":"Hi"}]}]""").RootElement.Clone(),
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(
                body,
                Encoding.UTF8,
                request.Content.Headers.ContentType?.MediaType ?? "application/json");

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage UnauthorizedResponse() => new(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("""{"error":"unauthorized"}""", Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage EventStreamResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private static async Task<string[]> ReadChunksAsync(IAsyncEnumerable<string> chunks)
    {
        var result = new List<string>();
        await foreach (var chunk in chunks)
        {
            result.Add(chunk);
        }

        return result.ToArray();
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubCredentialStore(IEnumerable<string> credentials) : ICopilotCredentialStore
    {
        private readonly Queue<string> _credentials = new(credentials);

        public string DisplayName => "stub";

        public int CallCount { get; private set; }

        public string? GetCredential()
        {
            CallCount++;
            return _credentials.Count > 0 ? _credentials.Dequeue() : null;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan timeSpan) => _utcNow = _utcNow.Add(timeSpan);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using LlmSvc.Core.Models;
using LlmSvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace llm_svc.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CopilotClientTests
{
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

    private static CopilotClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var originalToken = Environment.GetEnvironmentVariable("COPILOT_TOKEN");
        Environment.SetEnvironmentVariable("COPILOT_TOKEN", "test-token");

        try
        {
            var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
            {
                BaseAddress = new Uri("https://api.enterprise.githubcopilot.com"),
            };

            var client = new CopilotClient(NullLogger<CopilotClient>.Instance, new StubHttpClientFactory(httpClient));
            Assert.True(client.TryLoadCredential());
            return client;
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_TOKEN", originalToken);
        }
    }

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

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }
}

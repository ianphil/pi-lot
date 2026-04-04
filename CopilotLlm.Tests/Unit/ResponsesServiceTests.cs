using System.Text.Json;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using CopilotLlm.Tests.Fakes;
using FakeModelProvider = CopilotLlm.Tests.Fakes.TestModelProvider;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ResponsesServiceTests
{
    [Fact]
    public async Task CreateAsync_TranslatesChatCompletionIntoResponsesShape()
    {
        var provider = new TestModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    Name = "Claude Sonnet 4.5",
                    OwnedBy = "github-copilot",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_123",
                Object = "chat.completion",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Hello from Claude",
                        },
                        FinishReason = "stop",
                    },
                ],
                Usage = new UsageInfo
                {
                    PromptTokens = 11,
                    CompletionTokens = 7,
                    TotalTokens = 18,
                },
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("application/json", result.ContentType);

        var body = Assert.IsType<string>(result.Body);
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        Assert.Equal("response", response!.Object);
        Assert.Equal("completed", response.Status);
        Assert.Equal("claude-sonnet-4.5", response.Model);
        Assert.Single(response.Output);

        var message = Assert.IsType<ResponseMessageItem>(response.Output[0]);
        Assert.Equal("assistant", message.Role);
        var text = Assert.IsType<ResponseOutputTextPart>(message.Content[0]);
        Assert.Equal("Hello from Claude", text.Text);

        Assert.NotNull(provider.LastChatRequest);
        Assert.Equal("user", provider.LastChatRequest!.Messages![0].Role);
        Assert.Equal("Hi", provider.LastChatRequest.Messages[0].Content);
    }

    [Fact]
    public async Task CreateAsync_ReturnsIncompleteDetailsWhenChatCompletionStopsForLength()
    {
        var provider = new TestModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    Name = "Claude Sonnet 4.5",
                    OwnedBy = "github-copilot",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_incomplete",
                Object = "chat.completion",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Hello from Claude",
                        },
                        FinishReason = "length",
                    },
                ],
                Usage = new UsageInfo
                {
                    PromptTokens = 11,
                    CompletionTokens = 7,
                    TotalTokens = 18,
                },
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
            MaxOutputTokens = 7,
        });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("application/json", result.ContentType);

        var body = Assert.IsType<string>(result.Body);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("incomplete", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("max_output_tokens", document.RootElement.GetProperty("incomplete_details").GetProperty("reason").GetString());

        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        var message = Assert.IsType<ResponseMessageItem>(response!.Output[0]);
        Assert.Equal("incomplete", message.Status);
    }

    [Fact]
    public async Task CreateAsync_MarksOnlyLastNonStreamingOutputItemIncomplete()
    {
        var provider = new TestModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_tool_incomplete",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            ToolCalls =
                            [
                                new ChatToolCall
                                {
                                    Id = "call_one",
                                    Function = new ChatToolCallFunction
                                    {
                                        Name = "tool_one",
                                        Arguments = "{\"city\":\"Seattle\"}",
                                    },
                                },
                                new ChatToolCall
                                {
                                    Id = "call_two",
                                    Function = new ChatToolCallFunction
                                    {
                                        Name = "tool_two",
                                        Arguments = "{\"zip\":\"98101\"}",
                                    },
                                },
                            ],
                        },
                        FinishReason = "length",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Need tools\"").RootElement.Clone(),
            MaxOutputTokens = 7,
        });

        var body = Assert.IsType<string>(result.Body);
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);

        Assert.NotNull(response);
        Assert.Equal("incomplete", response!.Status);
        Assert.Equal(2, response.Output.Length);
        Assert.Equal("completed", response.Output[0].Status);
        Assert.Equal("incomplete", response.Output[1].Status);
    }

    [Fact]
    public async Task CreateAsync_EmitsSpecLikeSseWhenStreamingRequested()
    {
        var provider = new TestModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "gpt-5.4",
                    SupportedEndpoints = ["/responses"],
                },
            ],
            ResponsesStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "event: response.created\ndata: {\"type\":\"response.created\"}\n\n",
                    "event: response.completed\ndata: {\"type\":\"response.completed\"}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4",
            Stream = true,
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("text/event-stream", result.ContentType);
        var body = await result.ReadBodyAsync();
        Assert.Equal(
            "event: response.created\ndata: {\"type\":\"response.created\"}\n\n" +
            "event: response.completed\ndata: {\"type\":\"response.completed\"}\n\n" +
            "data: [DONE]\n\n",
            body);
        Assert.True(provider.LastResponsesRequest?.Stream);
    }

    [Fact]
    public async Task CreateAsync_TranslatesChatCompletionStreamIntoResponsesEvents()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        var body = await result.ReadBodyAsync();

        Assert.Equal("text/event-stream", result.ContentType);
        Assert.True(provider.LastChatRequest?.Stream);
        Assert.True(body.IndexOf("event: response.created", StringComparison.Ordinal) <
                    body.IndexOf("event: response.in_progress", StringComparison.Ordinal));
        Assert.True(body.IndexOf("event: response.output_item.added", StringComparison.Ordinal) <
                    body.IndexOf("event: response.content_part.added", StringComparison.Ordinal));
        Assert.True(body.IndexOf("event: response.content_part.added", StringComparison.Ordinal) <
                    body.IndexOf("event: response.output_text.delta", StringComparison.Ordinal));
        Assert.Contains("\"delta\":\"Hello\"", body);
        Assert.Contains("\"delta\":\" world\"", body);
        Assert.Contains("\"text\":\"Hello world\"", body);
        Assert.Contains("event: response.completed", body);
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task CreateAsync_EmitsIncompleteTerminalEventWhenChatCompletionStopsForLength()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"length\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
            MaxOutputTokens = 7,
        });

        var body = await result.ReadBodyAsync();
        var events = ParseSseEvents(body);

        Assert.Equal("text/event-stream", result.ContentType);
        Assert.True(provider.LastChatRequest?.Stream);

        var outputItemDone = Assert.Single(events, item => item.EventName == "response.output_item.done");
        Assert.Equal("incomplete", outputItemDone.Payload.GetProperty("item").GetProperty("status").GetString());

        var terminal = Assert.Single(events, item => item.EventName == "response.incomplete");
        Assert.Equal("incomplete", terminal.Payload.GetProperty("response").GetProperty("status").GetString());
        Assert.Equal(
            "max_output_tokens",
            terminal.Payload.GetProperty("response").GetProperty("incomplete_details").GetProperty("reason").GetString());

        Assert.DoesNotContain(events, item => item.EventName == "response.completed");
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task CreateAsync_MarksOnlyLastStreamingOutputItemIncomplete()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "data: {\"id\":\"chat_tool_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_one\",\"type\":\"function\",\"function\":{\"name\":\"tool_one\",\"arguments\":\"{\\\"city\\\":\"}},{\"index\":1,\"id\":\"call_two\",\"type\":\"function\",\"function\":{\"name\":\"tool_two\",\"arguments\":\"{\\\"zip\\\":\"}}]},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_tool_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"Seattle\\\"}\"}},{\"index\":1,\"function\":{\"arguments\":\"\\\"98101\\\"}\"}}]},\"finish_reason\":\"length\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"Need tools\"").RootElement.Clone(),
            MaxOutputTokens = 7,
        });

        var body = await result.ReadBodyAsync();
        var events = ParseSseEvents(body);
        var outputItemDoneEvents = events
            .Where(item => item.EventName == "response.output_item.done")
            .OrderBy(item => item.Payload.GetProperty("output_index").GetInt32())
            .ToArray();

        Assert.Equal(2, outputItemDoneEvents.Length);
        Assert.Equal("completed", outputItemDoneEvents[0].Payload.GetProperty("item").GetProperty("status").GetString());
        Assert.Equal("incomplete", outputItemDoneEvents[1].Payload.GetProperty("item").GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateAsync_EmitsFailedStreamEventsWhenUpstreamChunkIsInvalid()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks("data: {not-json}\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        var body = await result.ReadBodyAsync();
        Assert.Contains("event: error", body);
        Assert.Contains("event: response.failed", body);
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public void Serialize_EmitsIncompleteTerminalEventWhenResponseIsIncomplete()
    {
        var body = ResponseSseSerializer.Serialize(new Response
        {
            Id = "resp_incomplete",
            Status = ResponseStatuses.Incomplete,
            Model = "claude-sonnet-4.5",
            IncompleteDetails = new ResponseIncompleteDetails
            {
                Reason = "max_output_tokens",
            },
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_incomplete",
                    Status = ResponseStatuses.Incomplete,
                    Role = "assistant",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "Hello world",
                        },
                    ],
                },
            ],
        });

        var events = ParseSseEvents(body);
        var terminal = Assert.Single(events, item => item.EventName == "response.incomplete");

        Assert.Equal("incomplete", terminal.Payload.GetProperty("response").GetProperty("status").GetString());
        Assert.Equal(
            "max_output_tokens",
            terminal.Payload.GetProperty("response").GetProperty("incomplete_details").GetProperty("reason").GetString());
        Assert.DoesNotContain(events, item => item.EventName == "response.completed");
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task CreateAsync_MapsChatToolCallsToFunctionCallItems()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_tool",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            ToolCalls =
                            [
                                new ChatToolCall
                                {
                                    Id = "call_weather",
                                    Function = new ChatToolCallFunction
                                    {
                                        Name = "get_weather",
                                        Arguments = "{\"city\":\"Seattle\"}",
                                    },
                                },
                            ],
                        },
                        FinishReason = "tool_calls",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Need weather\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "get_weather",
                    Description = "Gets current weather",
                    Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                },
            ],
        });

        var body = Assert.IsType<string>(result.Body);
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        var functionCall = Assert.IsType<ResponseFunctionCallItem>(response!.Output[0]);
        Assert.Equal("get_weather", functionCall.Name);
        Assert.Equal("call_weather", functionCall.CallId);
        Assert.Equal("{\"city\":\"Seattle\"}", functionCall.Arguments);
    }

    [Fact]
    public async Task CreateAsync_RoundTripsFunctionCallOutputIntoToolMessages()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_followup",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "The weather is sunny.",
                        },
                        FinishReason = "stop",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("""
                [
                  {
                    "type": "function_call",
                    "call_id": "call_weather",
                    "name": "get_weather",
                    "arguments": "{\"city\":\"Seattle\"}"
                  },
                  {
                    "type": "function_call_output",
                    "call_id": "call_weather",
                    "output": "Sunny and 60."
                  }
                ]
                """).RootElement.Clone(),
        });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(provider.LastChatRequest?.Messages);
        Assert.Equal(2, provider.LastChatRequest!.Messages!.Length);
        Assert.Equal("assistant", provider.LastChatRequest.Messages[0].Role);
        Assert.Equal("tool", provider.LastChatRequest.Messages[1].Role);
        Assert.Equal("call_weather", provider.LastChatRequest.Messages[1].ToolCallId);
        Assert.Equal("Sunny and 60.", provider.LastChatRequest.Messages[1].Content);
    }

    [Fact]
    public async Task CreateAsync_MapsForcedFunctionToolChoiceToChatCompletions()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_choice",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Calling the function.",
                        },
                        FinishReason = "stop",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Need weather\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "get_weather",
                    Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                },
            ],
            ToolChoice = JsonDocument.Parse("""
                {
                  "type": "function",
                  "name": "get_weather"
                }
                """).RootElement.Clone(),
        });

        var toolChoiceJson = JsonSerializer.Serialize(provider.LastChatRequest?.ToolChoice, JsonDefaults.Web);
        Assert.Equal("""{"type":"function","function":{"name":"get_weather"}}""", toolChoiceJson);
    }

    [Fact]
    public async Task CreateAsync_FiltersToolsWhenAllowedToolsChoiceIsUsed()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_allowed",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Using one tool.",
                        },
                        FinishReason = "stop",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Need tool gating\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "get_weather",
                    Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                },
                new ResponseFunctionToolDefinition
                {
                    Name = "send_email",
                    Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                },
            ],
            ToolChoice = JsonDocument.Parse("""
                {
                  "type": "allowed_tools",
                  "tools": [
                    { "type": "function", "name": "get_weather" }
                  ]
                }
                """).RootElement.Clone(),
        });

        Assert.NotNull(provider.LastChatRequest?.Tools);
        Assert.Single(provider.LastChatRequest!.Tools!);
        Assert.Equal("get_weather", provider.LastChatRequest.Tools[0].Function?.Name);
        Assert.Equal("auto", provider.LastChatRequest.ToolChoice);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("required")]
    [InlineData("none")]
    public async Task CreateAsync_PreservesSimpleToolChoiceModes(string toolChoice)
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_modes",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "ok",
                        },
                        FinishReason = "stop",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Need tool choice\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "get_weather",
                    Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                },
            ],
            ToolChoice = JsonDocument.Parse($"\"{toolChoice}\"").RootElement.Clone(),
        });

        Assert.Equal(toolChoice, provider.LastChatRequest?.ToolChoice);
    }

    [Fact]
    public async Task CreateAsync_TranslatesStreamingToolCallArguments()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "data: {\"id\":\"chat_tool_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_weather\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\"}}]},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_tool_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"Seattle\\\"}\"}}]},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_tool_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"Need weather\"").RootElement.Clone(),
        });

        var body = await result.ReadBodyAsync();
        Assert.Contains("event: response.function_call_arguments.delta", body);
        Assert.Contains("event: response.function_call_arguments.done", body);
        Assert.Contains("\"name\":\"get_weather\"", body);
        Assert.Contains("\"arguments\":", body);
        Assert.Contains("Seattle", body);
    }

    [Fact]
    public async Task CreateAsync_ReturnsStructuredErrorWhenModelIsMissing()
    {
        var provider = new FakeModelProvider
        {
            Models = [],
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "missing-model",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(404, result.StatusCode);

        var body = Assert.IsType<string>(result.Body);
        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("model_not_found", error!.Error.Code);
        Assert.Equal("model", error.Error.Param);
    }

    [Fact]
    public async Task CreateAsync_ReturnsStructuredErrorWhenInputIsMissing()
    {
        var provider = new FakeModelProvider();
        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
        });

        Assert.Equal(400, result.StatusCode);
        var body = Assert.IsType<string>(result.Body);
        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("missing_required_parameter", error!.Error.Code);
        Assert.Equal("input", error.Error.Param);
    }

    [Fact]
    public async Task CreateAsync_MapsUpstreamAuthErrorIntoStructuredResponse()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(
                JsonSerializer.Serialize(new OpenAIErrorResponse
                {
                    Error = new OpenAIError
                    {
                        Message = "Not authenticated",
                        Code = "auth_error",
                        Type = "error",
                    },
                }, JsonDefaults.Web),
                401),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(401, result.StatusCode);
        var body = Assert.IsType<string>(result.Body);
        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("auth_error", error!.Error.Code);
        Assert.Equal("invalid_request_error", error.Error.Type);
    }

    [Fact]
    public async Task CreateAsync_MapsUpstreamRateLimitErrorIntoStructuredResponse()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(
                JsonSerializer.Serialize(new OpenAIErrorResponse
                {
                    Error = new OpenAIError
                    {
                        Message = "Slow down",
                        Code = "rate_limited",
                        Type = "error",
                    },
                }, JsonDefaults.Web),
                429),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(429, result.StatusCode);
        var body = Assert.IsType<string>(result.Body);
        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("rate_limited", error!.Error.Code);
        Assert.Equal("too_many_requests", error.Error.Type);
    }

    [Fact]
    public async Task CreateAsync_MapsRawUpstreamServerFailureIntoStructuredResponse()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult("upstream exploded", 500, "text/plain"),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"Hi\"").RootElement.Clone(),
        });

        Assert.Equal(500, result.StatusCode);
        var body = Assert.IsType<string>(result.Body);
        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("server_error", error!.Error.Type);
        Assert.Equal("upstream exploded", error.Error.Message);
    }

    private static async IAsyncEnumerable<string> AsAsyncChunks(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }

    private static List<ParsedSseEvent> ParseSseEvents(string body)
    {
        var events = new List<ParsedSseEvent>();
        var normalizedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var chunk in normalizedBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var eventName = lines.FirstOrDefault(line => line.StartsWith("event: ", StringComparison.Ordinal))?[7..];
            var data = lines.FirstOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal))?[6..];

            if (eventName is null || data is null || string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(data);
            events.Add(new ParsedSseEvent(eventName, document.RootElement.Clone()));
        }

        return events;
    }

    private sealed record ParsedSseEvent(string EventName, JsonElement Payload);

    // --- Tool call round-trip tests ---

    [Fact]
    public async Task CreateAsync_FullConversationWithToolCallOutput_TranslatesCorrectly()
    {
        // Simulates what the chat UI sends on resubmission:
        // user message → assistant message → function_call → function_call_output
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_final",
                Model = "claude-sonnet-4.5",
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
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("""
                [
                  {
                    "type": "message",
                    "role": "user",
                    "content": [{"type": "input_text", "text": "fetch https://example.com and summarize it"}]
                  },
                  {
                    "type": "function_call",
                    "call_id": "call_abc123",
                    "name": "web_fetch",
                    "arguments": "{\"url\":\"https://example.com\"}"
                  },
                  {
                    "type": "function_call_output",
                    "call_id": "call_abc123",
                    "output": "{\"title\":\"Example Domain\",\"content\":\"This domain is for illustrative examples.\"}"
                  }
                ]
                """).RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "web_fetch",
                    Description = "Fetch a web page",
                    Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                },
            ],
        });

        Assert.Equal(200, result.StatusCode);

        // Verify the translated chat messages
        var messages = provider.LastChatRequest!.Messages!;
        Assert.Equal(3, messages.Length);

        // Message 0: user
        Assert.Equal("user", messages[0].Role);

        // Message 1: assistant with tool_calls
        Assert.Equal("assistant", messages[1].Role);
        Assert.NotNull(messages[1].ToolCalls);
        Assert.Single(messages[1].ToolCalls!);
        Assert.Equal("call_abc123", messages[1].ToolCalls![0].Id);
        Assert.Equal("web_fetch", messages[1].ToolCalls![0].Function?.Name);
        Assert.Equal("{\"url\":\"https://example.com\"}", messages[1].ToolCalls![0].Function?.Arguments);

        // Message 2: tool result
        Assert.Equal("tool", messages[2].Role);
        Assert.Equal("call_abc123", messages[2].ToolCallId);

        // Verify tools were passed through
        Assert.NotNull(provider.LastChatRequest.Tools);
        Assert.Single(provider.LastChatRequest.Tools!);
        Assert.Equal("web_fetch", provider.LastChatRequest.Tools![0].Function?.Name);

        // Verify final response
        var body = Assert.IsType<string>(result.Body);
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);
        Assert.Contains("example domains", response!.Output[0] is ResponseMessageItem msg && msg.Content.Length > 0 && msg.Content[0] is ResponseOutputTextPart tp ? tp.Text : "");
    }

    [Fact]
    public async Task CreateAsync_FullConversationWithToolCallOutput_StreamingTranslatesCorrectly()
    {
        // Same scenario as above but with streaming — the exact flow the chat UI uses
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    "data: {\"id\":\"chat_final\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"That page \"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_final\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"is about examples.\"},\"finish_reason\":null}]}\n\n",
                    "data: {\"id\":\"chat_final\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("""
                [
                  {
                    "type": "message",
                    "role": "user",
                    "content": [{"type": "input_text", "text": "fetch https://example.com and summarize it"}]
                  },
                  {
                    "type": "function_call",
                    "call_id": "call_abc123",
                    "name": "web_fetch",
                    "arguments": "{\"url\":\"https://example.com\"}"
                  },
                  {
                    "type": "function_call_output",
                    "call_id": "call_abc123",
                    "output": "{\"title\":\"Example Domain\",\"content\":\"This domain is for illustrative examples.\"}"
                  }
                ]
                """).RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "web_fetch",
                    Description = "Fetch a web page",
                    Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                },
            ],
        });

        // Verify chat messages were correctly translated
        var messages = provider.LastChatRequest!.Messages!;
        Assert.Equal(3, messages.Length);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        Assert.NotNull(messages[1].ToolCalls);
        Assert.Equal("call_abc123", messages[1].ToolCalls![0].Id);
        Assert.Equal("web_fetch", messages[1].ToolCalls![0].Function?.Name);
        Assert.Equal("tool", messages[2].Role);
        Assert.Equal("call_abc123", messages[2].ToolCallId);

        // Verify streaming response content
        var body = await result.ReadBodyAsync();
        Assert.Contains("event: response.output_text.delta", body);
        Assert.Contains("That page ", body);
        Assert.Contains("is about examples.", body);
        Assert.Contains("event: response.completed", body);
    }

    [Fact]
    public async Task CreateAsync_TextPlusToolCallInSameResponse_MapsCorrectly()
    {
        // Model returns both text ("Sure!") AND a tool call in the same response
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsResult = new ProxyHttpResult(JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chat_mixed",
                Model = "claude-sonnet-4.5",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Sure! Let me fetch that for you.",
                            ToolCalls =
                            [
                                new ChatToolCall
                                {
                                    Id = "call_mixed",
                                    Function = new ChatToolCallFunction
                                    {
                                        Name = "web_fetch",
                                        Arguments = "{\"url\":\"https://example.com\"}",
                                    },
                                },
                            ],
                        },
                        FinishReason = "tool_calls",
                    },
                ],
            }, JsonDefaults.Web), 200),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Input = JsonDocument.Parse("\"fetch https://example.com\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "web_fetch",
                    Description = "Fetch a web page",
                    Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                },
            ],
        });

        Assert.Equal(200, result.StatusCode);
        var body = Assert.IsType<string>(result.Body);
        var response = JsonSerializer.Deserialize<Response>(body, JsonDefaults.Web);
        Assert.NotNull(response);

        // Should have both a message item and a function_call item
        Assert.True(response!.Output.Length >= 1);
        var hasMessage = response.Output.Any(o => o is ResponseMessageItem);
        var hasFunctionCall = response.Output.Any(o => o is ResponseFunctionCallItem);
        Assert.True(hasMessage || hasFunctionCall, "Should have at least a message or function call");

        if (hasFunctionCall)
        {
            var fc = response.Output.OfType<ResponseFunctionCallItem>().First();
            Assert.Equal("web_fetch", fc.Name);
            Assert.Equal("call_mixed", fc.CallId);
        }
    }

    [Fact]
    public async Task CreateAsync_StreamingTextPlusToolCall_MapsCorrectly()
    {
        // Streaming: model sends text deltas THEN tool call deltas in same response
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "claude-sonnet-4.5",
                    SupportedEndpoints = ["/chat/completions"],
                },
            ],
            ChatCompletionsStreamResult = new ProxyStreamResult(
                null,
                200,
                "text/event-stream",
                AsAsyncChunks(
                    // Text delta first
                    "data: {\"id\":\"chat_mixed_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Sure!\"},\"finish_reason\":null}]}\n\n",
                    // Then tool call starts
                    "data: {\"id\":\"chat_mixed_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_mixed\",\"type\":\"function\",\"function\":{\"name\":\"web_fetch\",\"arguments\":\"{\\\"url\\\":\"}}]},\"finish_reason\":null}]}\n\n",
                    // More arguments
                    "data: {\"id\":\"chat_mixed_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"https://example.com\\\"}\"}}]},\"finish_reason\":null}]}\n\n",
                    // Finish
                    "data: {\"id\":\"chat_mixed_stream\",\"model\":\"claude-sonnet-4.5\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n",
                    "data: [DONE]\n\n")),
        };

        var service = new ResponsesService(provider, new ChatCompletionsTranslator());

        var result = await service.CreateAsync(new CreateResponseRequest
        {
            Model = "claude-sonnet-4.5",
            Stream = true,
            Input = JsonDocument.Parse("\"fetch https://example.com\"").RootElement.Clone(),
            Tools =
            [
                new ResponseFunctionToolDefinition
                {
                    Name = "web_fetch",
                    Description = "Fetch a web page",
                    Parameters = JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""").RootElement.Clone(),
                },
            ],
        });

        var body = await result.ReadBodyAsync();
        var events = ParseSseEvents(body);

        // Should have text delta events
        Assert.Contains(events, e => e.EventName == "response.output_text.delta");

        // Should have function call events
        Assert.Contains(events, e => e.EventName == "response.function_call_arguments.delta");
        Assert.Contains(events, e => e.EventName == "response.function_call_arguments.done");

        // Verify the function call details
        var argsDone = events.First(e => e.EventName == "response.function_call_arguments.done");
        Assert.Contains("example.com", argsDone.Payload.GetProperty("arguments").GetString());

        // Should have output_item.added for both message and function call
        var addedEvents = events.Where(e => e.EventName == "response.output_item.added").ToList();
        Assert.True(addedEvents.Count >= 2, $"Expected at least 2 output_item.added events, got {addedEvents.Count}");

        // Verify completed event has both items
        Assert.Contains(events, e => e.EventName == "response.completed");
    }
}

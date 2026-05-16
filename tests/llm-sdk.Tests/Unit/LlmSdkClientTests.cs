using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;
using LlmSdk.Infrastructure;
using LlmSdk.Proxy;
using LlmSdk.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LlmSdkClientTests
{
    [Fact]
    public async Task CreateResponseAsync_WithRequest_ReturnsDeserializedResponseOnSuccess()
    {
        var expected = CreateResponse("resp_123", "Hello from Copilot");
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(expected, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var response = await client.CreateResponseAsync(CreateResponseRequest());

        Assert.Equal(expected.Id, response.Id);
        Assert.Equal("Hello from Copilot", response.GetOutputText());
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateResponseAsync_WithRequest_ThrowsLlmSdkExceptionOnErrorStatus()
    {
        var body = JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Model not found.",
                Type = ErrorTypes.InvalidRequestError,
                Param = "model",
                Code = ErrorCodes.ModelNotFound,
            },
        }, JsonDefaults.Web);
        var service = new StubResponsesService(ResponseHttpResult.FromBody(body, 404, "application/json"));
        var client = CreateClient(responsesService: service);

        var exception = await Assert.ThrowsAsync<ModelNotFoundException>(() => client.CreateResponseAsync(CreateResponseRequest()));

        Assert.Equal("Model not found.", exception.Message);
        Assert.Equal("model", exception.Param);
    }

    [Fact]
    public async Task CreateResponseAsync_WithModelAndInput_BuildsRequestAndReturnsResponse()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateResponse("resp_234", "Convenience"), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var response = await client.CreateResponseAsync("gpt-5.4-mini", "Hello!");

        Assert.Equal("resp_234", response.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Equal("Hello!", service.LastRequest?.Input.GetString());
        Assert.False(service.LastRequest?.Stream ?? false);
    }

    [Fact]
    public async Task CreateResponseAsync_WithMissingModel_UsesDefaultModel()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateResponse("resp_345", "Default model"), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(
            responsesService: service,
            options: new LlmSdkOptions { DefaultModel = "claude-sonnet-4.5" });

        _ = await client.CreateResponseAsync(model: null, "Hello!");

        Assert.Equal("claude-sonnet-4.5", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateResponseAsync_WithMissingModelAndNoDefault_ThrowsArgumentException()
    {
        var client = CreateClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.CreateResponseAsync(model: null, "Hello!"));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public async Task CreateResponseAsync_WithPerCallOptions_PreservesOptions()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateResponse("resp_options", "Options"), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        await client.CreateResponseAsync(new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Input = JsonSerializer.SerializeToElement("Hello!", JsonDefaults.Web),
            RequestId = "request-123",
            CorrelationId = "correlation-123",
            Metadata = new Dictionary<string, string> { ["traceId"] = "trace-123" },
            TimeoutMs = 10000,
            MaxRetries = 1,
            MaxRetryDelayMs = 500,
        });

        Assert.Equal("request-123", service.LastRequest?.RequestId);
        Assert.Equal("correlation-123", service.LastRequest?.CorrelationId);
        Assert.Equal("trace-123", Assert.IsType<Dictionary<string, string>>(service.LastRequest?.Metadata)["traceId"]);
        Assert.Equal(10000, service.LastRequest?.TimeoutMs);
        Assert.Equal(1, service.LastRequest?.MaxRetries);
        Assert.Equal(500, service.LastRequest?.MaxRetryDelayMs);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithRequest_ReturnsDeserializedResponseOnSuccess()
    {
        var expected = new ChatCompletionResponse
        {
            Id = "chatcmpl_123",
            Model = "gpt-5.4-mini",
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = "Hello from chat",
                    },
                },
            ],
        };
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(expected, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(chatService: service);

        var response = await client.CreateChatCompletionAsync(CreateChatCompletionRequest());

        Assert.Equal(expected.Id, response.Id);
        Assert.Equal("Hello from chat", response.GetMessageText());
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithRequest_ThrowsLlmSdkExceptionOnError()
    {
        var body = JsonSerializer.Serialize(new OpenAIErrorResponse
        {
            Error = new OpenAIError
            {
                Message = "Not authenticated.",
                Type = "error",
                Code = ErrorCodes.AuthError,
            },
        }, JsonDefaults.Web);
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(body, 401, "application/json"));
        var client = CreateClient(chatService: service);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => client.CreateChatCompletionAsync(CreateChatCompletionRequest()));

        Assert.Equal(ErrorCodes.AuthError, exception.ErrorCode);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_WithModelAndMessage_BuildsRequestAndReturnsResponse()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chatcmpl_234",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Convenience chat",
                        },
                    },
                ],
            }, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(chatService: service);

        var response = await client.CreateChatCompletionAsync("gpt-5.4-mini", "Hello!");

        Assert.Equal("chatcmpl_234", response.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Single(service.LastRequest?.Messages ?? []);
        Assert.Equal("user", service.LastRequest?.Messages?[0].Role);
        Assert.Equal("Hello!", service.LastRequest?.Messages?[0].Content);
        Assert.False(service.LastRequest?.Stream ?? false);
    }

    [Fact]
    public async Task CreateResponseStreamAsync_WithRequest_YieldsParsedResponseStreamEvents()
    {
        var response = CreateResponse("resp_stream_123", "Streaming hello");
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            SplitSseBody(ResponseSseSerializer.Serialize(response)).ToArray())));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.CreateResponseStreamAsync(CreateResponseRequest()));

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e is OutputTextDeltaEvent delta && delta.Delta == "Streaming hello");
        Assert.Contains(events, e => e is ResponseCompletedEvent completed && completed.Response.Id == response.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task CreateResponseStreamAsync_WithRequest_ThrowsOnErrorStatusBeforeStreaming()
    {
        var body = JsonSerializer.Serialize(new ResponseErrorEnvelope
        {
            Error = new ResponseError
            {
                Message = "Model not found.",
                Type = ErrorTypes.InvalidRequestError,
                Param = "model",
                Code = ErrorCodes.ModelNotFound,
            },
        }, JsonDefaults.Web);
        var service = new StubResponsesService(ResponseHttpResult.FromBody(body, 404, "application/json"));
        var client = CreateClient(responsesService: service);

        var exception = await Assert.ThrowsAsync<ModelNotFoundException>(async () =>
            await CollectAsync(client.CreateResponseStreamAsync(CreateResponseRequest())));

        Assert.Equal("Model not found.", exception.Message);
        Assert.Equal("model", exception.Param);
    }

    [Fact]
    public async Task CreateResponseStreamAsync_WithModelAndInput_BuildsStreamingRequest()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            ResponseSseSerializer.SerializeEvent("response.output_text.delta", new
            {
                type = "response.output_text.delta",
                sequence_number = 1,
                item_id = "msg_123",
                output_index = 0,
                content_index = 0,
                delta = "Hello!",
            }),
            ResponseSseSerializer.SerializeDone())));
        var client = CreateClient(responsesService: service);

        var events = await CollectAsync(client.CreateResponseStreamAsync("gpt-5.4-mini", "Hello!"));

        var delta = Assert.Single(events);
        Assert.IsType<OutputTextDeltaEvent>(delta);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Equal("Hello!", service.LastRequest?.Input.GetString());
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task CreateChatCompletionStreamAsync_WithRequest_YieldsChatCompletionChunks()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            SerializeChatChunk(new ChatCompletionChunk
            {
                Id = "chatcmpl_stream_123",
                Model = "gpt-5.4-mini",
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatChunkDelta
                        {
                            Role = "assistant",
                            Content = "Hello from stream",
                        },
                    },
                ],
            }),
            "data: [DONE]\n\n")));
        var client = CreateClient(chatService: service);

        var chunks = await CollectAsync(client.CreateChatCompletionStreamAsync(CreateChatCompletionRequest()));

        var chunk = Assert.Single(chunks);
        Assert.Equal("chatcmpl_stream_123", chunk.Id);
        Assert.Equal("Hello from stream", chunk.Choices?[0].Delta?.Content);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task CreateChatCompletionStreamAsync_WithModelAndMessage_BuildsStreamingRequest()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromStream(ToAsyncEnumerable(
            SerializeChatChunk(new ChatCompletionChunk
            {
                Id = "chatcmpl_stream_234",
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatChunkDelta
                        {
                            Content = "Convenience stream",
                        },
                    },
                ],
            }),
            "data: [DONE]\n\n")));
        var client = CreateClient(chatService: service);

        var chunks = await CollectAsync(client.CreateChatCompletionStreamAsync("gpt-5.4-mini", "Hello!"));

        var chunk = Assert.Single(chunks);
        Assert.Equal("chatcmpl_stream_234", chunk.Id);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Single(service.LastRequest?.Messages ?? []);
        Assert.Equal("user", service.LastRequest?.Messages?[0].Role);
        Assert.Equal("Hello!", service.LastRequest?.Messages?[0].Content);
        Assert.True(service.LastRequest?.Stream);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsModelListFromModelListService()
    {
        var modelProvider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-5.4-mini",
                    Name = "GPT 5.4 Mini",
                    OwnedBy = "github-copilot",
                    SupportedEndpoints = ["/responses", "/chat/completions"],
                    TokenLimits = new ModelTokenLimits
                    {
                        MaxContextWindowTokens = 128000,
                        MaxPromptTokens = 120000,
                        MaxOutputTokens = 16000,
                    },
                },
                new ModelInfo
                {
                    Id = "embeddings-only",
                    SupportedEndpoints = ["/embeddings"],
                },
            ],
        };
        var client = CreateClient(modelProvider: modelProvider);

        var models = await client.ListModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("gpt-5.4-mini", model.Id);
        Assert.NotNull(model.ProxySupportedEndpoints);
        Assert.Equal(["/v1/responses", "/v1/chat/completions"], model.ProxySupportedEndpoints);
        Assert.NotNull(model.TokenLimits);
        Assert.Equal(128000, model.TokenLimits.MaxContextWindowTokens);
        Assert.Equal(120000, model.TokenLimits.MaxPromptTokens);
        Assert.Equal(16000, model.TokenLimits.MaxOutputTokens);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsMergedCatalogueModelInfo()
    {
        var modelProvider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-4o",
                    Name = "GPT-4o from upstream",
                    SupportedEndpoints = ["/responses"],
                },
            ],
        };
        var client = CreateClient(modelProvider: modelProvider);

        var models = await client.ListModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal("GPT-4o from upstream", model.DisplayName);
        Assert.Equal(128000, model.ContextWindow);
        Assert.True(model.SupportsVision);
        Assert.NotNull(model.Pricing);
    }

    [Fact]
    public async Task GetModelAsync_WithKnownModel_ReturnsMergedCatalogueModelInfo()
    {
        var modelProvider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-4o",
                    Name = "GPT-4o from upstream",
                    SupportedEndpoints = ["/responses"],
                },
            ],
        };
        var client = CreateClient(modelProvider: modelProvider);

        var model = await client.GetModelAsync("gpt-4o");

        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal("GPT-4o from upstream", model.DisplayName);
        Assert.Equal(128000, model.ContextWindow);
        Assert.True(model.SupportsVision);
        Assert.NotNull(model.Pricing);
    }

    [Fact]
    public async Task GetModelAsync_IsCaseInsensitive()
    {
        var modelProvider = new FakeModelProvider
        {
            Models =
            [
                new ModelInfo
                {
                    Id = "gpt-4o",
                    SupportedEndpoints = ["/responses"],
                },
            ],
        };
        var client = CreateClient(modelProvider: modelProvider);

        var model = await client.GetModelAsync("GPT-4O");

        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal(128000, model.ContextWindow);
    }

    [Fact]
    public async Task GetModelAsync_WithUnknownModel_ReturnsConservativeDefaults()
    {
        var client = CreateClient();

        var model = await client.GetModelAsync("unknown-model");

        Assert.Equal("unknown-model", model.Id);
        Assert.Equal("unknown-model", model.DisplayName);
        Assert.Null(model.ContextWindow);
        Assert.Null(model.MaxOutputTokens);
        Assert.False(model.SupportsVision);
        Assert.False(model.SupportsReasoning);
        Assert.Empty(model.SupportedThinkingLevels);
        Assert.Null(model.Pricing);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task GetModelAsync_WithBlankModel_ThrowsArgumentException(string id)
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetModelAsync(id));
    }

    [Fact]
    public async Task CompleteAsync_WithResponsesPreference_TranslatesContextAndReturnsAssistantMessage()
    {
        var expected = CreateResponse("resp_context_123", "Hello from context");
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(expected, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var message = await client.CompleteAsync(
            new Context
            {
                System = "Be concise.",
                Messages = [new UserMessage([new TextContent("Hello!")])],
            },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.Responses });

        Assert.Equal(StopReason.Stop, message.StopReason);
        var text = Assert.IsType<TextContent>(Assert.Single(message.Content));
        Assert.Equal("Hello from context", text.Text);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Equal("Be concise.", service.LastRequest?.Instructions);
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidToolArguments_ReturnsErrorToolResult()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateToolCallResponse("""{"city":123}"""), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var message = await client.CompleteAsync(
            new Context
            {
                Messages = [new UserMessage([new TextContent("Weather in London?")])],
                Tools = [CreateWeatherTool()],
            },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.Responses });

        var result = Assert.IsType<ToolResultContent>(Assert.Single(message.Content));
        Assert.Equal("call_1", result.ToolCallId);
        Assert.True(result.IsError);
        Assert.Contains("city must be string", result.Output);
    }

    [Fact]
    public async Task CompleteAsync_WithValidToolArguments_ReturnsToolCall()
    {
        var service = new StubResponsesService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(CreateToolCallResponse("""{"city":"London"}"""), JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(responsesService: service);

        var message = await client.CompleteAsync(
            new Context
            {
                Messages = [new UserMessage([new TextContent("Weather in London?")])],
                Tools = [CreateWeatherTool()],
            },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.Responses });

        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(message.Content));
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("""{"city":"London"}""", toolCall.ArgumentsJson);
    }

    [Fact]
    public async Task CompleteAsync_WithChatCompletionsPreference_TranslatesContextAndReturnsAssistantMessage()
    {
        var service = new StubChatCompletionsService(ResponseHttpResult.FromBody(
            JsonSerializer.Serialize(new ChatCompletionResponse
            {
                Id = "chatcmpl_context_123",
                Model = "gpt-5.4-mini",
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        FinishReason = "tool_calls",
                        Message = new ChatMessage
                        {
                            Role = "assistant",
                            ToolCalls =
                            [
                                new ChatToolCall
                                {
                                    Id = "call_1",
                                    Function = new ChatToolCallFunction
                                    {
                                        Name = "get_weather",
                                        Arguments = "{\"city\":\"London\"}",
                                    },
                                },
                            ],
                        },
                    },
                ],
            }, JsonDefaults.Web),
            200,
            "application/json"));
        var client = CreateClient(chatService: service);

        var message = await client.CompleteAsync(
            new Context
            {
                Messages = [new UserMessage([new TextContent("Weather in London?")])],
            },
            new CompletionOptions { Model = "gpt-5.4-mini", PreferredApi = CompletionApi.ChatCompletions });

        Assert.Equal(StopReason.ToolUse, message.StopReason);
        var toolCall = Assert.IsType<ToolCallContent>(Assert.Single(message.Content));
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("{\"city\":\"London\"}", toolCall.ArgumentsJson);
        Assert.Equal("gpt-5.4-mini", service.LastRequest?.Model);
        Assert.Single(service.LastRequest?.Messages ?? []);
    }

    private static LlmSdkClient CreateClient(
        StubResponsesService? responsesService = null,
        StubChatCompletionsService? chatService = null,
        FakeModelProvider? modelProvider = null,
        LlmSdkOptions? options = null)
    {
        return new LlmSdkClient(
            responsesService ?? new StubResponsesService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            chatService ?? new StubChatCompletionsService(ResponseHttpResult.FromBody("{}", 200, "application/json")),
            new ModelListService(modelProvider ?? new FakeModelProvider(), new EmbeddedModelCatalogue()),
            Options.Create(options ?? new LlmSdkOptions()));
    }

    private static CreateResponseRequest CreateResponseRequest(string? model = "gpt-5.4-mini", string input = "Hello!")
    {
        return new CreateResponseRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(input, JsonDefaults.Web),
        };
    }

    private static ChatCompletionRequest CreateChatCompletionRequest(string? model = "gpt-5.4-mini", string message = "Hello!")
    {
        return new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = message,
                },
            ],
        };
    }

    private static Response CreateResponse(string id, string text)
    {
        return new Response
        {
            Id = id,
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = text,
                        },
                    ],
                },
            ],
        };
    }

    private static Response CreateToolCallResponse(string argumentsJson)
    {
        return new Response
        {
            Id = "resp_tool_123",
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseFunctionCallItem
                {
                    Id = "item_123",
                    CallId = "call_1",
                    Name = "get_weather",
                    Arguments = argumentsJson,
                },
            ],
        };
    }

    private static ToolDefinition CreateWeatherTool()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "city" },
            additionalProperties = false,
            properties = new
            {
                city = new { type = "string" },
            },
        }, JsonDefaults.Web);

        return new ToolDefinition("get_weather", "Gets the weather.", schema, Strict: true);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var items = new List<T>();
        await foreach (var value in values)
        {
            items.Add(value);
        }

        return items;
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(params string[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }

    private static IEnumerable<string> SplitSseBody(string body)
    {
        foreach (var chunk in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            yield return $"{chunk}\n\n";
        }
    }

    private static string SerializeChatChunk(ChatCompletionChunk chunk) =>
        $"data: {JsonSerializer.Serialize(chunk, JsonDefaults.Web)}\n\n";

    private sealed class StubResponsesService(ResponseHttpResult result) : IResponsesService
    {
        public CreateResponseRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(CreateResponseRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubChatCompletionsService(ResponseHttpResult result) : IChatCompletionsService
    {
        public ChatCompletionRequest? LastRequest { get; private set; }

        public Task<ResponseHttpResult> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}

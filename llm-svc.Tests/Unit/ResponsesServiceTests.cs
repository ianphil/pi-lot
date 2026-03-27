using System.Text.Json;
using LlmSvc;
using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;
using LlmSvc.Core.Services;
using llm_svc.Tests.Fakes;

namespace llm_svc.Tests.Unit;

public sealed class ResponsesServiceTests
{
    [Fact]
    public async Task CreateAsync_TranslatesChatCompletionIntoResponsesShape()
    {
        var provider = new FakeModelProvider
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

        var response = JsonSerializer.Deserialize<Response>(result.Body, JsonDefaults.Web);
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
    public async Task CreateAsync_EmitsSpecLikeSseWhenStreamingRequested()
    {
        var provider = new FakeModelProvider
        {
            Models =
            [
                new ModelDescriptor
                {
                    Id = "gpt-5.4",
                    SupportedEndpoints = ["/responses"],
                },
            ],
            ResponsesResult = new ProxyHttpResult(JsonSerializer.Serialize(new ResponsesApiResponse
            {
                Id = "resp_123",
                Object = "response",
                Status = "completed",
                Model = "gpt-5.4",
                Output =
                [
                    new ResponseOutput
                    {
                        Id = "msg_123",
                        Type = "message",
                        Role = "assistant",
                        Content =
                        [
                            new ResponseContent
                            {
                                Type = "output_text",
                                Text = "stream me",
                            },
                        ],
                    },
                ],
                Usage = new ResponsesUsageInfo
                {
                    InputTokens = 2,
                    OutputTokens = 2,
                },
            }, JsonDefaults.Web), 200),
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
        Assert.Contains("event: response.created", result.Body);
        Assert.Contains("event: response.output_item.added", result.Body);
        Assert.Contains("event: response.output_text.delta", result.Body);
        Assert.Contains("event: response.completed", result.Body);
        Assert.Contains("data: [DONE]", result.Body);
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

        var response = JsonSerializer.Deserialize<Response>(result.Body, JsonDefaults.Web);
        Assert.NotNull(response);
        var functionCall = Assert.IsType<ResponseFunctionCallItem>(response!.Output[0]);
        Assert.Equal("get_weather", functionCall.Name);
        Assert.Equal("call_weather", functionCall.CallId);
        Assert.Equal("{\"city\":\"Seattle\"}", functionCall.Arguments);
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

        var error = JsonSerializer.Deserialize<ResponseErrorEnvelope>(result.Body, JsonDefaults.Web);
        Assert.NotNull(error);
        Assert.Equal("model_not_found", error!.Error.Code);
        Assert.Equal("model", error.Error.Param);
    }
}

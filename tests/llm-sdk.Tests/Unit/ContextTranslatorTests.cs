using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ContextTranslatorTests
{
    [Fact]
    public void ToCreateResponseRequest_WithTextContext_MatchesEquivalentRawRequest()
    {
        var context = new Context
        {
            System = "Be concise.",
            Messages =
            [
                new UserMessage([new TextContent("Hello!")]),
            ],
        };
        var options = new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            MaxOutputTokens = 64,
            Temperature = 0.2,
            TopP = 0.9,
        };
        var expected = new CreateResponseRequest
        {
            Model = "gpt-5.4-mini",
            Instructions = "Be concise.",
            Input = JsonSerializer.SerializeToElement(new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = "Hello!" },
                    },
                },
            }, JsonDefaults.Web),
            Tools = [],
            MaxOutputTokens = 64,
            Temperature = 0.2,
            TopP = 0.9,
        };

        var request = ContextTranslator.ToCreateResponseRequest(context, options);

        Assert.Equal(JsonSerializer.Serialize(expected, JsonDefaults.Web), JsonSerializer.Serialize(request, JsonDefaults.Web));
    }

    [Fact]
    public void ToChatCompletionRequest_WithToolContext_MatchesEquivalentRawRequest()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                city = new { type = "string" },
            },
        }, JsonDefaults.Web);
        var context = new Context
        {
            System = "Use tools when needed.",
            Tools =
            [
                new ToolDefinition("get_weather", "Gets the weather.", parameters, Strict: true),
            ],
            Messages =
            [
                new UserMessage([new TextContent("Weather in London?")]),
                new AssistantMessage(
                [
                    new ToolCallContent("call_1", "get_weather", "{\"city\":\"London\"}"),
                ], StopReason.ToolUse),
                new ToolMessage("call_1", [new ToolResultContent("call_1", "{\"temperature\":21}")]),
            ],
        };
        var expected = new ChatCompletionRequest
        {
            Model = "gpt-5.4-mini",
            Messages =
            [
                new ChatMessage { Role = "system", Content = "Use tools when needed." },
                new ChatMessage { Role = "user", Content = "Weather in London?" },
                new ChatMessage
                {
                    Role = "assistant",
                    Content = null,
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
                new ChatMessage { Role = "tool", ToolCallId = "call_1", Content = "{\"temperature\":21}" },
            ],
            Tools =
            [
                new ChatToolDefinition
                {
                    Function = new ChatToolFunctionDefinition
                    {
                        Name = "get_weather",
                        Description = "Gets the weather.",
                        Parameters = parameters,
                        Strict = true,
                    },
                },
            ],
            MaxCompletionTokens = 64,
            Temperature = 0.2,
            TopP = 0.9,
        };

        var request = ContextTranslator.ToChatCompletionRequest(context, new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            MaxOutputTokens = 64,
            Temperature = 0.2,
            TopP = 0.9,
        });

        Assert.Equal(JsonSerializer.Serialize(expected, JsonDefaults.Web), JsonSerializer.Serialize(request, JsonDefaults.Web));
    }

    [Fact]
    public void ToCreateResponseRequest_WithSessionId_PropagatesPromptCacheKey()
    {
        var context = new Context
        {
            Messages = [new UserMessage([new TextContent("Hello!")])],
        };

        var request = ContextTranslator.ToCreateResponseRequest(context, new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            Cache = CacheRetention.Short,
            SessionId = "session-123",
        });

        Assert.Equal("session-123", request.PromptCacheKey);
        var json = JsonSerializer.Serialize(request, JsonDefaults.Web);
        Assert.Contains("\"prompt_cache_key\":\"session-123\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToChatCompletionRequest_WithSessionId_PropagatesUser()
    {
        var context = new Context
        {
            Messages = [new UserMessage([new TextContent("Hello!")])],
        };

        var request = ContextTranslator.ToChatCompletionRequest(context, new CompletionOptions
        {
            Model = "gpt-5.4-mini",
            Cache = CacheRetention.Long,
            SessionId = "session-123",
        });

        Assert.Equal("session-123", request.User);
        var json = JsonSerializer.Serialize(request, JsonDefaults.Web);
        Assert.Contains("\"user\":\"session-123\"", json, StringComparison.Ordinal);
    }
}

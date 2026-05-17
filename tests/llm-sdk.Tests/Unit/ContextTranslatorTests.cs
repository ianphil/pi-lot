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

    [Fact]
    public void ToCreateResponseRequest_WithThinkingOption_MapsReasoningEffort()
    {
        var request = ContextTranslator.ToCreateResponseRequest(
            new Context { Messages = [new UserMessage([new TextContent("Think carefully.")])] },
            new CompletionOptions { Thinking = ThinkingLevel.XHigh });

        Assert.Equal("xhigh", request.Reasoning?.Effort);
    }

    [Fact]
    public void ToChatCompletionRequest_WithThinkingOption_MapsReasoningEffort()
    {
        var request = ContextTranslator.ToChatCompletionRequest(
            new Context { Messages = [new UserMessage([new TextContent("Think carefully.")])] },
            new CompletionOptions { Thinking = ThinkingLevel.Minimal });

        Assert.Equal("minimal", request.Reasoning?.Effort);
    }

    [Fact]
    public void ToCreateResponseRequest_WithRedactedThinkingContent_PreservesSignatureForReplay()
    {
        var context = new Context
        {
            Messages =
            [
                new AssistantMessage(
                [
                    new ThinkingContent(string.Empty, Redacted: true, Signature: "encrypted_reasoning"),
                    new TextContent("Prior answer."),
                ], StopReason.Stop),
            ],
        };

        var request = ContextTranslator.ToCreateResponseRequest(context);
        var json = request.Input.GetRawText();

        Assert.Contains("\"type\":\"summary_text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"redacted\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"signature\":\"encrypted_reasoning\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToAssistantMessage_WithEncryptedReasoning_PreservesRedactedThinkingSignature()
    {
        var message = ContextTranslator.ToAssistantMessage(new Response
        {
            Id = "resp_123",
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseReasoningItem
                {
                    Id = "rs_123",
                    EncryptedContent = "encrypted_reasoning",
                },
            ],
        });

        var thinking = Assert.IsType<ThinkingContent>(Assert.Single(message.Content));
        Assert.True(thinking.Redacted);
        Assert.Equal("encrypted_reasoning", thinking.Signature);
    }

    [Fact]
    public void ToCreateResponseRequest_WithImageContent_EmitsResponsesImagePart()
    {
        var request = ContextTranslator.ToCreateResponseRequest(new Context
        {
            Messages =
            [
                new UserMessage(
                [
                    new TextContent("Describe this image."),
                    new ImageContent("image/png", "iVBORw0KGgo="),
                ]),
            ],
        });

        var json = request.Input.GetRawText();

        Assert.Contains("\"type\":\"input_image\"", json, StringComparison.Ordinal);
        Assert.Contains("\"image_url\":\"data:image/png;base64,iVBORw0KGgo=\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToChatCompletionRequest_WithImageContent_EmitsChatImagePart()
    {
        var request = ContextTranslator.ToChatCompletionRequest(new Context
        {
            Messages =
            [
                new UserMessage(
                [
                    new TextContent("Describe this image."),
                    new ImageContent("image/png", "iVBORw0KGgo="),
                ]),
            ],
        });

        Assert.NotNull(request.Messages);
        var content = Assert.Single(request.Messages).Content;
        Assert.NotNull(content);
        var json = JsonSerializer.Serialize(content, JsonDefaults.Web);

        Assert.Contains("\"type\":\"image_url\"", json, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"data:image/png;base64,iVBORw0KGgo=\"", json, StringComparison.Ordinal);
    }
}

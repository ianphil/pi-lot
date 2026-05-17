using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Core.Services;

public static class ContextTranslator
{
    public static CreateResponseRequest ToCreateResponseRequest(Context context, CompletionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CreateResponseRequest
        {
            Model = options?.Model,
            Instructions = context.System,
            Input = JsonSerializer.SerializeToElement(context.Messages.Select(ToResponseInputItem).ToArray(), JsonDefaults.Web),
            Tools = context.Tools.Select(ToResponseTool).ToArray(),
            ToolChoice = ToResponseToolChoice(options?.ToolChoice),
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            Reasoning = ToResponseReasoning(options?.Thinking),
            Metadata = options?.Metadata,
            Headers = options?.Headers,
            RequestId = options?.RequestId,
            CorrelationId = options?.CorrelationId,
            TimeoutMs = options?.TimeoutMs,
            MaxRetries = options?.MaxRetries,
            MaxRetryDelayMs = options?.MaxRetryDelayMs,
            PromptCacheKey = options?.SessionId,
            OnPayload = options?.OnPayload,
            OnResponse = options?.OnResponse,
        };
    }

    public static ChatCompletionRequest ToChatCompletionRequest(Context context, CompletionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(context.System))
        {
            messages.Add(new ChatMessage { Role = "system", Content = context.System });
        }

        messages.AddRange(context.Messages.Select(ToChatMessage));

        return new ChatCompletionRequest
        {
            Model = options?.Model,
            Messages = messages.ToArray(),
            User = options?.SessionId,
            Tools = context.Tools.Select(ToChatTool).ToArray(),
            ToolChoice = ToChatToolChoice(options?.ToolChoice),
            MaxCompletionTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            Reasoning = ToResponseReasoning(options?.Thinking),
            Headers = options?.Headers,
            RequestId = options?.RequestId,
            CorrelationId = options?.CorrelationId,
            TimeoutMs = options?.TimeoutMs,
            MaxRetries = options?.MaxRetries,
            MaxRetryDelayMs = options?.MaxRetryDelayMs,
            Metadata = options?.Metadata,
            OnPayload = options?.OnPayload,
            OnResponse = options?.OnResponse,
        };
    }

    public static AssistantMessage ToAssistantMessage(Response response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = new List<ContentBlock>();
        foreach (var item in response.Output)
        {
            switch (item)
            {
                case ResponseMessageItem message:
                    content.AddRange(message.Content.Select(ToContentBlock));
                    break;
                case ResponseFunctionCallItem toolCall:
                    content.Add(new ToolCallContent(toolCall.CallId, toolCall.Name, toolCall.Arguments));
                    break;
                case ResponseReasoningItem reasoning:
                    AddReasoningContent(content, reasoning);
                    break;
            }
        }

        return new AssistantMessage(content, ToStopReason(response.Status), UsageMath.FromResponseUsage(response.Usage), response.Error?.Message);
    }

    public static AssistantMessage ToAssistantMessage(Response response, IReadOnlyList<ToolDefinition> tools) =>
        ValidateToolCalls(ToAssistantMessage(response), tools);

    public static AssistantMessage ToAssistantMessage(ChatCompletionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var choice = response.Choices?.FirstOrDefault();
        if (choice?.Message is null)
        {
            return new AssistantMessage([], StopReason.Stop);
        }

        var content = new List<ContentBlock>();
        AddChatContent(content, choice.Message.Content);

        if (choice.Message.ToolCalls is not null)
        {
            content.AddRange(choice.Message.ToolCalls
                .Where(static toolCall => toolCall.Id is not null && toolCall.Function?.Name is not null)
                .Select(static toolCall => new ToolCallContent(
                    toolCall.Id!,
                    toolCall.Function!.Name!,
                    toolCall.Function.Arguments ?? "{}")));
        }

        return new AssistantMessage(content, ToStopReason(choice.FinishReason), UsageMath.FromUsageInfo(response.Usage));
    }

    public static AssistantMessage ToAssistantMessage(ChatCompletionResponse response, IReadOnlyList<ToolDefinition> tools) =>
        ValidateToolCalls(ToAssistantMessage(response), tools);

    private static object ToResponseInputItem(Message message) => message switch
    {
        UserMessage user => new
        {
            role = "user",
            content = user.Content.Select(ToResponseContentPart).ToArray(),
        },
        AssistantMessage assistant => new
        {
            role = "assistant",
            content = assistant.Content.Select(ToResponseContentPart).ToArray(),
        },
        ToolMessage tool => new
        {
            type = "function_call_output",
            call_id = tool.ToolCallId,
            output = ContentToText(tool.Content),
        },
        _ => throw new InvalidOperationException($"Unsupported message type '{message.GetType().Name}'."),
    };

    private static object ToResponseContentPart(ContentBlock block) => block switch
    {
        TextContent text => new { type = "input_text", text = text.Text },
        ImageContent image => new { type = "input_image", image_url = $"data:{image.MediaType};base64,{image.Base64Data}" },
        ThinkingContent thinking => ToResponseThinkingPart(thinking),
        ToolCallContent toolCall => new { type = "function_call", call_id = toolCall.Id, name = toolCall.Name, arguments = toolCall.ArgumentsJson },
        ToolResultContent toolResult => new { type = "function_call_output", call_id = toolResult.ToolCallId, output = toolResult.Output },
        _ => throw new InvalidOperationException($"Unsupported content block type '{block.GetType().Name}'."),
    };

    private static object ToResponseThinkingPart(ThinkingContent thinking) =>
        thinking.Redacted || !string.IsNullOrWhiteSpace(thinking.Signature)
            ? new { type = "summary_text", text = thinking.Text, redacted = thinking.Redacted, signature = thinking.Signature }
            : new { type = "summary_text", text = thinking.Text };

    private static ChatMessage ToChatMessage(Message message) => message switch
    {
        UserMessage user => new ChatMessage
        {
            Role = "user",
            Content = ToChatContent(user.Content),
        },
        AssistantMessage assistant => new ChatMessage
        {
            Role = "assistant",
            Content = ToChatContent(assistant.Content.Where(static block => block is not ToolCallContent).ToArray()),
            ToolCalls = assistant.Content.OfType<ToolCallContent>().Select(ToChatToolCall).ToArray() is { Length: > 0 } toolCalls
                ? toolCalls
                : null,
        },
        ToolMessage tool => new ChatMessage
        {
            Role = "tool",
            ToolCallId = tool.ToolCallId,
            Content = ContentToText(tool.Content),
        },
        _ => throw new InvalidOperationException($"Unsupported message type '{message.GetType().Name}'."),
    };

    private static object? ToChatContent(IReadOnlyList<ContentBlock> content)
    {
        var nonThinking = content.Where(static block => block is not ThinkingContent).ToArray();
        if (nonThinking.Length == 0)
        {
            return null;
        }

        if (nonThinking.All(static block => block is TextContent))
        {
            return string.Concat(nonThinking.Cast<TextContent>().Select(static text => text.Text));
        }

        return nonThinking.Select(ToChatContentPart).ToArray();
    }

    private static object ToChatContentPart(ContentBlock block) => block switch
    {
        TextContent text => new { type = "text", text = text.Text },
        ImageContent image => new { type = "image_url", image_url = new { url = $"data:{image.MediaType};base64,{image.Base64Data}" } },
        ToolResultContent result => new { type = "text", text = result.Output },
        ToolCallContent toolCall => new { type = "text", text = toolCall.ArgumentsJson },
        ThinkingContent thinking => new { type = "text", text = thinking.Text },
        _ => throw new InvalidOperationException($"Unsupported content block type '{block.GetType().Name}'."),
    };

    private static string ContentToText(IReadOnlyList<ContentBlock> content) =>
        string.Concat(content.Select(static block => block switch
        {
            TextContent text => text.Text,
            ToolResultContent toolResult => toolResult.Output,
            ThinkingContent thinking => thinking.Text,
            ToolCallContent toolCall => toolCall.ArgumentsJson,
            ImageContent image => $"data:{image.MediaType};base64,{image.Base64Data}",
            _ => string.Empty,
        }));

    private static ResponseFunctionToolDefinition ToResponseTool(ToolDefinition tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        Parameters = tool.Parameters,
        Strict = tool.Strict,
    };

    private static ChatToolDefinition ToChatTool(ToolDefinition tool) => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.Parameters,
            Strict = tool.Strict,
        },
    };

    private static ChatToolCall ToChatToolCall(ToolCallContent toolCall) => new()
    {
        Id = toolCall.Id,
        Function = new ChatToolCallFunction
        {
            Name = toolCall.Name,
            Arguments = toolCall.ArgumentsJson,
        },
    };

    private static JsonElement? ToResponseToolChoice(ToolChoice? toolChoice)
    {
        var value = ToolChoiceToWireObject(toolChoice, responses: true);
        return value is null ? null : JsonSerializer.SerializeToElement(value, JsonDefaults.Web);
    }

    private static object? ToChatToolChoice(ToolChoice? toolChoice) => ToolChoiceToWireObject(toolChoice, responses: false);

    internal static AssistantMessage ValidateToolCalls(AssistantMessage message, IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0 || !message.Content.OfType<ToolCallContent>().Any())
        {
            return message;
        }

        var toolsByName = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            toolsByName.TryAdd(tool.Name, tool);
        }

        var content = new List<ContentBlock>(message.Content.Count);
        var changed = false;

        foreach (var block in message.Content)
        {
            if (block is not ToolCallContent toolCall)
            {
                content.Add(block);
                continue;
            }

            if (!toolsByName.TryGetValue(toolCall.Name, out var tool))
            {
                content.Add(new ToolResultContent(
                    toolCall.Id,
                    $"Tool argument validation failed: tool '{toolCall.Name}' is not declared.",
                    IsError: true));
                changed = true;
                continue;
            }

            var result = ToolValidator.Validate(tool, toolCall.ArgumentsJson);
            if (result.IsValid)
            {
                content.Add(block);
                continue;
            }

            content.Add(result.ToErrorResult(toolCall.Id));
            changed = true;
        }

        return changed ? message with { Content = content } : message;
    }

    private static object? ToolChoiceToWireObject(ToolChoice? toolChoice, bool responses) => toolChoice?.Kind switch
    {
        null => null,
        ToolChoiceKind.Auto => "auto",
        ToolChoiceKind.None => "none",
        ToolChoiceKind.Required => "required",
        ToolChoiceKind.Function => responses
            ? new { type = "function", name = toolChoice.FunctionName }
            : new { type = "function", function = new { name = toolChoice.FunctionName } },
        _ => throw new InvalidOperationException($"Unsupported tool choice '{toolChoice.Kind}'."),
    };

    private static ContentBlock ToContentBlock(ResponseContentPart part) => part switch
    {
        ResponseOutputTextPart text => new TextContent(text.Text),
        ResponseInputTextPart text => new TextContent(text.Text),
        ResponseSummaryTextPart summary => new ThinkingContent(summary.Text),
        _ => throw new InvalidOperationException($"Unsupported response content part type '{part.GetType().Name}'."),
    };

    private static void AddReasoningContent(List<ContentBlock> content, ResponseReasoningItem reasoning)
    {
        if (reasoning.Content is not null)
        {
            content.AddRange(reasoning.Content.Select(ToContentBlock));
        }

        if (reasoning.Summary is not null)
        {
            content.AddRange(reasoning.Summary.Select(static summary => new ThinkingContent(summary.Text)));
        }

        if (!string.IsNullOrWhiteSpace(reasoning.EncryptedContent))
        {
            content.Add(new ThinkingContent(string.Empty, Redacted: true, Signature: reasoning.EncryptedContent));
        }
    }

    private static ResponseReasoning? ToResponseReasoning(ThinkingLevel? thinking) =>
        thinking.HasValue ? new ResponseReasoning { Effort = ToWireThinkingLevel(thinking.Value) } : null;

    private static string ToWireThinkingLevel(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        ThinkingLevel.XHigh => "xhigh",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private static void AddChatContent(List<ContentBlock> content, object? chatContent)
    {
        switch (chatContent)
        {
            case null:
                return;
            case string text:
                content.Add(new TextContent(text));
                return;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                content.Add(new TextContent(element.GetString() ?? string.Empty));
                return;
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                content.AddRange(element.EnumerateArray().Select(ToContentBlock));
                return;
            default:
                content.Add(new TextContent(chatContent.ToString() ?? string.Empty));
                return;
        }
    }

    private static ContentBlock ToContentBlock(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var type))
        {
            return new TextContent(element.GetRawText());
        }

        return type.GetString() switch
        {
            "text" when element.TryGetProperty("text", out var text) => new TextContent(text.GetString() ?? string.Empty),
            "image_url" when element.TryGetProperty("image_url", out var imageUrl) => new ImageContent("image/unknown", imageUrl.GetRawText()),
            _ => new TextContent(element.GetRawText()),
        };
    }

    private static StopReason ToStopReason(string? value) => value switch
    {
        ResponseStatuses.Incomplete or "length" => StopReason.Length,
        ResponseStatuses.Failed => StopReason.Error,
        "tool_calls" => StopReason.ToolUse,
        "content_filter" => StopReason.ContentFilter,
        _ => StopReason.Stop,
    };
}

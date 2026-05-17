using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmAgent;

public sealed class AgentContext
{
    private readonly List<AgentContextItem> _items = [];

    public IReadOnlyList<AgentContextItem> Items => _items;

    public void AddUserMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _items.Add(new UserMessageContextItem(text));
    }

    public void AddAssistantMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _items.Add(new AssistantMessageContextItem(text));
    }

    public void AddAssistantMessage(AssistantMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _items.Add(new AssistantResponseContextItem(message));
    }

    public void AddResponseOutput(IEnumerable<ResponseItem> outputItems)
    {
        ArgumentNullException.ThrowIfNull(outputItems);

        foreach (var outputItem in outputItems)
        {
            ArgumentNullException.ThrowIfNull(outputItem);
            _items.Add(new ResponseOutputContextItem(outputItem));
        }
    }

    public void AddToolResult(string callId, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(output);

        _items.Add(new ToolResultContextItem(callId, output));
    }

    public Context ToSdkContext(string? instructions = null, IReadOnlyList<ToolDefinition>? tools = null) => new()
    {
        System = instructions,
        Messages = _items.Select(ToSdkMessage).ToArray(),
        Tools = tools ?? [],
    };

    public JsonElement ToResponseInput()
    {
        var items = _items.SelectMany(SerializeItems).ToArray();
        return JsonSerializer.SerializeToElement(items, JsonDefaults.Web);
    }

    internal JsonElement SerializeInput() => ToResponseInput();

    private static IEnumerable<JsonElement> SerializeItems(AgentContextItem item)
    {
        switch (item)
        {
            case UserMessageContextItem userMessage:
                yield return JsonSerializer.SerializeToElement(
                    new
                    {
                        type = "message",
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "input_text",
                                text = userMessage.Text,
                            },
                        },
                    },
                    JsonDefaults.Web);
                break;

            case AssistantMessageContextItem assistantMessage:
                yield return JsonSerializer.SerializeToElement(
                    new
                    {
                        type = "message",
                        role = "assistant",
                        content = new[]
                        {
                            new
                            {
                                type = "output_text",
                                text = assistantMessage.Text,
                            },
                        },
                    },
                    JsonDefaults.Web);
                break;

            case AssistantResponseContextItem assistantMessage:
                var request = ContextTranslator.ToCreateResponseRequest(new Context { Messages = [assistantMessage.Message] });
                foreach (var inputItem in request.Input.EnumerateArray())
                {
                    yield return inputItem.Clone();
                }

                break;

            case ResponseOutputContextItem responseOutput:
                yield return JsonSerializer.SerializeToElement(responseOutput.Item, JsonDefaults.Web);
                break;

            case ToolResultContextItem toolResult:
                yield return JsonSerializer.SerializeToElement(
                    new
                    {
                        type = "function_call_output",
                        call_id = toolResult.CallId,
                        output = toolResult.Output,
                    },
                    JsonDefaults.Web);
                break;

            default:
                throw new InvalidOperationException($"Unknown context item type: {item.GetType().Name}");
        }
    }

    private static Message ToSdkMessage(AgentContextItem item) => item switch
    {
        UserMessageContextItem userMessage => new UserMessage([new TextContent(userMessage.Text)]),
        AssistantMessageContextItem assistantMessage => new AssistantMessage(
            [new TextContent(assistantMessage.Text)],
            StopReason.Stop),
        AssistantResponseContextItem assistantMessage => assistantMessage.Message,
        ResponseOutputContextItem responseOutput => new AssistantMessage(
            [ToContentBlock(responseOutput.Item)],
            StopReason.Stop),
        ToolResultContextItem toolResult => new ToolMessage(
            toolResult.CallId,
            [new TextContent(toolResult.Output)]),
        _ => throw new InvalidOperationException($"Unknown context item type: {item.GetType().Name}"),
    };

    private static ContentBlock ToContentBlock(ResponseItem item) => item switch
    {
        ResponseMessageItem message => new TextContent(string.Concat(message.Content.Select(static part => part switch
        {
            ResponseOutputTextPart text => text.Text,
            ResponseInputTextPart text => text.Text,
            ResponseSummaryTextPart text => text.Text,
            _ => string.Empty,
        }))),
        ResponseFunctionCallItem functionCall => new ToolCallContent(functionCall.CallId, functionCall.Name, functionCall.Arguments),
        ResponseReasoningItem reasoning => new ThinkingContent(string.Concat(
            (reasoning.Content ?? []).Select(static part => part switch
            {
                ResponseSummaryTextPart text => text.Text,
                _ => string.Empty,
            }).Concat((reasoning.Summary ?? []).Select(static part => part.Text)))),
        _ => new TextContent(JsonSerializer.Serialize(item, JsonDefaults.Web)),
    };
}

public abstract record AgentContextItem;

public sealed record UserMessageContextItem(string Text) : AgentContextItem;

public sealed record AssistantMessageContextItem(string Text) : AgentContextItem;

public sealed record AssistantResponseContextItem(AssistantMessage Message) : AgentContextItem;

public sealed record ResponseOutputContextItem(ResponseItem Item) : AgentContextItem;

public sealed record ToolResultContextItem(string CallId, string Output) : AgentContextItem;

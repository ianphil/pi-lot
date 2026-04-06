using System.Text.Json;
using LlmSdk.Core.Models;

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

    internal JsonElement SerializeInput()
    {
        var items = _items.Select(SerializeItem).ToArray();
        return JsonSerializer.SerializeToElement(items, JsonDefaults.Web);
    }

    private static JsonElement SerializeItem(AgentContextItem item) => item switch
    {
        UserMessageContextItem userMessage => JsonSerializer.SerializeToElement(
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
            JsonDefaults.Web),
        ResponseOutputContextItem responseOutput => JsonSerializer.SerializeToElement(responseOutput.Item, JsonDefaults.Web),
        ToolResultContextItem toolResult => JsonSerializer.SerializeToElement(
            (ResponseItem)new ResponseFunctionCallOutputItem
            {
                Id = Guid.NewGuid().ToString("N"),
                CallId = toolResult.CallId,
                Output = toolResult.Output,
            },
            JsonDefaults.Web),
        _ => throw new InvalidOperationException($"Unknown context item type: {item.GetType().Name}"),
    };
}

public abstract record AgentContextItem;

public sealed record UserMessageContextItem(string Text) : AgentContextItem;

public sealed record ResponseOutputContextItem(ResponseItem Item) : AgentContextItem;

public sealed record ToolResultContextItem(string CallId, string Output) : AgentContextItem;

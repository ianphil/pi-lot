using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmSdk.Tests.Fakes;

internal sealed record FauxResponse(
    IReadOnlyList<AssistantStreamEvent> Events,
    TimeSpan? PerEventDelay = null,
    Usage? Usage = null)
{
    public static FauxResponse Text(string text, Usage? usage = null)
    {
        var finalMessage = new AssistantMessage([new TextContent(text)], StopReason.Stop, usage);
        var events = new List<AssistantStreamEvent>
        {
            new StreamStart(string.Empty),
            new TextDelta(text),
        };

        if (usage is not null)
        {
            events.Add(new UsageEvent(usage));
        }

        events.Add(new StreamDone(finalMessage));
        return new FauxResponse(events, Usage: usage);
    }

    public static FauxResponse ToolCall(string name, string argsJson, string id = "call_1")
    {
        var finalMessage = new AssistantMessage([new ToolCallContent(id, name, argsJson)], StopReason.ToolUse);
        return new FauxResponse(
        [
            new StreamStart(string.Empty),
            new ToolCallDelta(id, name, argsJson),
            new StreamDone(finalMessage),
        ]);
    }

    public static FauxResponse Error(string message, AssistantMessage partial)
    {
        ArgumentNullException.ThrowIfNull(partial);
        return new FauxResponse(
        [
            new StreamStart(string.Empty),
            new StreamError(partial, message),
        ]);
    }

    internal AssistantMessage ToAssistantMessage()
    {
        var terminal = Events.LastOrDefault(static e => e is StreamDone or StreamError);
        return terminal switch
        {
            StreamDone done => done.FinalMessage,
            StreamError error => error.PartialMessage with
            {
                StopReason = StopReason.Error,
                ErrorMessage = error.Message,
                Usage = Usage ?? error.PartialMessage.Usage,
            },
            _ => AggregateEvents(),
        };
    }

    private AssistantMessage AggregateEvents()
    {
        var content = new List<ContentBlock>();
        Usage? usage = Usage;
        StopReason stopReason = StopReason.Stop;
        string? errorMessage = null;

        foreach (var streamEvent in Events)
        {
            switch (streamEvent)
            {
                case TextDelta text:
                    content.Add(new TextContent(text.Text));
                    break;
                case ThinkingDelta thinking:
                    content.Add(new ThinkingContent(thinking.Text, Signature: thinking.Signature));
                    break;
                case ToolCallDelta toolCall:
                    content.Add(new ToolCallContent(toolCall.Id, toolCall.Name, toolCall.ArgumentsJsonChunk));
                    stopReason = StopReason.ToolUse;
                    break;
                case UsageEvent usageEvent:
                    usage = usageEvent.Usage;
                    break;
                case StreamError error:
                    stopReason = StopReason.Error;
                    errorMessage = error.Message;
                    break;
            }
        }

        return new AssistantMessage(content, stopReason, usage, errorMessage);
    }
}

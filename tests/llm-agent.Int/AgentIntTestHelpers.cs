using System.Text;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Int;

internal static class AgentIntTestHelpers
{
    public static async IAsyncEnumerable<ResponseStreamEvent> ToAsyncEnumerable(IEnumerable<ResponseStreamEvent> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }

    public static Response CreateResponse(params ResponseItem[] output) => new()
    {
        Id = "resp_agent_int",
        Output = output,
    };

    public static ResponseMessageItem AssistantMessage(string text, string id = "msg_agent_int") => new()
    {
        Id = id,
        Content =
        [
            new ResponseOutputTextPart
            {
                Text = text,
            },
        ],
    };

    public static ResponseFunctionCallItem FunctionCall(
        string name,
        string callId,
        string arguments,
        string id = "fc_agent_int") => new()
    {
        Id = id,
        Name = name,
        CallId = callId,
        Arguments = arguments,
    };

    public static OutputTextDeltaEvent OutputTextDelta(string delta, int sequenceNumber = 0) => new(
        "response.output_text.delta",
        sequenceNumber,
        delta,
        0,
        0,
        "msg_agent_int");

    public static ResponseCompletedEvent Completed(Response response, int sequenceNumber = 0) =>
        new("response.completed", sequenceNumber, response);

    public static async Task<List<AgentEvent>> CollectEventsAsync(IAsyncEnumerable<AgentEvent> source)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in source)
        {
            events.Add(evt);
        }

        return events;
    }

    public static string CollectOutputText(IEnumerable<AgentEvent> events)
    {
        var builder = new StringBuilder();
        foreach (var evt in events)
        {
            if (evt is MessageDelta { StreamEvent: TextDelta delta })
            {
                builder.Append(delta.Text);
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString();
        }

        return events
            .OfType<MessageEnded>()
            .LastOrDefault()
            ?.Response
            .Content
            .OfType<TextContent>()
            .Aggregate(string.Empty, static (text, content) => text + content.Text) ?? string.Empty;
    }
}

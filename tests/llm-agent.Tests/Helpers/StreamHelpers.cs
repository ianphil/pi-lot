using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Helpers;

internal static class StreamHelpers
{
    public static async IAsyncEnumerable<ResponseStreamEvent> ToAsyncEnumerable(IEnumerable<ResponseStreamEvent> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }

    public static IAsyncEnumerable<ResponseStreamEvent> ToAsyncEnumerable(params ResponseStreamEvent[] source)
        => ToAsyncEnumerable(source.AsEnumerable());

    public static Response CreateResponse(params ResponseItem[] output) => new()
    {
        Id = "resp_123",
        Output = output,
    };

    public static ResponseMessageItem AssistantMessage(string text, string id = "msg_123") => new()
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
        string id = "fc_123") => new()
    {
        Id = id,
        Name = name,
        CallId = callId,
        Arguments = arguments,
    };

    public static OutputTextDeltaEvent OutputTextDelta(
        string delta,
        int sequenceNumber = 0,
        int outputIndex = 0,
        int contentIndex = 0,
        string? itemId = "msg_123") => new(
            "response.output_text.delta",
            sequenceNumber,
            delta,
            outputIndex,
            contentIndex,
            itemId);

    public static ResponseCompletedEvent Completed(Response response, int sequenceNumber = 0)
        => new("response.completed", sequenceNumber, response);

    public static ResponseFailedEvent Failed(Response response, int sequenceNumber = 0)
        => new("response.failed", sequenceNumber, response);

    public static ResponseIncompleteEvent Incomplete(Response response, int sequenceNumber = 0)
        => new("response.incomplete", sequenceNumber, response);
}

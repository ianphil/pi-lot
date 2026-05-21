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

    public static Response CreateResponse(params ResponseItem[] output) => CreateResponse(ResponseStatuses.Completed, null, output);

    public static Response CreateResponse(string status, ResponseUsage? usage = null, params ResponseItem[] output) => new()
    {
        Id = "resp_123",
        Status = status,
        Output = output,
        Usage = usage,
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

    public static ResponseReasoningItem Reasoning(string text, string id = "rs_123") => new()
    {
        Id = id,
        Summary =
        [
            new ResponseSummaryTextPart
            {
                Text = text,
            },
        ],
    };

    public static ResponseUsage Usage(int inputTokens = 10, int outputTokens = 5) => new()
    {
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        TotalTokens = inputTokens + outputTokens,
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

    public static FunctionCallArgumentsDeltaEvent FunctionCallArgumentsDelta(
        string delta,
        int sequenceNumber = 0,
        int outputIndex = 0,
        string? itemId = "fc_123") => new(
            "response.function_call_arguments.delta",
            sequenceNumber,
            delta,
            outputIndex,
            itemId);

    public static ReasoningDeltaEvent ReasoningDelta(
        string delta,
        int sequenceNumber = 0,
        int outputIndex = 0,
        int contentIndex = 0,
        string? itemId = "rs_123") => new(
            "response.reasoning.delta",
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

    public static async IAsyncEnumerable<ResponseStreamEvent> ThrowAfterAsync(
        Exception exception,
        params ResponseStreamEvent[] events)
    {
        foreach (var streamEvent in events)
        {
            yield return streamEvent;
            await Task.Yield();
        }

        throw exception;
    }
}

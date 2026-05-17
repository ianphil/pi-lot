using System.Text.Json;
using System.Text.Json.Serialization;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Client;

public abstract record ResponseStreamEvent(string Type, int SequenceNumber)
{
    public static ResponseStreamEvent? Parse(string chunk)
    {
        var parsed = SseChunkParser.Parse(chunk);
        if (parsed is null)
        {
            return null;
        }

        return Parse(parsed.Value);
    }

    internal static ResponseStreamEvent? Parse(ParsedSseChunk parsed)
    {
        if (string.Equals(parsed.Data, "[DONE]", StringComparison.Ordinal))
        {
            return null;
        }

        return parsed.EventName switch
        {
            "response.created" => CreateResponseEvent<ResponseCreatedEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseCreatedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.in_progress" => CreateResponseEvent<ResponseInProgressEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseInProgressEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.completed" => CreateResponseEvent<ResponseCompletedEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseCompletedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.failed" => CreateResponseEvent<ResponseFailedEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseFailedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.incomplete" => CreateResponseEvent<ResponseIncompleteEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseIncompleteEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.output_item.added" => CreateResponseEvent<OutputItemAddedEvent, OutputItemPayload>(parsed.Data,
                static payload => new OutputItemAddedEvent(payload.Type, payload.SequenceNumber, payload.Item, payload.OutputIndex)),
            "response.output_item.done" => CreateResponseEvent<OutputItemDoneEvent, OutputItemPayload>(parsed.Data,
                static payload => new OutputItemDoneEvent(payload.Type, payload.SequenceNumber, payload.Item, payload.OutputIndex)),
            "response.content_part.added" => CreateResponseEvent<ContentPartAddedEvent, ContentPartPayload>(parsed.Data,
                static payload => new ContentPartAddedEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.content_part.done" => CreateResponseEvent<ContentPartDoneEvent, ContentPartPayload>(parsed.Data,
                static payload => new ContentPartDoneEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.output_text.delta" => CreateResponseEvent<OutputTextDeltaEvent, OutputTextDeltaPayload>(parsed.Data,
                static payload => new OutputTextDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.output_text.done" => CreateResponseEvent<OutputTextDoneEvent, OutputTextDonePayload>(parsed.Data,
                static payload => new OutputTextDoneEvent(payload.Type, payload.SequenceNumber, payload.Text, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.function_call_arguments.delta" => CreateResponseEvent<FunctionCallArgumentsDeltaEvent, FunctionCallArgumentsDeltaPayload>(parsed.Data,
                static payload => new FunctionCallArgumentsDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ItemId)),
            "response.function_call_arguments.done" => CreateResponseEvent<FunctionCallArgumentsDoneEvent, FunctionCallArgumentsDonePayload>(parsed.Data,
                static payload => new FunctionCallArgumentsDoneEvent(payload.Type, payload.SequenceNumber, payload.Arguments, payload.OutputIndex, payload.ItemId)),
            "response.queued" => CreateResponseEvent<ResponseQueuedEvent, ResponsePayload>(parsed.Data,
                static payload => new ResponseQueuedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.refusal.delta" => CreateResponseEvent<RefusalDeltaEvent, RefusalDeltaPayload>(parsed.Data,
                static payload => new RefusalDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.refusal.done" => CreateResponseEvent<RefusalDoneEvent, RefusalDonePayload>(parsed.Data,
                static payload => new RefusalDoneEvent(payload.Type, payload.SequenceNumber, payload.Refusal, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.reasoning.delta" => CreateResponseEvent<ReasoningDeltaEvent, ReasoningDeltaPayload>(parsed.Data,
                static payload => new ReasoningDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.reasoning.done" => CreateResponseEvent<ReasoningDoneEvent, ReasoningDonePayload>(parsed.Data,
                static payload => new ReasoningDoneEvent(payload.Type, payload.SequenceNumber, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.reasoning_summary_part.added" => CreateResponseEvent<ReasoningSummaryPartAddedEvent, ReasoningSummaryPartPayload>(parsed.Data,
                static payload => new ReasoningSummaryPartAddedEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.SummaryIndex, payload.ItemId)),
            "response.reasoning_summary_part.done" => CreateResponseEvent<ReasoningSummaryPartDoneEvent, ReasoningSummaryPartPayload>(parsed.Data,
                static payload => new ReasoningSummaryPartDoneEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.SummaryIndex, payload.ItemId)),
            "response.reasoning_summary_text.delta" => CreateResponseEvent<ReasoningSummaryDeltaEvent, ReasoningSummaryDeltaPayload>(parsed.Data,
                static payload => new ReasoningSummaryDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.SummaryIndex, payload.ItemId)),
            "response.reasoning_summary_text.done" => CreateResponseEvent<ReasoningSummaryDoneEvent, ReasoningSummaryDonePayload>(parsed.Data,
                static payload => new ReasoningSummaryDoneEvent(payload.Type, payload.SequenceNumber, payload.Text, payload.OutputIndex, payload.SummaryIndex, payload.ItemId)),
            "response.output_text.annotation.added" => CreateResponseEvent<OutputTextAnnotationAddedEvent, OutputTextAnnotationAddedPayload>(parsed.Data,
                static payload => new OutputTextAnnotationAddedEvent(payload.Type, payload.SequenceNumber, payload.Annotation, payload.OutputIndex, payload.ContentIndex, payload.AnnotationIndex, payload.ItemId)),
            "error" => CreateResponseEvent<ErrorEvent, ErrorPayload>(parsed.Data,
                static payload => new ErrorEvent(payload.Type, payload.SequenceNumber, payload.ToResponseError())),
            _ => CreateUnknownEvent(parsed.EventName ?? string.Empty, parsed.Data),
        };
    }

    private static TEvent CreateResponseEvent<TEvent, TPayload>(string data, Func<TPayload, TEvent> factory)
        where TEvent : ResponseStreamEvent
        where TPayload : class
    {
        var payload = JsonSerializer.Deserialize<TPayload>(data, JsonDefaults.Web)
                      ?? throw new InvalidOperationException($"The stream event payload could not be deserialized into {typeof(TPayload).Name}.");
        return factory(payload);
    }

    private static UnknownStreamEvent CreateUnknownEvent(string eventName, string data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        var sequenceNumber = root.TryGetProperty("sequence_number", out var sn) ? sn.GetInt32() : 0;
        return new UnknownStreamEvent(eventName, sequenceNumber, data);
    }

    private sealed class ResponsePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("response")]
        public required Response Response { get; init; }
    }

    private sealed class OutputItemPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("item")]
        public required ResponseItem Item { get; init; }

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }
    }

    private sealed class ContentPartPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("part")]
        public required ResponseContentPart Part { get; init; }

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class OutputTextDeltaPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("delta")]
        public string Delta { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class OutputTextDonePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class FunctionCallArgumentsDeltaPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("delta")]
        public string Delta { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class FunctionCallArgumentsDonePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class RefusalDeltaPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("delta")]
        public string Delta { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class RefusalDonePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("refusal")]
        public string Refusal { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ReasoningDeltaPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("delta")]
        public string Delta { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ReasoningDonePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ReasoningSummaryPartPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("part")]
        public JsonElement Part { get; init; }

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("summary_index")]
        public int SummaryIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ReasoningSummaryDeltaPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("delta")]
        public string Delta { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("summary_index")]
        public int SummaryIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ReasoningSummaryDonePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("summary_index")]
        public int SummaryIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class OutputTextAnnotationAddedPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("annotation")]
        public JsonElement Annotation { get; init; }

        [JsonPropertyName("output_index")]
        public int OutputIndex { get; init; }

        [JsonPropertyName("content_index")]
        public int ContentIndex { get; init; }

        [JsonPropertyName("annotation_index")]
        public int AnnotationIndex { get; init; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; init; }
    }

    private sealed class ErrorPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "error";

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("error")]
        public ErrorPayloadBody? Error { get; init; }

        [JsonPropertyName("message")]
        public string? FlatMessage { get; init; }

        [JsonPropertyName("code")]
        public string? FlatCode { get; init; }

        [JsonPropertyName("param")]
        public string? FlatParam { get; init; }

        public ResponseError ToResponseError() => new()
        {
            Message = Error?.Message ?? FlatMessage ?? string.Empty,
            Type = Error?.Type ?? Type,
            Code = Error?.Code ?? FlatCode,
            Param = Error?.Param ?? FlatParam,
        };
    }

    private sealed class ErrorPayloadBody
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("param")]
        public string? Param { get; init; }
    }
}

public abstract record ResponseEvent(string Type, int SequenceNumber, Response Response)
    : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ResponseCreatedEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public sealed record ResponseInProgressEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public sealed record ResponseCompletedEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public sealed record ResponseFailedEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public sealed record ResponseIncompleteEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public abstract record OutputItemEvent(string Type, int SequenceNumber, ResponseItem Item, int OutputIndex)
    : ResponseStreamEvent(Type, SequenceNumber);

public sealed record OutputItemAddedEvent(string Type, int SequenceNumber, ResponseItem Item, int OutputIndex)
    : OutputItemEvent(Type, SequenceNumber, Item, OutputIndex);

public sealed record OutputItemDoneEvent(string Type, int SequenceNumber, ResponseItem Item, int OutputIndex)
    : OutputItemEvent(Type, SequenceNumber, Item, OutputIndex);

public abstract record ContentPartEvent(
    string Type,
    int SequenceNumber,
    ResponseContentPart Part,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ContentPartAddedEvent(
    string Type,
    int SequenceNumber,
    ResponseContentPart Part,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ContentPartEvent(Type, SequenceNumber, Part, OutputIndex, ContentIndex, ItemId);

public sealed record ContentPartDoneEvent(
    string Type,
    int SequenceNumber,
    ResponseContentPart Part,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ContentPartEvent(Type, SequenceNumber, Part, OutputIndex, ContentIndex, ItemId);

public sealed record OutputTextDeltaEvent(
    string Type,
    int SequenceNumber,
    string Delta,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record OutputTextDoneEvent(
    string Type,
    int SequenceNumber,
    string Text,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record FunctionCallArgumentsDeltaEvent(
    string Type,
    int SequenceNumber,
    string Delta,
    int OutputIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record FunctionCallArgumentsDoneEvent(
    string Type,
    int SequenceNumber,
    string Arguments,
    int OutputIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ErrorEvent(string Type, int SequenceNumber, ResponseError Error)
    : ResponseStreamEvent(Type, SequenceNumber);

public sealed record UnknownStreamEvent(string EventName, int SequenceNumber, string RawData)
    : ResponseStreamEvent(EventName, SequenceNumber);

public sealed record ResponseQueuedEvent(string Type, int SequenceNumber, Response Response)
    : ResponseEvent(Type, SequenceNumber, Response);

public sealed record RefusalDeltaEvent(
    string Type,
    int SequenceNumber,
    string Delta,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record RefusalDoneEvent(
    string Type,
    int SequenceNumber,
    string Refusal,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningDeltaEvent(
    string Type,
    int SequenceNumber,
    string Delta,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningDoneEvent(
    string Type,
    int SequenceNumber,
    int OutputIndex,
    int ContentIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningSummaryPartAddedEvent(
    string Type,
    int SequenceNumber,
    JsonElement Part,
    int OutputIndex,
    int SummaryIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningSummaryPartDoneEvent(
    string Type,
    int SequenceNumber,
    JsonElement Part,
    int OutputIndex,
    int SummaryIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningSummaryDeltaEvent(
    string Type,
    int SequenceNumber,
    string Delta,
    int OutputIndex,
    int SummaryIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record ReasoningSummaryDoneEvent(
    string Type,
    int SequenceNumber,
    string Text,
    int OutputIndex,
    int SummaryIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

public sealed record OutputTextAnnotationAddedEvent(
    string Type,
    int SequenceNumber,
    JsonElement Annotation,
    int OutputIndex,
    int ContentIndex,
    int AnnotationIndex,
    string? ItemId) : ResponseStreamEvent(Type, SequenceNumber);

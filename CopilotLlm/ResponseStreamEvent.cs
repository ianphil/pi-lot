using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Services;

namespace CopilotLlm.Client;

public abstract record ResponseStreamEvent(string Type, int SequenceNumber)
{
    public static ResponseStreamEvent? Parse(string chunk)
    {
        var parsed = SseChunkParser.Parse(chunk);
        if (parsed is null)
        {
            return null;
        }

        if (string.Equals(parsed.Value.Data, "[DONE]", StringComparison.Ordinal))
        {
            return null;
        }

        return parsed.Value.EventName switch
        {
            "response.created" => CreateResponseEvent<ResponseCreatedEvent, ResponsePayload>(parsed.Value.Data,
                static payload => new ResponseCreatedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.in_progress" => CreateResponseEvent<ResponseInProgressEvent, ResponsePayload>(parsed.Value.Data,
                static payload => new ResponseInProgressEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.completed" => CreateResponseEvent<ResponseCompletedEvent, ResponsePayload>(parsed.Value.Data,
                static payload => new ResponseCompletedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.failed" => CreateResponseEvent<ResponseFailedEvent, ResponsePayload>(parsed.Value.Data,
                static payload => new ResponseFailedEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.incomplete" => CreateResponseEvent<ResponseIncompleteEvent, ResponsePayload>(parsed.Value.Data,
                static payload => new ResponseIncompleteEvent(payload.Type, payload.SequenceNumber, payload.Response)),
            "response.output_item.added" => CreateResponseEvent<OutputItemAddedEvent, OutputItemPayload>(parsed.Value.Data,
                static payload => new OutputItemAddedEvent(payload.Type, payload.SequenceNumber, payload.Item, payload.OutputIndex)),
            "response.output_item.done" => CreateResponseEvent<OutputItemDoneEvent, OutputItemPayload>(parsed.Value.Data,
                static payload => new OutputItemDoneEvent(payload.Type, payload.SequenceNumber, payload.Item, payload.OutputIndex)),
            "response.content_part.added" => CreateResponseEvent<ContentPartAddedEvent, ContentPartPayload>(parsed.Value.Data,
                static payload => new ContentPartAddedEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.content_part.done" => CreateResponseEvent<ContentPartDoneEvent, ContentPartPayload>(parsed.Value.Data,
                static payload => new ContentPartDoneEvent(payload.Type, payload.SequenceNumber, payload.Part, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.output_text.delta" => CreateResponseEvent<OutputTextDeltaEvent, OutputTextDeltaPayload>(parsed.Value.Data,
                static payload => new OutputTextDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.output_text.done" => CreateResponseEvent<OutputTextDoneEvent, OutputTextDonePayload>(parsed.Value.Data,
                static payload => new OutputTextDoneEvent(payload.Type, payload.SequenceNumber, payload.Text, payload.OutputIndex, payload.ContentIndex, payload.ItemId)),
            "response.function_call_arguments.delta" => CreateResponseEvent<FunctionCallArgumentsDeltaEvent, FunctionCallArgumentsDeltaPayload>(parsed.Value.Data,
                static payload => new FunctionCallArgumentsDeltaEvent(payload.Type, payload.SequenceNumber, payload.Delta, payload.OutputIndex, payload.ItemId)),
            "response.function_call_arguments.done" => CreateResponseEvent<FunctionCallArgumentsDoneEvent, FunctionCallArgumentsDonePayload>(parsed.Value.Data,
                static payload => new FunctionCallArgumentsDoneEvent(payload.Type, payload.SequenceNumber, payload.Arguments, payload.OutputIndex, payload.ItemId)),
            "error" => CreateResponseEvent<ErrorEvent, ErrorPayload>(parsed.Value.Data,
                static payload => new ErrorEvent(payload.Type, payload.SequenceNumber, payload.ToResponseError())),
            _ => throw new InvalidOperationException($"Unsupported response stream event '{parsed.Value.EventName}'."),
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

    private sealed class ErrorPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "error";

        [JsonPropertyName("sequence_number")]
        public int SequenceNumber { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("param")]
        public string? Param { get; init; }

        public ResponseError ToResponseError() => new()
        {
            Message = Message,
            Type = Type,
            Code = Code,
            Param = Param,
        };
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

using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ResponseStreamEventTests
{
    [Fact]
    public void Parse_WhenChunkIsOutputTextDelta_ReturnsOutputTextDelta()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.output_text.delta", new
        {
            type = "response.output_text.delta",
            sequence_number = 2,
            item_id = "msg_123",
            output_index = 0,
            content_index = 1,
            delta = "Hello",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var delta = Assert.IsType<OutputTextDeltaEvent>(streamEvent);
        Assert.Equal(2, delta.SequenceNumber);
        Assert.Equal("msg_123", delta.ItemId);
        Assert.Equal(0, delta.OutputIndex);
        Assert.Equal(1, delta.ContentIndex);
        Assert.Equal("Hello", delta.Delta);
    }

    [Fact]
    public void Parse_WhenChunkIsResponseCompleted_ReturnsResponseCompleted()
    {
        var response = CreateResponse("resp_123", ResponseStatuses.Completed, "Hello");
        var chunk = ResponseSseSerializer.SerializeEvent("response.completed", new
        {
            type = "response.completed",
            sequence_number = 4,
            response,
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var completed = Assert.IsType<ResponseCompletedEvent>(streamEvent);
        Assert.Equal(4, completed.SequenceNumber);
        Assert.Equal(response.Id, completed.Response.Id);
        Assert.Equal(ResponseStatuses.Completed, completed.Response.Status);
    }

    [Fact]
    public void Parse_WhenChunkIsFunctionCallArgumentsDelta_ReturnsFunctionCallArgumentsDelta()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.function_call_arguments.delta", new
        {
            type = "response.function_call_arguments.delta",
            sequence_number = 6,
            item_id = "fc_123",
            output_index = 1,
            delta = "{\"url\":\"https://example.com\"}",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var delta = Assert.IsType<FunctionCallArgumentsDeltaEvent>(streamEvent);
        Assert.Equal(6, delta.SequenceNumber);
        Assert.Equal("fc_123", delta.ItemId);
        Assert.Equal(1, delta.OutputIndex);
        Assert.Contains("example.com", delta.Delta);
    }

    [Fact]
    public void Parse_WhenChunkIsDoneSentinel_ReturnsNull()
    {
        var streamEvent = ResponseStreamEvent.Parse(ResponseSseSerializer.SerializeDone());

        Assert.Null(streamEvent);
    }

    [Fact]
    public void Parse_WhenChunkIsUnrecognizedEvent_ReturnsUnknownStreamEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.some_future_event", new
        {
            type = "response.some_future_event",
            sequence_number = 99,
            custom_data = "hello",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var unknown = Assert.IsType<UnknownStreamEvent>(streamEvent);
        Assert.Equal("response.some_future_event", unknown.EventName);
        Assert.Equal(99, unknown.SequenceNumber);
        Assert.Contains("hello", unknown.RawData);
    }

    [Fact]
    public void Parse_WhenChunkIsResponseQueued_ReturnsResponseQueuedEvent()
    {
        var response = CreateResponse("resp_queued", ResponseStatuses.InProgress, "");
        var chunk = ResponseSseSerializer.SerializeEvent("response.queued", new
        {
            type = "response.queued",
            sequence_number = 0,
            response,
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var queued = Assert.IsType<ResponseQueuedEvent>(streamEvent);
        Assert.Equal(0, queued.SequenceNumber);
        Assert.Equal("resp_queued", queued.Response.Id);
    }

    [Fact]
    public void Parse_WhenChunkIsRefusalDelta_ReturnsRefusalDeltaEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.refusal.delta", new
        {
            type = "response.refusal.delta",
            sequence_number = 3,
            item_id = "msg_456",
            output_index = 0,
            content_index = 0,
            delta = "I cannot",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var delta = Assert.IsType<RefusalDeltaEvent>(streamEvent);
        Assert.Equal(3, delta.SequenceNumber);
        Assert.Equal("I cannot", delta.Delta);
        Assert.Equal("msg_456", delta.ItemId);
    }

    [Fact]
    public void Parse_WhenChunkIsRefusalDone_ReturnsRefusalDoneEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.refusal.done", new
        {
            type = "response.refusal.done",
            sequence_number = 4,
            item_id = "msg_456",
            output_index = 0,
            content_index = 0,
            refusal = "I cannot help with that.",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var done = Assert.IsType<RefusalDoneEvent>(streamEvent);
        Assert.Equal("I cannot help with that.", done.Refusal);
    }

    [Fact]
    public void Parse_WhenChunkIsReasoningDelta_ReturnsReasoningDeltaEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.reasoning.delta", new
        {
            type = "response.reasoning.delta",
            sequence_number = 2,
            item_id = "rs_001",
            output_index = 0,
            content_index = 0,
            delta = "Let me think",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var delta = Assert.IsType<ReasoningDeltaEvent>(streamEvent);
        Assert.Equal("Let me think", delta.Delta);
        Assert.Equal("rs_001", delta.ItemId);
    }

    [Fact]
    public void Parse_WhenChunkIsReasoningDone_ReturnsReasoningDoneEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.reasoning.done", new
        {
            type = "response.reasoning.done",
            sequence_number = 3,
            item_id = "rs_001",
            output_index = 0,
            content_index = 0,
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var done = Assert.IsType<ReasoningDoneEvent>(streamEvent);
        Assert.Equal("rs_001", done.ItemId);
    }

    [Fact]
    public void Parse_WhenChunkIsReasoningSummaryDelta_ReturnsReasoningSummaryDeltaEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.reasoning_summary_text.delta", new
        {
            type = "response.reasoning_summary_text.delta",
            sequence_number = 5,
            item_id = "rs_001",
            output_index = 0,
            summary_index = 0,
            delta = "Summary chunk",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var delta = Assert.IsType<ReasoningSummaryDeltaEvent>(streamEvent);
        Assert.Equal("Summary chunk", delta.Delta);
        Assert.Equal(0, delta.SummaryIndex);
    }

    [Fact]
    public void Parse_WhenChunkIsReasoningSummaryDone_ReturnsReasoningSummaryDoneEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.reasoning_summary_text.done", new
        {
            type = "response.reasoning_summary_text.done",
            sequence_number = 6,
            item_id = "rs_001",
            output_index = 0,
            summary_index = 0,
            text = "Full summary text",
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var done = Assert.IsType<ReasoningSummaryDoneEvent>(streamEvent);
        Assert.Equal("Full summary text", done.Text);
    }

    [Fact]
    public void Parse_WhenChunkIsOutputTextAnnotationAdded_ReturnsOutputTextAnnotationAddedEvent()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("response.output_text.annotation.added", new
        {
            type = "response.output_text.annotation.added",
            sequence_number = 7,
            item_id = "msg_123",
            output_index = 0,
            content_index = 0,
            annotation_index = 0,
            annotation = new
            {
                type = "url_citation",
                url = "https://example.com",
                title = "Example",
            },
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var annotation = Assert.IsType<OutputTextAnnotationAddedEvent>(streamEvent);
        Assert.Equal(0, annotation.AnnotationIndex);
        Assert.Equal("msg_123", annotation.ItemId);
    }

    [Fact]
    public void Parse_WhenChunkIsErrorWithNestedPayload_ReturnsErrorEventWithCorrectFields()
    {
        var chunk = ResponseSseSerializer.SerializeEvent("error", new
        {
            type = "error",
            sequence_number = 1,
            error = new
            {
                message = "Rate limit exceeded",
                type = "rate_limit_error",
                code = "rate_limit",
                param = (string?)null,
            },
        });

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var errorEvent = Assert.IsType<ErrorEvent>(streamEvent);
        Assert.Equal("Rate limit exceeded", errorEvent.Error.Message);
        Assert.Equal("rate_limit_error", errorEvent.Error.Type);
        Assert.Equal("rate_limit", errorEvent.Error.Code);
    }

    [Theory]
    [MemberData(nameof(GetLifecycleChunks))]
    public void Parse_WhenChunkIsLifecycleEvent_ReturnsExpectedEvent(string chunk, Type expectedType, string expectedStatus)
    {
        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var lifecycle = Assert.IsAssignableFrom<ResponseEvent>(streamEvent);
        Assert.IsType(expectedType, lifecycle);
        Assert.Equal(expectedStatus, lifecycle.Response.Status);
    }

    [Theory]
    [MemberData(nameof(GetContentPartChunks))]
    public void Parse_WhenChunkIsContentPartEvent_ReturnsExpectedEvent(string chunk, Type expectedType, string expectedText)
    {
        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var contentPart = Assert.IsAssignableFrom<ContentPartEvent>(streamEvent);
        Assert.IsType(expectedType, contentPart);
        var part = Assert.IsType<ResponseOutputTextPart>(contentPart.Part);
        Assert.Equal(expectedText, part.Text);
    }

    [Theory]
    [MemberData(nameof(GetOutputItemChunks))]
    public void Parse_WhenChunkIsOutputItemEvent_ReturnsExpectedEvent(string chunk, Type expectedType)
    {
        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var outputItem = Assert.IsAssignableFrom<OutputItemEvent>(streamEvent);
        Assert.IsType(expectedType, outputItem);
        var item = Assert.IsType<ResponseMessageItem>(outputItem.Item);
        Assert.Equal("message_123", item.Id);
        Assert.Equal("assistant", item.Role);
    }

    [Fact]
    public void Parse_WhenOutputItemTypeDiscriminatorAppearsLast_ReturnsOutputItemEvent()
    {
        const string chunk = """
event: response.output_item.added
data: {"item":{"content":[],"id":"message_123","role":"assistant","status":"in_progress","type":"message"},"output_index":0,"sequence_number":2,"type":"response.output_item.added"}

""";

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var outputItem = Assert.IsType<OutputItemAddedEvent>(streamEvent);
        var item = Assert.IsType<ResponseMessageItem>(outputItem.Item);
        Assert.Equal("message_123", item.Id);
        Assert.Equal("assistant", item.Role);
    }

    [Fact]
    public void Parse_WhenContentPartTypeDiscriminatorAppearsLast_ReturnsContentPartEvent()
    {
        const string chunk = """
event: response.content_part.added
data: {"content_index":0,"item_id":"message_123","output_index":0,"part":{"annotations":[],"text":"hello","type":"output_text"},"sequence_number":3,"type":"response.content_part.added"}

""";

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var contentPart = Assert.IsType<ContentPartAddedEvent>(streamEvent);
        var part = Assert.IsType<ResponseOutputTextPart>(contentPart.Part);
        Assert.Equal("hello", part.Text);
    }

    [Fact]
    public void Parse_WhenResponseOutputItemTypeDiscriminatorAppearsLast_ReturnsLifecycleEvent()
    {
        const string chunk = """
event: response.completed
data: {"response":{"id":"resp_123","status":"completed","model":"gpt-5.4-mini","output":[{"content":[{"annotations":[],"text":"hello","type":"output_text"}],"id":"message_123","role":"assistant","status":"completed","type":"message"}],"temperature":1,"top_p":1,"tools":[],"tool_choice":"auto","truncation":"disabled","parallel_tool_calls":true,"text":{"format":{"type":"text"}},"presence_penalty":0,"frequency_penalty":0,"top_logprobs":0,"store":false,"background":false,"service_tier":"default","metadata":null,"completed_at":null,"incomplete_details":null,"previous_response_id":null,"instructions":null,"error":null,"reasoning":null,"usage":null,"max_output_tokens":null,"max_tool_calls":null,"safety_identifier":null,"prompt_cache_key":null},"sequence_number":4,"type":"response.completed"}

""";

        var streamEvent = ResponseStreamEvent.Parse(chunk);

        var completed = Assert.IsType<ResponseCompletedEvent>(streamEvent);
        var item = Assert.IsType<ResponseMessageItem>(Assert.Single(completed.Response.Output));
        var part = Assert.IsType<ResponseOutputTextPart>(Assert.Single(item.Content));
        Assert.Equal("hello", part.Text);
    }

    public static TheoryData<string, Type, string> GetLifecycleChunks()
    {
        return new TheoryData<string, Type, string>
        {
            {
                CreateLifecycleChunk("response.created", 0, ResponseStatuses.InProgress),
                typeof(ResponseCreatedEvent),
                ResponseStatuses.InProgress
            },
            {
                CreateLifecycleChunk("response.in_progress", 1, ResponseStatuses.InProgress),
                typeof(ResponseInProgressEvent),
                ResponseStatuses.InProgress
            },
            {
                CreateLifecycleChunk("response.failed", 2, ResponseStatuses.Failed),
                typeof(ResponseFailedEvent),
                ResponseStatuses.Failed
            },
            {
                CreateLifecycleChunk("response.incomplete", 3, ResponseStatuses.Incomplete),
                typeof(ResponseIncompleteEvent),
                ResponseStatuses.Incomplete
            },
            {
                CreateLifecycleChunk("response.queued", 4, ResponseStatuses.InProgress),
                typeof(ResponseQueuedEvent),
                ResponseStatuses.InProgress
            },
        };
    }

    public static TheoryData<string, Type, string> GetContentPartChunks()
    {
        return new TheoryData<string, Type, string>
        {
            {
                ResponseSseSerializer.SerializeEvent("response.content_part.added", new
                {
                    type = "response.content_part.added",
                    sequence_number = 3,
                    item_id = "message_123",
                    output_index = 0,
                    content_index = 0,
                    part = new
                    {
                        type = "output_text",
                        annotations = Array.Empty<object>(),
                        text = string.Empty,
                    },
                }),
                typeof(ContentPartAddedEvent),
                string.Empty
            },
            {
                ResponseSseSerializer.SerializeEvent("response.content_part.done", new
                {
                    type = "response.content_part.done",
                    sequence_number = 5,
                    item_id = "message_123",
                    output_index = 0,
                    content_index = 0,
                    part = new
                    {
                        type = "output_text",
                        annotations = Array.Empty<object>(),
                        text = "Hello",
                    },
                }),
                typeof(ContentPartDoneEvent),
                "Hello"
            },
        };
    }

    public static TheoryData<string, Type> GetOutputItemChunks()
    {
        ResponseItem item = new ResponseMessageItem
        {
            Id = "message_123",
            Role = "assistant",
            Content =
            [
                new ResponseOutputTextPart
                {
                    Text = "Hello",
                },
            ],
        };

        return new TheoryData<string, Type>
        {
            {
                ResponseSseSerializer.SerializeEvent("response.output_item.added", new
                {
                    type = "response.output_item.added",
                    sequence_number = 2,
                    output_index = 0,
                    item,
                }),
                typeof(OutputItemAddedEvent)
            },
            {
                ResponseSseSerializer.SerializeEvent("response.output_item.done", new
                {
                    type = "response.output_item.done",
                    sequence_number = 7,
                    output_index = 0,
                    item,
                }),
                typeof(OutputItemDoneEvent)
            },
        };
    }

    private static string CreateLifecycleChunk(string eventName, int sequenceNumber, string status)
    {
        var response = CreateResponse($"resp_{sequenceNumber}", status, "Hello");
        return ResponseSseSerializer.SerializeEvent(eventName, new
        {
            type = eventName,
            sequence_number = sequenceNumber,
            response,
        });
    }

    private static Response CreateResponse(string id, string status, string text)
    {
        return new Response
        {
            Id = id,
            Status = status,
            Model = "gpt-5.4-mini",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "message_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = text,
                        },
                    ],
                },
            ],
        };
    }
}

using System.Text.Json;
using CopilotLlm.Client;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Services;

namespace CopilotLlm.Tests.Unit;

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

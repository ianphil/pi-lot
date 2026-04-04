using System.Text;
using System.Text.Json;
using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Services;

public static class ResponseSseSerializer
{
    public static string Serialize(Response response)
    {
        var builder = new StringBuilder();
        var sequence = 0;

        AppendEvent(builder, "response.created", new
        {
            type = "response.created",
            sequence_number = sequence++,
            response = CreateSnapshot(response, ResponseStatuses.InProgress),
        });

        AppendEvent(builder, "response.in_progress", new
        {
            type = "response.in_progress",
            sequence_number = sequence++,
            response = CreateSnapshot(response, ResponseStatuses.InProgress),
        });

        for (var outputIndex = 0; outputIndex < response.Output.Length; outputIndex++)
        {
            var item = response.Output[outputIndex];
            AppendEvent(builder, "response.output_item.added", new
            {
                type = "response.output_item.added",
                sequence_number = sequence++,
                output_index = outputIndex,
                item = item,
            });

            switch (item)
            {
                case ResponseMessageItem message:
                    WriteMessageItem(builder, message, outputIndex, ref sequence);
                    break;

                case ResponseFunctionCallItem functionCall:
                    WriteFunctionCallItem(builder, functionCall, outputIndex, ref sequence);
                    break;
            }

            AppendEvent(builder, "response.output_item.done", new
            {
                type = "response.output_item.done",
                sequence_number = sequence++,
                output_index = outputIndex,
                item = item,
            });
        }

        var terminalEventName = GetTerminalEventName(response.Status);
        AppendEvent(builder, terminalEventName, new
        {
            type = terminalEventName,
            sequence_number = sequence++,
            response = response,
        });

        builder.Append(SerializeDone());
        return builder.ToString();
    }

    public static string SerializeEvent(string eventName, object payload) =>
        SerializeChunk(eventName, JsonSerializer.Serialize(payload, JsonDefaults.Web));

    public static string SerializeChunk(string? eventName, string data)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            builder.Append("event: ").Append(eventName).Append('\n');
        }

        builder.Append("data: ").Append(data).Append('\n').Append('\n');
        return builder.ToString();
    }

    public static string SerializeDone() => SerializeChunk(null, "[DONE]");

    internal static string GetTerminalEventName(string status) =>
        status switch
        {
            ResponseStatuses.Failed => "response.failed",
            ResponseStatuses.Incomplete => "response.incomplete",
            _ => "response.completed",
        };

    private static void WriteMessageItem(StringBuilder builder, ResponseMessageItem message, int outputIndex, ref int sequence)
    {
        for (var contentIndex = 0; contentIndex < message.Content.Length; contentIndex++)
        {
            if (message.Content[contentIndex] is not ResponseOutputTextPart outputText)
            {
                continue;
            }

            AppendEvent(builder, "response.content_part.added", new
            {
                type = "response.content_part.added",
                sequence_number = sequence++,
                item_id = message.Id,
                output_index = outputIndex,
                content_index = contentIndex,
                part = new
                {
                    type = "output_text",
                    annotations = Array.Empty<object>(),
                    text = string.Empty,
                },
            });

            if (!string.IsNullOrEmpty(outputText.Text))
            {
                AppendEvent(builder, "response.output_text.delta", new
                {
                    type = "response.output_text.delta",
                    sequence_number = sequence++,
                    item_id = message.Id,
                    output_index = outputIndex,
                    content_index = contentIndex,
                    delta = outputText.Text,
                });
            }

            AppendEvent(builder, "response.output_text.done", new
            {
                type = "response.output_text.done",
                sequence_number = sequence++,
                item_id = message.Id,
                output_index = outputIndex,
                content_index = contentIndex,
                text = outputText.Text,
            });

            AppendEvent(builder, "response.content_part.done", new
            {
                type = "response.content_part.done",
                sequence_number = sequence++,
                item_id = message.Id,
                output_index = outputIndex,
                content_index = contentIndex,
                part = new
                {
                    type = "output_text",
                    annotations = outputText.Annotations,
                    text = outputText.Text,
                },
            });
        }
    }

    private static void WriteFunctionCallItem(StringBuilder builder, ResponseFunctionCallItem functionCall, int outputIndex, ref int sequence)
    {
        AppendEvent(builder, "response.function_call_arguments.delta", new
        {
            type = "response.function_call_arguments.delta",
            sequence_number = sequence++,
            item_id = functionCall.Id,
            output_index = outputIndex,
            delta = functionCall.Arguments,
        });

        AppendEvent(builder, "response.function_call_arguments.done", new
        {
            type = "response.function_call_arguments.done",
            sequence_number = sequence++,
            item_id = functionCall.Id,
            output_index = outputIndex,
            arguments = functionCall.Arguments,
        });
    }

    private static Response CreateSnapshot(Response response, string status) => new()
    {
        Id = response.Id,
        Object = response.Object,
        CreatedAt = response.CreatedAt,
        Status = status,
        Model = response.Model,
        Output = response.Output,
        CompletedAt = response.CompletedAt,
        Usage = response.Usage,
        Error = response.Error,
        IncompleteDetails = response.IncompleteDetails,
        Temperature = response.Temperature,
        TopP = response.TopP,
        MaxOutputTokens = response.MaxOutputTokens,
        Tools = response.Tools,
        ToolChoice = response.ToolChoice,
        PreviousResponseId = response.PreviousResponseId,
        Instructions = response.Instructions,
        Truncation = response.Truncation,
        ParallelToolCalls = response.ParallelToolCalls,
        Text = response.Text,
        PresencePenalty = response.PresencePenalty,
        FrequencyPenalty = response.FrequencyPenalty,
        TopLogprobs = response.TopLogprobs,
        Store = response.Store,
        Background = response.Background,
        ServiceTier = response.ServiceTier,
        Metadata = response.Metadata,
        MaxToolCalls = response.MaxToolCalls,
        Reasoning = response.Reasoning,
        SafetyIdentifier = response.SafetyIdentifier,
        PromptCacheKey = response.PromptCacheKey,
    };

    private static void AppendEvent(StringBuilder builder, string eventName, object payload)
    {
        builder.Append("event: ").Append(eventName).Append('\n');
        builder.Append("data: ").Append(JsonSerializer.Serialize(payload, JsonDefaults.Web)).Append('\n');
        builder.Append('\n');
    }
}

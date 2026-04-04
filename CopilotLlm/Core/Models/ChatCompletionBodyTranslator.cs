using System.Text.Json;
using CopilotLlm.Core.Models;

namespace CopilotLlm.Core.Models;

public static class ChatCompletionBodyTranslator
{
    public static string TranslateResponseBodyToChatCompletion(string responsesBody)
    {
        var resp = JsonSerializer.Deserialize<ResponsesApiResponse>(responsesBody, JsonDefaults.Web);
        if (resp is null)
        {
            return responsesBody;
        }

        var textOutput = resp.Output?.FirstOrDefault(output => output.Type == "message");
        var text = textOutput?.Content?.FirstOrDefault(content => content.Type == "output_text")?.Text;
        var toolCalls = resp.Output?
            .Where(output => output.Type == "function_call")
            .Select(output => new ChatToolCall
            {
                Id = output.CallId ?? output.Id,
                Function = new ChatToolCallFunction
                {
                    Name = output.Name,
                    Arguments = output.Arguments,
                },
            })
            .ToArray();

        var translated = new ChatCompletionResponse
        {
            Id = resp.Id,
            Object = "chat.completion",
            Model = resp.Model,
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = text,
                        ToolCalls = toolCalls is { Length: > 0 } ? toolCalls : null,
                    },
                    FinishReason = resp.Status == ResponseStatuses.Incomplete ? "length" : "stop",
                },
            ],
            Usage = new UsageInfo
            {
                PromptTokens = resp.Usage?.InputTokens ?? 0,
                CompletionTokens = resp.Usage?.OutputTokens ?? 0,
                TotalTokens = (resp.Usage?.InputTokens ?? 0) + (resp.Usage?.OutputTokens ?? 0),
            },
        };

        return JsonSerializer.Serialize(translated, JsonDefaults.Web);
    }

    internal static object? NormalizeMessageContent(object? content) => content switch
    {
        null => null,
        JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<object>(item.GetRawText(), JsonDefaults.Web)
                : item.ToString())
            .ToArray(),
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
        JsonElement element => JsonSerializer.Deserialize<object>(element.GetRawText(), JsonDefaults.Web),
        _ => content,
    };
}

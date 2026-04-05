using System.Text.Json;
using LlmSdk.Core.Models;

namespace LlmSdk.Client;

public static class ChatCompletionExtensions
{
    public static string? GetMessageText(this ChatCompletionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = response.Choices is { Length: > 0 }
            ? response.Choices[0].Message?.Content
            : null;

        return content switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
    }
}

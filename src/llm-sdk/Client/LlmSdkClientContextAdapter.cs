using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Client;

internal static class LlmSdkClientContextAdapter
{
    public static async Task<AssistantMessage> CompleteAsync(
        ILlmSdkClient client,
        Context context,
        CompletionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);

        if (options?.PreferredApi == CompletionApi.ChatCompletions)
        {
            var chatResponse = await client.CreateChatCompletionAsync(
                ContextTranslator.ToChatCompletionRequest(context, options),
                cancellationToken);
            return ContextTranslator.ToAssistantMessage(chatResponse);
        }

        var response = await client.CreateResponseAsync(
            ContextTranslator.ToCreateResponseRequest(context, options),
            cancellationToken);
        return ContextTranslator.ToAssistantMessage(response);
    }
}

using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli;

public static class SdkChatAgent
{
    public static async Task RunNonStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        var response = await client.CreateChatCompletionAsync(CreateRequest(request), cancellationToken);
        var text = response.GetMessageText();
        writer.WriteLine(text is null ? "No message text was returned." : text);
    }

    public static async Task RunStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        await foreach (var chunk in client.CreateChatCompletionStreamAsync(CreateRequest(request, stream: true), cancellationToken))
        {
            if (chunk.Choices is not { Length: > 0 })
            {
                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                if (choice.Delta?.Content is { } content)
                {
                    writer.Write(content);
                }
            }
        }

        writer.WriteLine();
    }

    private static ChatCompletionRequest CreateRequest(AskRequest request, bool? stream = null)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = request.SystemInstructions,
            });
        }

        messages.Add(new ChatMessage
        {
            Role = "user",
            Content = request.Prompt,
        });

        return new ChatCompletionRequest
        {
            Model = request.Model,
            Messages = [.. messages],
            Stream = stream,
        };
    }
}

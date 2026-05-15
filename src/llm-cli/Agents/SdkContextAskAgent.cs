using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Agents;

public static class SdkContextAskAgent
{
    public static async Task RunNonStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        CompletionApi preferredApi,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        var response = await client.CompleteAsync(CreateContext(request), new CompletionOptions
        {
            Model = request.Model,
            PreferredApi = preferredApi,
        }, cancellationToken);

        var text = string.Concat(response.Content.OfType<TextContent>().Select(static content => content.Text));
        writer.WriteLine(string.IsNullOrEmpty(text) ? "No output text was returned." : text);

        if (response.StopReason is not StopReason.Stop)
        {
            writer.WriteLine($"Stop reason: {response.StopReason}");
        }
    }

    public static async Task RunStreamingAsync(
        ILlmSdkClient client,
        AskRequest request,
        CompletionApi preferredApi,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        var wroteText = false;
        await foreach (var streamEvent in client.StreamAsync(CreateContext(request), new CompletionOptions
                       {
                           Model = request.Model,
                           PreferredApi = preferredApi,
                       }, cancellationToken))
        {
            switch (streamEvent)
            {
                case TextDelta delta:
                    writer.Write(delta.Text);
                    wroteText = true;
                    break;
                case StreamDone done:
                    WriteFinalMessage(writer, done.FinalMessage, wroteText);
                    return;
                case StreamError error:
                    WriteStatusLine(writer, wroteText, $"Stream error: {error.Message}");
                    return;
            }
        }

        if (wroteText)
        {
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine("No output text was returned.");
        }
    }

    private static void WriteFinalMessage(TextWriter writer, AssistantMessage message, bool wroteText)
    {
        if (!wroteText)
        {
            var text = string.Concat(message.Content.OfType<TextContent>().Select(static content => content.Text));
            writer.WriteLine(string.IsNullOrEmpty(text) ? "No output text was returned." : text);
        }
        else
        {
            writer.WriteLine();
        }

        if (message.StopReason is not StopReason.Stop)
        {
            writer.WriteLine($"Stop reason: {message.StopReason}");
        }
    }

    private static void WriteStatusLine(TextWriter writer, bool wroteText, string message)
    {
        if (wroteText)
        {
            writer.WriteLine();
        }

        writer.WriteLine(message);
    }

    private static Context CreateContext(AskRequest request) => new()
    {
        System = request.SystemInstructions,
        Messages =
        [
            new UserMessage([new TextContent(request.Prompt)]),
        ],
    };
}

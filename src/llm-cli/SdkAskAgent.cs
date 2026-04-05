using System.Text.Json;
using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli;

public static class SdkAskAgent
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

        var response = await client.CreateResponseAsync(CreateRequest(request), cancellationToken);
        var text = response.GetOutputText();
        writer.WriteLine(text is null ? "No output text was returned." : text);
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

        var wroteText = false;

        await foreach (var streamEvent in client.CreateResponseStreamAsync(CreateRequest(request, stream: true), cancellationToken))
        {
            switch (streamEvent)
            {
                case OutputTextDeltaEvent delta:
                    writer.Write(delta.Delta);
                    wroteText = true;
                    break;
                case ResponseFailedEvent failed:
                    WriteStatusLine(writer, wroteText, $"Response failed: {GetFailureMessage(failed.Response)}");
                    return;
                case ResponseIncompleteEvent incomplete:
                    WriteStatusLine(writer, wroteText, $"Response incomplete: {GetIncompleteReason(incomplete.Response)}");
                    return;
            }
        }

        writer.WriteLine();
    }

    private static CreateResponseRequest CreateRequest(AskRequest request, bool? stream = null)
    {
        return new CreateResponseRequest
        {
            Model = request.Model,
            Input = JsonSerializer.SerializeToElement(request.Prompt, JsonDefaults.Web),
            Stream = stream,
            Instructions = request.SystemInstructions,
        };
    }

    private static void WriteStatusLine(TextWriter writer, bool wroteText, string message)
    {
        if (wroteText)
        {
            writer.WriteLine();
        }

        writer.WriteLine(message);
    }

    private static string GetFailureMessage(Response response)
        => response.Error?.Message ?? response.Status;

    private static string GetIncompleteReason(Response response)
        => response.IncompleteDetails?.Reason ?? response.Status;
}

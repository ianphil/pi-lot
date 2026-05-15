using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace llm_cli.Agents;

public static class SdkContextAskAgent
{
    public static async Task RunAsync(
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

    private static Context CreateContext(AskRequest request) => new()
    {
        System = request.SystemInstructions,
        Messages =
        [
            new UserMessage([new TextContent(request.Prompt)]),
        ],
    };
}

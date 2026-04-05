using LlmSdk.Core.Models;

namespace LlmSdk.Client;

public static class ResponseExtensions
{
    public static string? GetOutputText(this Response response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (var item in response.Output)
        {
            if (item is not ResponseMessageItem message)
            {
                continue;
            }

            foreach (var contentPart in message.Content)
            {
                if (contentPart is ResponseOutputTextPart outputText)
                {
                    return outputText.Text;
                }
            }

            return null;
        }

        return null;
    }
}

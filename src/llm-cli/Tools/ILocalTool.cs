#pragma warning disable OPENAI001

using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli;

public interface ILocalTool
{
    string Name { get; }

    string Description { get; }

    JsonElement? Parameters { get; }

    bool? Strict { get; }

    ResponseTool Definition { get; }

    ChatTool ChatDefinition { get; }

    Task<string> ExecuteAsync(BinaryData arguments, CancellationToken cancellationToken);
}

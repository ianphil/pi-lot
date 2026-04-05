#pragma warning disable OPENAI001

using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli;

public interface ILocalTool
{
    string Name { get; }

    ResponseTool Definition { get; }

    ChatTool ChatDefinition { get; }

    Task<string> ExecuteAsync(BinaryData arguments, CancellationToken cancellationToken);
}

#pragma warning disable OPENAI001

using OpenAI.Chat;
using OpenAI.Responses;

namespace llm_cli;

public interface IToolRegistry
{
    IReadOnlyList<ILocalTool> Tools { get; }

    IReadOnlyList<ResponseTool> Definitions { get; }

    IReadOnlyList<ChatTool> ChatDefinitions { get; }

    Task<string> ExecuteAsync(string toolName, BinaryData arguments, CancellationToken cancellationToken);
}

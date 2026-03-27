#pragma warning disable OPENAI001

using System.Text.Json;
using OpenAI.Responses;

namespace llm_cli;

public interface ILocalTool
{
    string Name { get; }

    ResponseTool Definition { get; }

    Task<string> ExecuteAsync(BinaryData arguments, CancellationToken cancellationToken);
}

public interface IToolRegistry
{
    IReadOnlyList<ResponseTool> Definitions { get; }

    Task<string> ExecuteAsync(string toolName, BinaryData arguments, CancellationToken cancellationToken);
}

public sealed class LocalToolRegistry : IToolRegistry
{
    private static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ILocalTool> _tools;
    private readonly IReadOnlyList<ResponseTool> _definitions;

    public LocalToolRegistry(IEnumerable<ILocalTool> tools)
    {
        var toolList = tools.ToList();
        _tools = toolList.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _definitions = toolList.Select(tool => tool.Definition).ToList();
    }

    public IReadOnlyList<ResponseTool> Definitions => _definitions;

    public static LocalToolRegistry CreateDefault(HttpClient httpClient)
        => new([new FetchUrlTool(httpClient)]);

    public Task<string> ExecuteAsync(string toolName, BinaryData arguments, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                ok = false,
                error = $"Unsupported tool '{toolName}'.",
            }, s_JsonOptions));
        }

        return tool.ExecuteAsync(arguments, cancellationToken);
    }
}

#pragma warning disable OPENAI001

using OpenAI.Responses;

namespace llm_cli;

public sealed class AskAgent
{
    public const int MaxToolIterations = 10;

    private readonly Func<CreateResponseOptions, CancellationToken, Task<ResponseResult>> _createResponseAsync;
    private readonly Func<CreateResponseOptions, CancellationToken, IAsyncEnumerable<StreamingResponseUpdate>> _createResponseStreamingAsync;
    private readonly IToolRegistry _toolRegistry;
    private readonly TextWriter _writer;

    public AskAgent(
        Func<CreateResponseOptions, CancellationToken, Task<ResponseResult>> createResponseAsync,
        Func<CreateResponseOptions, CancellationToken, IAsyncEnumerable<StreamingResponseUpdate>> createResponseStreamingAsync,
        IToolRegistry toolRegistry,
        TextWriter writer)
    {
        _createResponseAsync = createResponseAsync;
        _createResponseStreamingAsync = createResponseStreamingAsync;
        _toolRegistry = toolRegistry;
        _writer = writer;
    }

    public static AskAgent Create(ResponsesClient client, IToolRegistry toolRegistry, TextWriter writer)
        => new(
            async (options, cancellationToken) => await client.CreateResponseAsync(options, cancellationToken),
            (options, cancellationToken) => client.CreateResponseStreamingAsync(options, cancellationToken),
            toolRegistry,
            writer);

    public static CreateResponseOptions BuildOptions(
        AskRequest request,
        IEnumerable<ResponseItem> inputItems,
        bool streamingEnabled,
        IToolRegistry toolRegistry)
    {
        var options = new CreateResponseOptions(request.Model, inputItems)
        {
            StreamingEnabled = streamingEnabled,
        };

        if (!string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            options.Instructions = request.SystemInstructions;
        }

        if (request.ToolsEnabled)
        {
            foreach (var tool in toolRegistry.Definitions)
            {
                options.Tools.Add(tool);
            }

            options.ToolChoice = ResponseToolChoice.CreateAutoChoice();
        }

        return options;
    }

    public async Task<string> RunNonStreamingAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var conversationItems = CreateConversation(request.Prompt);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var options = BuildOptions(request, conversationItems, streamingEnabled: false, _toolRegistry);
            var response = await _createResponseAsync(options, cancellationToken);
            var functionCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

            if (functionCalls.Count == 0)
            {
                return response.GetOutputText();
            }

            await AppendToolResultsAsync(conversationItems, functionCalls, cancellationToken);
        }

        throw new InvalidOperationException($"Tool loop exceeded the maximum of {MaxToolIterations} iterations.");
    }

    public async Task RunStreamingAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var conversationItems = CreateConversation(request.Prompt);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var options = BuildOptions(request, conversationItems, streamingEnabled: true, _toolRegistry);
            var turn = await StreamResponseAsync(options, request.ToolsEnabled, cancellationToken);
            var response = turn.Response;
            var functionCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

            if (functionCalls.Count == 0)
            {
                if (request.ToolsEnabled)
                {
                    var finalText = turn.BufferedOutput.Length > 0
                        ? turn.BufferedOutput
                        : response.GetOutputText();

                    _writer.Write(finalText);
                }

                _writer.WriteLine();
                return;
            }

            await AppendToolResultsAsync(conversationItems, functionCalls, cancellationToken);
        }

        throw new InvalidOperationException($"Tool loop exceeded the maximum of {MaxToolIterations} iterations.");
    }

    private static List<ResponseItem> CreateConversation(string prompt)
        => [ResponseItem.CreateUserMessageItem(prompt)];

    private async Task<StreamTurnResult> StreamResponseAsync(
        CreateResponseOptions options,
        bool bufferOutput,
        CancellationToken cancellationToken)
    {
        ResponseResult? terminalResponse = null;
        var textBuffer = new StringWriter();

        await foreach (var update in _createResponseStreamingAsync(options, cancellationToken))
        {
            switch (update)
            {
                case StreamingResponseOutputTextDeltaUpdate delta:
                    if (bufferOutput)
                    {
                        textBuffer.Write(delta.Delta);
                    }
                    else
                    {
                        _writer.Write(delta.Delta);
                    }

                    break;
                case StreamingResponseCompletedUpdate completed:
                    terminalResponse = completed.Response;
                    break;
                case StreamingResponseIncompleteUpdate incomplete:
                    terminalResponse = incomplete.Response;
                    break;
                case StreamingResponseFailedUpdate failed:
                    terminalResponse = failed.Response;
                    break;
            }
        }

        return new StreamTurnResult(
            terminalResponse ?? throw new InvalidOperationException(
                "Streaming response ended without a terminal response update."),
            textBuffer.ToString());
    }

    private async Task AppendToolResultsAsync(
        IList<ResponseItem> conversationItems,
        IEnumerable<FunctionCallResponseItem> functionCalls,
        CancellationToken cancellationToken)
    {
        foreach (var functionCall in functionCalls)
        {
            conversationItems.Add(functionCall);

            var output = await _toolRegistry.ExecuteAsync(
                functionCall.FunctionName,
                functionCall.FunctionArguments,
                cancellationToken);

            conversationItems.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, output));
        }
    }

    private sealed record StreamTurnResult(ResponseResult Response, string BufferedOutput);
}

#pragma warning disable OPENAI001

using OpenAI.Chat;

namespace llm_cli;

public sealed class ChatAgent
{
    public const int MaxToolIterations = 10;

    private readonly Func<IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<ChatCompletion>> _completeChatAsync;
    private readonly Func<IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken, IAsyncEnumerable<StreamingChatCompletionUpdate>> _completeChatStreamingAsync;
    private readonly IToolRegistry _toolRegistry;
    private readonly TextWriter _writer;

    public ChatAgent(
        Func<IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<ChatCompletion>> completeChatAsync,
        Func<IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken, IAsyncEnumerable<StreamingChatCompletionUpdate>> completeChatStreamingAsync,
        IToolRegistry toolRegistry,
        TextWriter writer)
    {
        _completeChatAsync = completeChatAsync;
        _completeChatStreamingAsync = completeChatStreamingAsync;
        _toolRegistry = toolRegistry;
        _writer = writer;
    }

    public static ChatAgent Create(ChatClient client, IToolRegistry toolRegistry, TextWriter writer)
        => new(
            async (messages, options, cancellationToken) => await client.CompleteChatAsync(messages, options, cancellationToken),
            (messages, options, cancellationToken) => client.CompleteChatStreamingAsync(messages, options, cancellationToken),
            toolRegistry,
            writer);

    public async Task<string> RunNonStreamingAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var messages = CreateConversation(request);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var options = BuildOptions(request);
            var completion = await _completeChatAsync(messages, options, cancellationToken);
            var toolCalls = completion.ToolCalls;

            if (toolCalls is not { Count: > 0 })
            {
                return completion.Content[0].Text;
            }

            messages.Add(new AssistantChatMessage(completion));
            await AppendToolResultsAsync(messages, toolCalls, cancellationToken);
        }

        throw new InvalidOperationException($"Tool loop exceeded the maximum of {MaxToolIterations} iterations.");
    }

    public async Task RunStreamingAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var messages = CreateConversation(request);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var options = BuildOptions(request);
            var turn = await StreamResponseAsync(messages, options, request.ToolsEnabled, cancellationToken);

            if (turn.ToolCalls.Count == 0)
            {
                if (request.ToolsEnabled && turn.BufferedOutput.Length > 0)
                {
                    _writer.Write(turn.BufferedOutput);
                }

                _writer.WriteLine();
                return;
            }

            messages.Add(new AssistantChatMessage(turn.ToolCalls) { Content = { turn.BufferedOutput } });
            await AppendToolResultsAsync(messages, turn.ToolCalls, cancellationToken);
        }

        throw new InvalidOperationException($"Tool loop exceeded the maximum of {MaxToolIterations} iterations.");
    }

    internal ChatCompletionOptions BuildOptions(AskRequest request)
    {
        var options = new ChatCompletionOptions();

        if (request.ToolsEnabled)
        {
            foreach (var tool in _toolRegistry.ChatDefinitions)
            {
                options.Tools.Add(tool);
            }

            options.ToolChoice = ChatToolChoice.CreateAutoChoice();
        }

        return options;
    }

    private static List<ChatMessage> CreateConversation(AskRequest request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            messages.Add(new SystemChatMessage(request.SystemInstructions));
        }

        messages.Add(new UserChatMessage(request.Prompt));
        return messages;
    }

    private async Task<StreamTurnResult> StreamResponseAsync(
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        bool bufferOutput,
        CancellationToken cancellationToken)
    {
        var textBuffer = new StringWriter();
        var toolCallsBuilder = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)>();

        await foreach (var update in _completeChatStreamingAsync(messages, options, cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (part.Text is not null)
                {
                    if (bufferOutput)
                    {
                        textBuffer.Write(part.Text);
                    }
                    else
                    {
                        _writer.Write(part.Text);
                    }
                }
            }

            foreach (var toolCallUpdate in update.ToolCallUpdates)
            {
                if (!toolCallsBuilder.TryGetValue(toolCallUpdate.Index, out var existing))
                {
                    existing = (toolCallUpdate.ToolCallId ?? "", toolCallUpdate.FunctionName ?? "", new System.Text.StringBuilder());
                    toolCallsBuilder[toolCallUpdate.Index] = existing;
                }

                if (toolCallUpdate.FunctionArgumentsUpdate is not null)
                {
                    existing.Args.Append(toolCallUpdate.FunctionArgumentsUpdate);
                    toolCallsBuilder[toolCallUpdate.Index] = existing;
                }
            }
        }

        var toolCalls = toolCallsBuilder.Values
            .Select(tc => ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, BinaryData.FromString(tc.Args.ToString())))
            .ToList();

        return new StreamTurnResult(textBuffer.ToString(), toolCalls);
    }

    private async Task AppendToolResultsAsync(
        IList<ChatMessage> messages,
        IEnumerable<ChatToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        foreach (var toolCall in toolCalls)
        {
            var output = await _toolRegistry.ExecuteAsync(
                toolCall.FunctionName,
                toolCall.FunctionArguments,
                cancellationToken);

            messages.Add(new ToolChatMessage(toolCall.Id, output));
        }
    }

    private sealed record StreamTurnResult(string BufferedOutput, IReadOnlyList<ChatToolCall> ToolCalls);
}

namespace llm_ui;

public static class UiDefaults
{
    public const string DefaultModel = "gpt-5.4";
}

public sealed record UiConfig(string DefaultModel);

public sealed record UiModel(string Id, string DisplayName);

public sealed record UiModelsResponse(string DefaultModel, IReadOnlyList<UiModel> Models);

public sealed record UiChatRequest(string? Model, string? ConversationMarkdown, string? Message);

public sealed record UiErrorResponse(IReadOnlyList<string> Errors);

public sealed record UiChatDelta(string Type, string Text);

public sealed record UiChatDone(string Type);

public sealed record UiChatError(string Type, string Message);

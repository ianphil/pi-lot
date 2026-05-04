using LlmAgent;

namespace llm_ui.Services;

public static class ConversationMarkdownParser
{
    public static ConversationMarkdown Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var sections = ReadSections(markdown);
        var instructions = new List<string>();
        var turns = new List<ConversationTurn>();
        var errors = new List<string>();

        foreach (var section in sections)
        {
            switch (section.Heading.ToLowerInvariant())
            {
                case "system":
                    if (!string.IsNullOrWhiteSpace(section.Content))
                    {
                        instructions.Add(section.Content);
                    }

                    break;

                case "user":
                    AddTurn(ConversationRole.User, section, turns, errors);
                    break;

                case "assistant":
                    AddTurn(ConversationRole.Assistant, section, turns, errors);
                    break;

                case "tool":
                    errors.Add("Tool sections are not supported in the first llm-ui prototype.");
                    break;

                default:
                    errors.Add($"Unsupported conversation section: '{section.Heading}'.");
                    break;
            }
        }

        return new ConversationMarkdown(
            instructions.Count == 0 ? null : string.Join("\n\n", instructions),
            turns,
            errors);
    }

    public static AgentContext ToAgentContext(this ConversationMarkdown conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.Errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(" ", conversation.Errors));
        }

        var context = new AgentContext();

        foreach (var turn in conversation.Turns)
        {
            switch (turn.Role)
            {
                case ConversationRole.User:
                    context.AddUserMessage(turn.Text);
                    break;
                case ConversationRole.Assistant:
                    context.AddAssistantMessage(turn.Text);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported conversation role: {turn.Role}.");
            }
        }

        return context;
    }

    private static void AddTurn(
        ConversationRole role,
        MarkdownSection section,
        ICollection<ConversationTurn> turns,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(section.Content))
        {
            errors.Add($"Section '{section.Heading}' must contain message text.");
            return;
        }

        turns.Add(new ConversationTurn(role, section.Content));
    }

    private static IReadOnlyList<MarkdownSection> ReadSections(string markdown)
    {
        var sections = new List<MarkdownSection>();
        var heading = string.Empty;
        var content = new List<string>();

        foreach (var rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (rawLine.StartsWith("## ", StringComparison.Ordinal))
            {
                AddSection(sections, heading, content);
                heading = rawLine[3..].Trim();
                content.Clear();
                continue;
            }

            content.Add(rawLine);
        }

        AddSection(sections, heading, content);
        return sections;
    }

    private static void AddSection(ICollection<MarkdownSection> sections, string heading, IEnumerable<string> content)
    {
        var text = string.Join("\n", content).Trim();

        if (string.IsNullOrWhiteSpace(heading) && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        sections.Add(new MarkdownSection(heading, text));
    }

    private sealed record MarkdownSection(string Heading, string Content);
}

public sealed record ConversationMarkdown(
    string? Instructions,
    IReadOnlyList<ConversationTurn> Turns,
    IReadOnlyList<string> Errors);

public sealed record ConversationTurn(ConversationRole Role, string Text);

public enum ConversationRole
{
    User,
    Assistant,
}

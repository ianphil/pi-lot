using llm_ui.Services;

namespace llm_ui.Tests;

public sealed class ConversationMarkdownParserTests
{
    [Fact]
    public void Parse_WithSystemUserAndAssistantSections_ReturnsTypedConversation()
    {
        var markdown = """
            ## System

            Be concise.

            ## User

            Hello.

            ## Assistant

            Hi there.
            """;

        var conversation = ConversationMarkdownParser.Parse(markdown);

        Assert.Empty(conversation.Errors);
        Assert.Equal("Be concise.", conversation.Instructions);
        Assert.Collection(
            conversation.Turns,
            user =>
            {
                Assert.Equal(ConversationRole.User, user.Role);
                Assert.Equal("Hello.", user.Text);
            },
            assistant =>
            {
                Assert.Equal(ConversationRole.Assistant, assistant.Role);
                Assert.Equal("Hi there.", assistant.Text);
            });
    }

    [Fact]
    public void Parse_WithUnsupportedSection_ReturnsValidationError()
    {
        var conversation = ConversationMarkdownParser.Parse("""
            ## Developer

            Hidden instruction.
            """);

        var error = Assert.Single(conversation.Errors);
        Assert.Equal("Unsupported conversation section: 'Developer'.", error);
    }

    [Fact]
    public void ToAgentContext_WithAssistantTurn_SerializesAssistantAsOutputText()
    {
        var conversation = ConversationMarkdownParser.Parse("""
            ## User

            Hello.

            ## Assistant

            Hi there.
            """);

        var input = conversation.ToAgentContext().ToResponseInput();

        Assert.Equal("user", input[0].GetProperty("role").GetString());
        Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("assistant", input[1].GetProperty("role").GetString());
        Assert.Equal("output_text", input[1].GetProperty("content")[0].GetProperty("type").GetString());
    }
}

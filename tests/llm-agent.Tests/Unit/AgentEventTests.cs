using LlmSdk.Client;
using LlmSdk.Core.Models;

namespace LlmAgent.Tests.Unit;

public sealed class AgentEventTests
{
    [Fact]
    public void AgentEventSubtypes_CanBePatternMatched()
    {
        var context = new AgentContext();
        context.AddUserMessage("hello");

        var message = new AssistantMessage([new TextContent("hi")], StopReason.Stop);
        var streamEvent = new TextDelta("hi");
        var toolResult = new AgentToolResult("done");
        var usage = new Usage(10, 5);
        var diagnostics = new Diagnostics(
        [
            new DiagnosticEntry(DiagnosticSeverity.Warning, "test_warning", "Test warning."),
        ]);

        var names = new[]
        {
            Describe(new AgentStarted()),
            Describe(new AgentEnded(context) { Status = AgentRunStatus.Completed }),
            Describe(new TurnStarted()),
            Describe(new TurnEnded(message, [new AgentToolCallResult("call_1", "lookup", "done", false)])),
            Describe(new ContextBudgetWarning(new AgentContextBudgetResult(
                "gpt-5.4",
                600,
                1000,
                0.60,
                AgentContextBudgetLevel.Warning,
                new ModelTokenLimits { MaxPromptTokens = 1000 }))),
            Describe(new MessageStarted()),
            Describe(new MessageDelta(streamEvent)),
            Describe(new MessageUsage(usage)),
            Describe(new MessageDiagnostics(diagnostics)),
            Describe(new MessageEnded(message) { Status = AgentMessageStatus.Completed }),
            Describe(new ToolExecutionStarted("call_1", "lookup", "{\"city\":\"Paris\"}")),
            Describe(new ToolExecutionEnded("call_1", "lookup", toolResult)),
        };

        Assert.Equal(
        [
            "agent_started",
            "agent_ended",
            "turn_started",
            "turn_ended",
            "context_budget_warning",
            "message_started",
            "message_delta",
            "message_usage",
            "message_diagnostics",
            "message_ended",
            "tool_execution_started",
            "tool_execution_ended",
        ], names);
    }

    private static string Describe(AgentEvent agentEvent) => agentEvent switch
    {
        AgentStarted => "agent_started",
        AgentEnded => "agent_ended",
        TurnStarted => "turn_started",
        TurnEnded => "turn_ended",
        ContextBudgetWarning => "context_budget_warning",
        MessageStarted => "message_started",
        MessageDelta => "message_delta",
        MessageUsage => "message_usage",
        MessageDiagnostics => "message_diagnostics",
        MessageEnded => "message_ended",
        ToolExecutionStarted => "tool_execution_started",
        ToolExecutionEnded => "tool_execution_ended",
        _ => throw new InvalidOperationException($"Unknown event type: {agentEvent.GetType().Name}"),
    };
}

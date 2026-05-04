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

        var response = new Response
        {
            Id = "resp_123",
            Output =
            [
                new ResponseMessageItem
                {
                    Id = "msg_123",
                    Content =
                    [
                        new ResponseOutputTextPart
                        {
                            Text = "hi",
                        },
                    ],
                },
            ],
        };
        var streamEvent = new OutputTextDeltaEvent("response.output_text.delta", 1, "hi", 0, 0, "msg_123");
        var toolResult = new AgentToolResult("done");

        var names = new[]
        {
            Describe(new AgentStarted()),
            Describe(new AgentEnded(context)),
            Describe(new TurnStarted()),
            Describe(new TurnEnded(response, [new AgentToolCallResult("call_1", "lookup", "done", false)])),
            Describe(new ContextBudgetWarning(new AgentContextBudgetResult(
                "gpt-5.4",
                600,
                1000,
                0.60,
                AgentContextBudgetLevel.Warning,
                new ModelTokenLimits { MaxPromptTokens = 1000 }))),
            Describe(new MessageStarted()),
            Describe(new MessageDelta(streamEvent)),
            Describe(new MessageEnded(response)),
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
        MessageEnded => "message_ended",
        ToolExecutionStarted => "tool_execution_started",
        ToolExecutionEnded => "tool_execution_ended",
        _ => throw new InvalidOperationException($"Unknown event type: {agentEvent.GetType().Name}"),
    };
}

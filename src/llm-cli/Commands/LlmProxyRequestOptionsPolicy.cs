using System.ClientModel.Primitives;
using llm_cli.Agents;

namespace llm_cli.Commands;

internal sealed class LlmProxyRequestOptionsPolicy(AskRequest request) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request.Headers, request);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request.Headers, request);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    internal static void Apply(PipelineRequestHeaders headers, AskRequest request)
    {
        SetIfPresent(headers, "X-LLM-Request-Id", request.RequestId);
        SetIfPresent(headers, "X-LLM-Correlation-Id", request.CorrelationId);
        SetIfPresent(headers, "X-LLM-Timeout-Ms", request.TimeoutMs?.ToString());
        SetIfPresent(headers, "X-LLM-Max-Retries", request.MaxRetries?.ToString());
        SetIfPresent(headers, "X-LLM-Max-Retry-Delay-Ms", request.MaxRetryDelayMs?.ToString());

        if (request.Metadata is null)
        {
            return;
        }

        foreach (var (key, value) in request.Metadata)
        {
            SetIfPresent(headers, $"X-LLM-Metadata-{key}", value);
        }
    }

    private static void SetIfPresent(PipelineRequestHeaders headers, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        headers.Set(name, value);
    }
}

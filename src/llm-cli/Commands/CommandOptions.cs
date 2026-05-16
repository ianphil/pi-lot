using System.CommandLine;
using System.ClientModel.Primitives;
using llm_cli.Agents;
using OpenAI;

namespace llm_cli.Commands;

public static class CommandOptions
{
    private const int MaxTimeoutMs = 600_000;
    private const int MaxRetriesValue = 3;
    private const int MaxRetryDelayMsValue = 30_000;
    private const int MaxMetadataPairs = 16;
    private const int MaxMetadataKeyLength = 64;
    private const int MaxMetadataValueLength = 256;

    public static Argument<string> Prompt()
        => new("prompt") { Description = "The prompt to send" };

    public static Option<string> Model(string defaultModel)
        => new("--model", "-m")
        {
            Description = "Model to use",
            DefaultValueFactory = _ => defaultModel,
        };

    public static Option<string?> System()
        => new("--system", "-s") { Description = "System instructions" };

    public static Option<bool> NoStream()
        => new("--no-stream") { Description = "Disable streaming" };

    public static Option<bool> Tools()
        => new("--tools") { Description = "Enable local tools (currently: fetch_url)" };

    public static Option<string> Endpoint()
        => new("--endpoint", "-e")
        {
            Description = "Base URL of the LLM proxy",
            DefaultValueFactory = _ => "http://localhost:5100",
        };

    public static Option<string?> RequestId()
        => new("--request-id") { Description = "Request ID to send as X-Request-Id through the SDK/proxy" };

    public static Option<string?> CorrelationId()
        => new("--correlation-id") { Description = "Local correlation ID for SDK/proxy diagnostics" };

    public static Option<int?> TimeoutMs()
        => new("--timeout-ms") { Description = "Per-call upstream timeout in milliseconds" };

    public static Option<int?> MaxRetries()
        => new("--max-retries") { Description = "Per-call maximum retry count" };

    public static Option<int?> MaxRetryDelayMs()
        => new("--max-retry-delay-ms") { Description = "Per-call maximum retry delay in milliseconds" };

    public static Option<string[]> Metadata()
        => new("--metadata")
        {
            Description = "Local metadata as key=value. Repeat for multiple values.",
            AllowMultipleArgumentsPerToken = true,
        };

    public static OpenAIClientOptions CreateProxyClientOptions(string endpoint, AskRequest request)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        options.AddPolicy(new LlmProxyRequestOptionsPolicy(request), PipelinePosition.PerCall);
        return options;
    }

    public static IToolRegistry CreateDefaultToolRegistry(HttpClient httpClient)
        => LocalToolRegistry.CreateDefault(httpClient);

    public static AskRequest CreateAskRequest(
        ParseResult parseResult,
        Argument<string> prompt,
        Option<string> model,
        Option<string?> system,
        Option<bool>? tools,
        Option<string?> requestId,
        Option<string?> correlationId,
        Option<string[]> metadata,
        Option<int?> timeoutMs,
        Option<int?> maxRetries,
        Option<int?> maxRetryDelayMs)
    {
        var timeoutValue = ValidateRange(parseResult.GetValue(timeoutMs), "--timeout-ms", 1, MaxTimeoutMs);
        var maxRetriesValue = ValidateRange(parseResult.GetValue(maxRetries), "--max-retries", 0, MaxRetriesValue);
        var maxRetryDelayValue = ValidateRange(parseResult.GetValue(maxRetryDelayMs), "--max-retry-delay-ms", 1, MaxRetryDelayMsValue);

        return new(
            parseResult.GetValue(prompt)!,
            parseResult.GetValue(model)!,
            parseResult.GetValue(system),
            tools is not null && parseResult.GetValue(tools),
            string.IsNullOrWhiteSpace(parseResult.GetValue(requestId))
                ? "cli-" + Guid.NewGuid().ToString("N")
                : parseResult.GetValue(requestId),
            parseResult.GetValue(correlationId),
            ParseMetadata(parseResult.GetValue(metadata)),
            timeoutValue,
            maxRetriesValue,
            maxRetryDelayValue);
    }

    private static IReadOnlyDictionary<string, string>? ParseMetadata(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        if (values.Length > MaxMetadataPairs)
        {
            throw new ArgumentException($"At most {MaxMetadataPairs} --metadata values are allowed.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException("--metadata values must use key=value syntax.");
            }

            var key = value[..separator];
            var metadataValue = value[(separator + 1)..];
            if (!IsValidMetadataKey(key))
            {
                throw new ArgumentException($"Metadata key '{key}' must use 1 through {MaxMetadataKeyLength} letters, digits, '.', '_', or '-'.");
            }

            if (metadataValue.Length > MaxMetadataValueLength)
            {
                throw new ArgumentException($"Metadata value for '{key}' must be {MaxMetadataValueLength} characters or fewer.");
            }

            if (metadata.ContainsKey(key))
            {
                throw new ArgumentException($"Metadata key '{key}' was provided more than once.");
            }

            metadata[key] = metadataValue;
        }

        return metadata;
    }

    private static int? ValidateRange(int? value, string optionName, int min, int max)
    {
        if (value is null)
        {
            return null;
        }

        if (value < min || value > max)
        {
            throw new ArgumentException($"{optionName} must be an integer from {min} through {max}.");
        }

        return value;
    }

    private static bool IsValidMetadataKey(string key)
    {
        if (key.Length is 0 or > MaxMetadataKeyLength)
        {
            return false;
        }

        foreach (var ch in key)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }
}

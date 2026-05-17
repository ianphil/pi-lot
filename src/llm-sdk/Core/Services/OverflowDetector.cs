using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LlmSdk.Core.Models;

namespace LlmSdk.Core.Services;

public static partial class OverflowDetector
{
    private const int SnippetLimit = 4096;

    public static bool IsOverflow(int statusCode, string? upstreamBodySnippet, string? upstreamErrorCode)
    {
        if (string.Equals(upstreamErrorCode, ErrorCodes.ContextLengthExceeded, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (statusCode != 400 || string.IsNullOrWhiteSpace(upstreamBodySnippet))
        {
            return false;
        }

        var snippet = Limit(upstreamBodySnippet);
        return MaximumContextLengthRegex().IsMatch(snippet) ||
               ThisModelsMaximumContextLengthRegex().IsMatch(snippet) ||
               InputTooLongRegex().IsMatch(snippet) ||
               PromptTooLongRegex().IsMatch(snippet) ||
               TooManyTokensRegex().IsMatch(snippet) ||
               ReduceMessagesLengthRegex().IsMatch(snippet);
    }

    public static (int? window, int? input) TryExtractTokens(string? upstreamBodySnippet)
    {
        if (string.IsNullOrWhiteSpace(upstreamBodySnippet))
        {
            return (null, null);
        }

        var snippet = Limit(upstreamBodySnippet);
        return (
            TryParseTokenCount(MaximumContextLengthRegex().Match(snippet)),
            TryParseTokenCount(RequestedTokensRegex().Match(snippet)));
    }

    public static bool IsSilentTruncation(long inputTokens, int? contextWindow, StopReason stopReason) =>
        stopReason == StopReason.Length &&
        inputTokens > 0 &&
        contextWindow is > 0 &&
        inputTokens > contextWindow.Value * 0.95;

    private static string Limit(string value) =>
        value.Length <= SnippetLimit ? value : value[..SnippetLimit];

    private static int? TryParseTokenCount(Match match)
    {
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        var digits = new StringBuilder(match.Groups[1].Value.Length);
        foreach (var ch in match.Groups[1].Value)
        {
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
            }
        }

        return int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"maximum context length is (\d[\d,_\s]*)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex MaximumContextLengthRegex();

    [GeneratedRegex(@"requested (\d[\d,_\s]*) (?:input )?tokens", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex RequestedTokensRegex();

    [GeneratedRegex(@"\bthis model's maximum context length\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex ThisModelsMaximumContextLengthRegex();

    [GeneratedRegex(@"\binput is too long\b(?!-)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex InputTooLongRegex();

    [GeneratedRegex(@"\bprompt is too long\b(?!-)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex PromptTooLongRegex();

    [GeneratedRegex(@"\btoo many tokens\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex TooManyTokensRegex();

    [GeneratedRegex(@"\breduce the length of (?:the )?messages\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 50)]
    private static partial Regex ReduceMessagesLengthRegex();
}

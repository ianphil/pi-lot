using System.Globalization;
using System.Text.Json;
using LlmSdk.Core.Models;
using LlmSdk.Core.Services;

namespace LlmSdk.Client;

internal static class LlmSdkExceptionFactory
{
    public static LlmSdkException Create(int statusCode, string? body)
    {
        var error = Parse(body);

        if (OverflowDetector.IsOverflow(statusCode, error.Message, error.Code))
        {
            var (window, input) = OverflowDetector.TryExtractTokens(error.Message);
            return new ContextOverflowException(
                error.Message,
                window,
                input,
                error.Code ?? ErrorCodes.ContextLengthExceeded,
                error.Type,
                error.Param,
                statusCode);
        }

        return statusCode switch
        {
            401 => new AuthenticationException(error.Message, error.Code, error.Type, error.Param, statusCode),
            404 when string.Equals(error.Code, Core.Models.ErrorCodes.ModelNotFound, StringComparison.OrdinalIgnoreCase) =>
                new ModelNotFoundException(error.Message, error.Type, error.Param),
            429 => new RateLimitException(error.Message, error.RetryAfter, error.Code, error.Type, error.Param, statusCode),
            _ => new LlmSdkException(error.Message, error.Code, statusCode, error.Type, error.Param),
        };
    }

    private static ParsedError Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ParsedError("Request failed.", null, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errorElement = root;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out var nestedError) &&
                nestedError.ValueKind == JsonValueKind.Object)
            {
                errorElement = nestedError;
            }

            if (errorElement.ValueKind != JsonValueKind.Object)
            {
                return new ParsedError(body, null, null, null, null);
            }

            return new ParsedError(
                TryGetString(errorElement, "message") ?? body,
                TryGetString(errorElement, "code"),
                TryGetString(errorElement, "type"),
                TryGetString(errorElement, "param"),
                TryGetRetryAfter(errorElement) ?? TryGetRetryAfter(root));
        }
        catch (JsonException)
        {
            return new ParsedError(body, null, null, null, null);
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.GetRawText(),
            _ => null,
        };
    }

    private static TimeSpan? TryGetRetryAfter(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("retry_after", out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var seconds) && seconds >= 0 =>
                TimeSpan.FromSeconds(seconds),
            JsonValueKind.String when TimeSpan.TryParse(property.GetString(), CultureInfo.InvariantCulture, out var retryAfter) =>
                retryAfter,
            JsonValueKind.String when double.TryParse(property.GetString(), CultureInfo.InvariantCulture, out var retryAfterSeconds)
                                      && retryAfterSeconds >= 0 =>
                TimeSpan.FromSeconds(retryAfterSeconds),
            _ => null,
        };
    }

    private sealed record ParsedError(
        string Message,
        string? Code,
        string? Type,
        string? Param,
        TimeSpan? RetryAfter);
}

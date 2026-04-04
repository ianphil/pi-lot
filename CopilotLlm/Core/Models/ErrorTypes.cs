namespace CopilotLlm.Core.Models;

/// <summary>
/// Well-known error type strings returned in the Responses API error envelope.
/// </summary>
public static class ErrorTypes
{
    public const string InvalidRequestError = "invalid_request_error";
    public const string ServerError = "server_error";
    public const string TooManyRequests = "too_many_requests";
    public const string NotFound = "not_found";
}

/// <summary>
/// Well-known error code strings returned in the Responses API error envelope.
/// </summary>
public static class ErrorCodes
{
    public const string MissingRequiredParameter = "missing_required_parameter";
    public const string ModelNotFound = "model_not_found";
    public const string UnsupportedModelEndpoint = "unsupported_model_endpoint";
    public const string InvalidUpstreamResponse = "invalid_upstream_response";
    public const string InvalidInputFormat = "invalid_input_format";
    public const string StreamError = "stream_error";
    public const string AuthError = "auth_error";
}

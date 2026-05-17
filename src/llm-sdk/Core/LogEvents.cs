namespace LlmSdk.Core;

/// <summary>
/// Centralized Event IDs for Windows Event Log entries.
/// Grouped by category: 1xxx=Lifecycle, 2xxx=Auth, 3xxx=API, 4xxx=Errors.
/// </summary>
public static class LogEvents
{
    // ── Lifecycle (1xxx) ─────────────────────────────────────────────────────
    public static readonly EventId ServiceStarted = new(1000, "ServiceStarted");
    public static readonly EventId ServiceStopping = new(1001, "ServiceStopping");

    // ── Auth (2xxx) ──────────────────────────────────────────────────────────
    public static readonly EventId CredentialLoaded = new(2000, "CredentialLoaded");
    public static readonly EventId CredentialMissing = new(2001, "CredentialMissing");
    public static readonly EventId TokenValidated = new(2002, "TokenValidated");
    public static readonly EventId TokenValidationFailed = new(2003, "TokenValidationFailed");
    public static readonly EventId TokenExpired = new(2004, "TokenExpired");

    // ── API (3xxx) ───────────────────────────────────────────────────────────
    public static readonly EventId ModelsFetched = new(3000, "ModelsFetched");
    public static readonly EventId RequestProxied = new(3001, "RequestProxied");
    public static readonly EventId RequestFailed = new(3002, "RequestFailed");
    public static readonly EventId SilentTruncationSuspected = new(3003, "SilentTruncationSuspected");

    // ── Errors (4xxx) ────────────────────────────────────────────────────────
    public static readonly EventId UnexpectedError = new(4000, "UnexpectedError");
    public static readonly EventId InspectionHookFailed = new(4001, "InspectionHookFailed");
}

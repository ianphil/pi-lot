using System.Diagnostics;
using LlmSvc;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CopilotLlmProxy";
});

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "CopilotLlmProxy";
    settings.LogName = "CopilotLlmProxy";
});

builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.Logger.LogInformation(LogEvents.ServiceStarted, "CopilotLlmProxy service starting.");

// ── Load credential at startup ───────────────────────────────────────────────
var client = app.Services.GetRequiredService<CopilotClient>();
if (!client.TryLoadCredential())
{
    app.Logger.LogError(LogEvents.CredentialMissing, "Failed to load Copilot credential. Service will start but requests will fail.");
}

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", (CopilotClient c) => c.IsAuthenticated
    ? Results.Ok(new { status = "healthy", authenticated = true })
    : Results.Json(new { status = "degraded", authenticated = false }, statusCode: 503));

// ── GET /v1/models — OpenAI-compatible model list ────────────────────────────
app.MapGet("/v1/models", async (CopilotClient c) =>
{
    try
    {
        var models = await c.FetchModelsAsync();
        var response = new OpenAIModelListResponse
        {
            Data = models.Select(m => new OpenAIModelInfo
            {
                Id = m.Id,
                OwnedBy = "github-copilot",
                Name = m.Name,
                SupportedEndpoints = m.SupportedEndpoints,
            }).ToArray()
        };
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new OpenAIErrorResponse
            {
                Error = new OpenAIError { Message = ex.Message, Code = "upstream_error", Type = "error" }
            },
            statusCode: 502);
    }
});

// ── POST /v1/chat/completions — proxy to Copilot API ────────────────────────
app.MapPost("/v1/chat/completions", async (ChatCompletionRequest request, CopilotClient c) =>
{
    var (body, statusCode) = await c.ChatAsync(request);
    return Results.Text(body, "application/json", statusCode: statusCode);
});

app.Run();

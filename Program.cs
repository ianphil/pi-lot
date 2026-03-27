using LlmSvc;
using LlmSvc.Core.Models;
using LlmSvc.Core.Ports;
using LlmSvc.Core.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "CopilotLlmProxy";
    });

    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "CopilotLlmProxy";
        settings.LogName = "CopilotLlmProxy";
    });
}

builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<CopilotClient>());
builder.Services.AddSingleton<ChatCompletionsTranslator>();
builder.Services.AddSingleton<IResponsesService, ResponsesService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<Worker>();
}

var app = builder.Build();

app.Logger.LogInformation(LogEvents.ServiceStarted, "CopilotLlmProxy service starting.");

// ── Load credential at startup ───────────────────────────────────────────────
var provider = app.Services.GetRequiredService<IModelProvider>();
if (!app.Environment.IsEnvironment("Testing") && !provider.TryLoadCredential())
{
    app.Logger.LogError(LogEvents.CredentialMissing, "Failed to load Copilot credential. Service will start but requests will fail.");
}

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", (IModelProvider p) => p.IsAuthenticated
    ? Results.Ok(new { status = "healthy", authenticated = true })
    : Results.Json(new { status = "degraded", authenticated = false }, statusCode: 503));

// ── GET /v1/models — OpenAI-compatible model list ────────────────────────────
app.MapGet("/v1/models", GetModelsAsync);
app.MapGet("/models", GetModelsAsync);

// ── POST /v1/responses — unified responses surface ───────────────────────────
app.MapPost("/v1/responses", CreateResponseAsync);
app.MapPost("/responses", CreateResponseAsync);

// ── POST /v1/chat/completions — proxy to Copilot API ────────────────────────
app.MapPost("/v1/chat/completions", ProxyChatCompletionsAsync);
app.MapPost("/chat/completions", ProxyChatCompletionsAsync);

app.Run();

static async Task<IResult> GetModelsAsync(IModelProvider p, CancellationToken cancellationToken)
{
    try
    {
        var response = new OpenAIModelListResponse
        {
            Data = (await p.FetchModelsAsync(cancellationToken: cancellationToken))
                .Select(model => new
                {
                    Model = model,
                    ProxySupportedEndpoints = GetProxySupportedEndpoints(model),
                })
                .Where(entry => entry.ProxySupportedEndpoints.Length > 0)
                .Select(entry => new OpenAIModelInfo
                {
                    Id = entry.Model.Id,
                    OwnedBy = entry.Model.OwnedBy ?? "github-copilot",
                    Name = entry.Model.Name,
                    SupportedEndpoints = entry.Model.SupportedEndpoints,
                    ProxySupportedEndpoints = entry.ProxySupportedEndpoints,
                })
                .ToArray()
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
}

static async Task<IResult> CreateResponseAsync(CreateResponseRequest request, IResponsesService responsesService, CancellationToken cancellationToken) =>
    await responsesService.CreateAsync(request, cancellationToken);

static async Task<IResult> ProxyChatCompletionsAsync(ChatCompletionRequest request, CopilotClient c)
{
    var (body, statusCode) = await c.ChatAsync(request);
    return Results.Text(body, "application/json", statusCode: statusCode);
}

static string[] GetProxySupportedEndpoints(ModelDescriptor model) =>
    model.SupportsResponses || model.SupportsChatCompletions
        ? ["/v1/responses", "/v1/chat/completions"]
        : [];

public partial class Program;

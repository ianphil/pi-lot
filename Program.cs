using CopilotLlm.Core;
using CopilotLlm.Core.Models;
using CopilotLlm.Core.Ports;
using CopilotLlm.Core.Services;
using CopilotLlm.Infrastructure;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

if (!builder.Environment.IsEnvironment("Testing") && OperatingSystem.IsWindows())
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

builder.Services.AddHttpClient();
if (OperatingSystem.IsLinux())
{
    builder.Services.AddSingleton<CopilotCliConfigMetadataReader>();
    builder.Services.AddSingleton<ISecretServiceClient, SecretServiceDbusClient>();
}
builder.Services.AddSingleton<ICopilotCredentialStore>(static sp =>
{
    if (OperatingSystem.IsWindows())
    {
        return new WindowsCredentialStore();
    }

    if (OperatingSystem.IsLinux())
    {
        return ActivatorUtilities.CreateInstance<LinuxSecretServiceCredentialStore>(sp);
    }

    return new NoOpCopilotCredentialStore();
});
builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<CopilotClient>());
builder.Services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<CopilotClient>());
builder.Services.AddSingleton<ChatCompletionsTranslator>();
builder.Services.AddSingleton<ChatCompletionsStreamTranslator>();
builder.Services.AddSingleton<ModelListService>();
builder.Services.AddSingleton<IResponsesService, ResponsesService>();
builder.Services.AddSingleton<ResponsesStreamToChatTranslator>();
builder.Services.AddSingleton<IChatCompletionsService, ChatCompletionsService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<Worker>();
}

var app = builder.Build();

app.Logger.LogInformation(LogEvents.ServiceStarted, "CopilotLlmProxy service starting.");

// ── Load credential at startup ───────────────────────────────────────────────
var auth = app.Services.GetRequiredService<IAuthProvider>();
if (!app.Environment.IsEnvironment("Testing") && !auth.TryLoadCredential())
{
    app.Logger.LogError(LogEvents.CredentialMissing, "Failed to load Copilot credential. Service will start but requests will fail.");
}

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", (IAuthProvider a) => a.IsAuthenticated
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

static async Task<IResult> GetModelsAsync(ModelListService modelList, CancellationToken cancellationToken)
{
    try
    {
        var response = await modelList.GetModelsAsync(cancellationToken);
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
    new ResponseHttpResultAdapter(await responsesService.CreateAsync(request, cancellationToken));

static async Task<IResult> ProxyChatCompletionsAsync(ChatCompletionRequest request, IChatCompletionsService chatService, CancellationToken cancellationToken) =>
    new ResponseHttpResultAdapter(await chatService.CreateAsync(request, cancellationToken));

public partial class Program;

sealed class ResponseHttpResultAdapter(ResponseHttpResult result) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = result.StatusCode;
        httpContext.Response.ContentType = result.ContentType;

        if (result.Chunks is null)
        {
            if (!string.IsNullOrEmpty(result.Body))
            {
                await httpContext.Response.WriteAsync(result.Body, httpContext.RequestAborted);
            }

            return;
        }

        await foreach (var chunk in result.Chunks.WithCancellation(httpContext.RequestAborted))
        {
            await httpContext.Response.WriteAsync(chunk, httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
        }
    }
}

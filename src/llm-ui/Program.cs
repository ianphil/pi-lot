using LlmSdk;
using LlmSdk.Client;
using LlmSdk.Core.Models;
using llm_ui;
using llm_ui.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLlmSdk(options => options.DefaultModel = UiDefaults.DefaultModel);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", () => new UiConfig(UiDefaults.DefaultModel));

app.MapGet("/api/models", async (ILlmSdkClient client, CancellationToken cancellationToken) =>
{
    var models = await client.ListModelsAsync(cancellationToken);
    var responseModels = models
        .Where(static model => !string.IsNullOrWhiteSpace(model.Id))
        .Select(static model => new UiModel(model.Id!, model.Name ?? model.Id!))
        .OrderBy(static model => model.Id, StringComparer.Ordinal)
        .ToArray();

    if (!responseModels.Any(static model => string.Equals(model.Id, UiDefaults.DefaultModel, StringComparison.Ordinal)))
    {
        responseModels = [new UiModel(UiDefaults.DefaultModel, UiDefaults.DefaultModel), .. responseModels];
    }

    return new UiModelsResponse(UiDefaults.DefaultModel, responseModels);
});

app.MapPost("/api/chat", async Task<IResult> (
    UiChatRequest request,
    ILlmSdkClient client,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var conversation = ConversationMarkdownParser.Parse(request.ConversationMarkdown ?? string.Empty);

    if (!string.IsNullOrWhiteSpace(request.Message)
        && !conversation.Turns.Any(turn =>
            turn.Role is ConversationRole.User
            && string.Equals(turn.Text, request.Message.Trim(), StringComparison.Ordinal)))
    {
        conversation = conversation with
        {
            Turns = [.. conversation.Turns, new ConversationTurn(ConversationRole.User, request.Message.Trim())],
        };
    }

    if (conversation.Errors.Count != 0)
    {
        return Results.BadRequest(new UiErrorResponse(conversation.Errors));
    }

    if (!conversation.Turns.Any(static turn => turn.Role is ConversationRole.User))
    {
        return Results.BadRequest(new UiErrorResponse(["The conversation must contain at least one user message."]));
    }

    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";

    try
    {
        var context = conversation.ToAgentContext();
        var responseRequest = new CreateResponseRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model,
            Input = context.ToResponseInput(),
            Instructions = conversation.Instructions,
            Stream = true,
        };

        await foreach (var streamEvent in client.CreateResponseStreamAsync(responseRequest, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (streamEvent is OutputTextDeltaEvent delta)
            {
                await WriteSseAsync(httpContext.Response, new UiChatDelta("delta", delta.Delta), cancellationToken);
            }
        }

        await WriteSseAsync(httpContext.Response, new UiChatDone("done"), cancellationToken);
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
        await WriteSseAsync(httpContext.Response, new UiChatError("error", exception.Message), cancellationToken);
    }

    return Results.Empty;
});

app.MapFallbackToFile("index.html");

app.Run();

static async Task WriteSseAsync<T>(HttpResponse response, T payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload, JsonDefaults.Web);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public partial class Program;

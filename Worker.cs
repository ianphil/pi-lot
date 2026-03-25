namespace LlmSvc;

/// <summary>
/// Background service that periodically validates the Copilot token
/// and reloads credentials if needed.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly CopilotClient _client;
    private readonly ILogger<Worker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public Worker(CopilotClient client, ILogger<Worker> logger)
    {
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token validation worker started (interval: {Interval})", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                var valid = await _client.ValidateTokenAsync();
                if (valid)
                    _logger.LogDebug("Token validation succeeded");
                else
                    _logger.LogWarning("Token validation failed. Will retry next cycle.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation error");
            }
        }
    }
}

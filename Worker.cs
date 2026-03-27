using LlmSvc.Core.Ports;

namespace LlmSvc;

/// <summary>
/// Background service that periodically validates the Copilot token
/// and reloads credentials if needed.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IAuthProvider _auth;
    private readonly ILogger<Worker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public Worker(IAuthProvider auth, ILogger<Worker> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(LogEvents.ServiceStarted, "Token validation worker started (interval: {Interval})", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                var valid = await _auth.ValidateTokenAsync();
                if (valid)
                    _logger.LogDebug(LogEvents.TokenValidated, "Token validation succeeded");
                else
                    _logger.LogWarning(LogEvents.TokenValidationFailed, "Token validation failed. Will retry next cycle.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogEvents.UnexpectedError, ex, "Token validation error");
            }
        }
    }
}

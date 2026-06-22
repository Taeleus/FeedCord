using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FeedCord.Infrastructure.Workers;

public sealed class HealthHeartbeatWorker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromMinutes(2);
    private readonly string _heartbeatPath;
    private readonly ILogger<HealthHeartbeatWorker> _logger;

    public HealthHeartbeatWorker(ILogger<HealthHeartbeatWorker> logger)
    {
        _logger = logger;
        _heartbeatPath = GetHeartbeatPath();
    }

    public static bool IsHealthy(string? heartbeatPath = null, TimeSpan? maximumAge = null)
    {
        var path = heartbeatPath ?? GetHeartbeatPath();
        if (!File.Exists(path))
            return false;

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(path);
            return DateTimeOffset.UtcNow - lastWrite <= (maximumAge ?? DefaultMaximumAge);
        }
        catch
        {
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await File.WriteAllTextAsync(
                    _heartbeatPath,
                    DateTimeOffset.UtcNow.ToString("O"),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update health heartbeat at {Path}", _heartbeatPath);
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

    private static string GetHeartbeatPath()
    {
        return Environment.GetEnvironmentVariable("FEEDCORD_HEALTH_FILE")
            ?? Path.Combine(Path.GetTempPath(), "feedcord-health");
    }
}

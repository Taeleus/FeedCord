using FeedCord.Infrastructure.Workers;

namespace FeedCord.Tests;

public class HealthHeartbeatTests
{
    [Fact]
    public void IsHealthy_ReturnsFalse_WhenHeartbeatDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.False(HealthHeartbeatWorker.IsHealthy(path));
    }

    [Fact]
    public async Task IsHealthy_DistinguishesFreshAndStaleHeartbeat()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O"));
            Assert.True(HealthHeartbeatWorker.IsHealthy(path));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            Assert.False(HealthHeartbeatWorker.IsHealthy(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

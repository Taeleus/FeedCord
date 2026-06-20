using FeedCord.Common;
using FeedCord.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FeedCord.Tests;

public class JsonFeedStateStoreTests
{
    [Fact]
    public async Task SaveAsync_PreservesMultipleInstances_AndLoadsTheirCheckpoints()
    {
        var directory = Path.Combine(Path.GetTempPath(), "feedcord-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "feed-state.json");

        try
        {
            var store = new JsonFeedStateStore(new NullLogger<JsonFeedStateStore>(), filePath);
            var firstDate = DateTimeOffset.UtcNow.AddMinutes(-2);
            var secondDate = DateTimeOffset.UtcNow.AddMinutes(-1);

            await store.SaveAsync("first", new Dictionary<string, FeedState>
            {
                ["https://example.com/first"] = new()
                {
                    IsYoutube = false,
                    LastPublishDate = firstDate
                }
            });
            await store.SaveAsync("second", new Dictionary<string, FeedState>
            {
                ["https://example.com/second"] = new()
                {
                    IsYoutube = true,
                    LastPublishDate = secondDate
                }
            });

            Assert.Equal(
                firstDate,
                (await store.LoadAsync("first"))["https://example.com/first"].LastRunDate);
            Assert.Equal(
                secondDate,
                (await store.LoadAsync("second"))["https://example.com/second"].LastRunDate);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_PreservesCorruptFile_AndReturnsEmptyState()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "feed-state.json");

        try
        {
            await File.WriteAllTextAsync(filePath, "{ invalid json");
            var store = new JsonFeedStateStore(new NullLogger<JsonFeedStateStore>(), filePath);

            var state = await store.LoadAsync("test");

            Assert.Empty(state);
            Assert.Single(Directory.GetFiles(directory, "feed-state.json.corrupt-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_SerializesConcurrentWritersAcrossStoreInstances()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "feed-state.json");

        try
        {
            var firstStore = new JsonFeedStateStore(new NullLogger<JsonFeedStateStore>(), filePath);
            var secondStore = new JsonFeedStateStore(new NullLogger<JsonFeedStateStore>(), filePath);

            await Task.WhenAll(
                firstStore.SaveAsync("first", CreateState("https://example.com/first")),
                secondStore.SaveAsync("second", CreateState("https://example.com/second")));

            Assert.Single(await firstStore.LoadAsync("first"));
            Assert.Single(await firstStore.LoadAsync("second"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<string, FeedState> CreateState(string url)
    {
        return new Dictionary<string, FeedState>
        {
            [url] = new()
            {
                LastPublishDate = DateTimeOffset.UtcNow,
                ItemIdsAtLastPublishDate = new HashSet<string> { "item-id" }
            }
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "feedcord-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

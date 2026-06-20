using System.Text.Json;
using FeedCord.Common;
using FeedCord.Services.Interfaces;
using FeedCord.Helpers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FeedCord.Infrastructure.Persistence;

public sealed class JsonFeedStateStore : IFeedStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<JsonFeedStateStore> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate;

    public JsonFeedStateStore(ILogger<JsonFeedStateStore> logger)
        : this(logger, Path.Combine(AppContext.BaseDirectory, "state", "feed-state.json"))
    {
    }

    public JsonFeedStateStore(ILogger<JsonFeedStateStore> logger, string filePath)
    {
        _logger = logger;
        _filePath = Path.GetFullPath(filePath);
        _gate = FileGates.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<IReadOnlyDictionary<string, ReferencePost>> LoadAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                var legacyPaths = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "feed_dump.csv"),
                    Path.Combine(Environment.CurrentDirectory, "feed_dump.csv")
                };

                foreach (var legacyPath in legacyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var legacyState = CsvReader.LoadReferencePosts(legacyPath);
                    if (legacyState.Count == 0)
                        continue;

                    _logger.LogInformation(
                        "Loaded legacy feed state for {InstanceId} from {FilePath}; it will be migrated on the next save",
                        instanceId,
                        legacyPath);
                    return legacyState;
                }

                return new Dictionary<string, ReferencePost>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                var state = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ReferencePost>>>(json);

                return state?.GetValueOrDefault(instanceId)
                    ?? new Dictionary<string, ReferencePost>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load persisted feed state from {FilePath}", _filePath);
                await PreserveCorruptStateAsync(cancellationToken);
                return new Dictionary<string, ReferencePost>();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string instanceId,
        IReadOnlyDictionary<string, FeedState> states,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            Dictionary<string, Dictionary<string, ReferencePost>> allState = new();
            if (File.Exists(_filePath))
            {
                try
                {
                    var existingJson = await File.ReadAllTextAsync(_filePath, cancellationToken);
                    allState = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ReferencePost>>>(
                        existingJson) ?? new();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Existing state file is invalid; replacing it");
                }
            }

            allState[instanceId] = states.ToDictionary(
                pair => pair.Key,
                pair => new ReferencePost
                {
                    IsYoutube = pair.Value.IsYoutube,
                    LastRunDate = pair.Value.LastPublishDate.ToUniversalTime(),
                    ItemIdsAtLastRunDate = new HashSet<string>(
                        pair.Value.ItemIdsAtLastPublishDate,
                        StringComparer.Ordinal)
                });

            var temporaryPath = _filePath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(allState, JsonOptions),
                    cancellationToken);
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PreserveCorruptStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var corruptPath = $"{_filePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
            await using var source = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous);
            await using var destination = new FileStream(
                corruptPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preserve corrupt state file {FilePath}", _filePath);
        }
    }
}

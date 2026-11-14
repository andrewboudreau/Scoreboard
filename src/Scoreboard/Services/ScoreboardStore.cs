using System.Text.Json;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Scoreboard.Services;

/// <summary>
/// All scoreboard data lives in one blob folder: roster.json, current-game.json and games/{id}.json.
/// There are no groups or access codes; the folder is fixed per deployment.
/// </summary>
public sealed class ScoreboardStore(BlobContainerClient container, IConfiguration config, ILogger<ScoreboardStore> logger)
{
    private readonly SemaphoreSlim _prefixLock = new(1, 1);
    private string? _prefix;

    public async Task<BinaryData?> ReadAsync(string path)
    {
        try
        {
            var response = await Blob(await PrefixAsync(), path).DownloadContentAsync();
            return response.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Reads a blob by its full name, outside the data folder (for files written by older versions).</summary>
    public async Task<BinaryData?> ReadRootAsync(string name)
    {
        try { return (await container.GetBlobClient(name).DownloadContentAsync()).Value.Content; }
        catch (RequestFailedException e) when (e.Status == 404) { return null; }
    }

    public async Task WriteAsync(string path, BinaryData data) =>
        await Blob(await PrefixAsync(), path).UploadAsync(data, overwrite: true);

    /// <summary>Reads every saved game, in no particular order.</summary>
    public async Task<List<BinaryData>> ReadAllGamesAsync()
    {
        var prefix = await PrefixAsync();
        var names = new List<string>();
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{prefix}/games/", default))
        {
            names.Add(item.Name);
        }

        var games = await Task.WhenAll(names.Select(async name =>
        {
            try { return (await container.GetBlobClient(name).DownloadContentAsync()).Value.Content; }
            catch (RequestFailedException) { return null; }
        }));
        return [.. games.OfType<BinaryData>()];
    }

    /// <summary>Number of saved games (for the /info diagnostics endpoint).</summary>
    public async Task<int> CountGamesAsync()
    {
        var count = 0;
        await foreach (var _ in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{await PrefixAsync()}/games/", default)) count++;
        return count;
    }

    /// <summary>The blob folder all data is read from and written to.</summary>
    public Task<string> DataFolderAsync() => PrefixAsync();

    private BlobClient Blob(string prefix, string path) => container.GetBlobClient($"{prefix}/{path}");

    // The data folder is BlobStorage:DataFolder if set. Otherwise it is the folder of the old
    // group with the most games (so existing history carries over), or "main" on a fresh install.
    private async Task<string> PrefixAsync()
    {
        if (_prefix is not null) return _prefix;

        await _prefixLock.WaitAsync();
        try
        {
            _prefix ??= config["BlobStorage:DataFolder"] is { Length: > 0 } configured
                ? configured
                : await FindLegacyGroupFolderAsync() ?? "main";
            logger.LogInformation("Scoreboard data folder: {Folder}", _prefix);
            return _prefix;
        }
        finally
        {
            _prefixLock.Release();
        }
    }

    private async Task<string?> FindLegacyGroupFolderAsync()
    {
        string? best = null;
        var bestCount = 0;
        DateTimeOffset bestCreated = DateTimeOffset.MaxValue;

        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, "_groups/", default))
        {
            try
            {
                var json = (await container.GetBlobClient(item.Name).DownloadContentAsync()).Value.Content;
                using var doc = JsonDocument.Parse(json);
                var id = doc.RootElement.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var created = doc.RootElement.TryGetProperty("createdAt", out var c) && c.TryGetDateTimeOffset(out var d)
                    ? d : DateTimeOffset.MaxValue;

                var count = 0;
                await foreach (var _ in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{id}/games/", default)) count++;

                if (best is null || count > bestCount || (count == bestCount && created < bestCreated))
                {
                    (best, bestCount, bestCreated) = (id, count, created);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Skipping unreadable group blob {Name}", item.Name);
            }
        }

        return best;
    }
}

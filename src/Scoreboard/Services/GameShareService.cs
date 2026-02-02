using Azure.Storage.Blobs;

using Scoreboard.Models;

using System.Security.Cryptography;
using System.Text.Json;

namespace Scoreboard.Services;

/// <summary>
/// Creates and resolves share codes that map to game blobs.
/// </summary>
public interface IGameShareService
{
    /// <summary>
    /// Creates a share code for a specific game in a group.
    /// </summary>
    Task<GameShare> CreateShareAsync(string groupId, string gameId);

    /// <summary>
    /// Resolves a share code to a game share mapping.
    /// </summary>
    Task<GameShare?> GetShareAsync(string code);

    /// <summary>
    /// Reads the game JSON blob for a resolved share.
    /// </summary>
    Task<string?> GetGameJsonAsync(GameShare share);
}

/// <summary>
/// Stores share mappings at <c>_shares/{code}.json</c> in blob storage.
/// </summary>
public class GameShareService : IGameShareService
{
    private readonly BlobContainerClient _containerClient;

    private const string ShareBlobPrefix = "_shares/";
    // Same charset as admin codes in GroupService — excludes ambiguous chars
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GameShareService(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    public async Task<GameShare> CreateShareAsync(string groupId, string gameId)
    {
        var share = new GameShare
        {
            Code = GenerateCode(8),
            GroupId = groupId,
            GameId = gameId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var blobClient = _containerClient.GetBlobClient($"{ShareBlobPrefix}{share.Code}.json");
        var json = JsonSerializer.Serialize(share, JsonOptions);
        await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true);

        return share;
    }

    public async Task<GameShare?> GetShareAsync(string code)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient($"{ShareBlobPrefix}{code}.json");
            var response = await blobClient.DownloadContentAsync();
            return JsonSerializer.Deserialize<GameShare>(response.Value.Content);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetGameJsonAsync(GameShare share)
    {
        try
        {
            var blobPath = $"{share.GroupId}/games/{share.GameId}.json";
            var blobClient = _containerClient.GetBlobClient(blobPath);
            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateCode(int length)
    {
        return new string(Enumerable.Range(0, length)
            .Select(_ => CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)])
            .ToArray());
    }
}

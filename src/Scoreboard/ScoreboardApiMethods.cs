using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Scoreboard.Models;
using Scoreboard.Services;
using System.Text.Json;

namespace SharedTools.Scoreboard;

public class ScoreboardLogger { };

public static class ScoreboardApiMethods
{
    public static async Task<IResult> UploadHistory(IHttpContextAccessor contextAccessor, BlobContainerClient blobClient, ILogger<ScoreboardLogger> logger)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(contextAccessor.HttpContext);

            using MemoryStream? stream = await SafeReadSmallRequestBody(contextAccessor.HttpContext.Request);
            if (stream is null)
            {
                return Results.Problem(detail: "Invalid History Data.", statusCode: 400, title: "Bad Request");
            }

            // Validate that the body is valid JSON
            try
            {
                using var doc = JsonDocument.Parse(stream);
            }
            catch (JsonException)
            {
                return Results.Problem(detail: "Request body is not valid JSON.", statusCode: 400, title: "Bad Request");
            }

            stream.Position = 0;

            var filename = $"score-history-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.json";
            var blobClientInstance = blobClient.GetBlobClient(filename);

            await blobClientInstance.UploadAsync(stream, overwrite: true);

            return Results.Ok(new { filename });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading score history");
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Error uploading score history"
            );
        }
    }

    public static async Task<IResult> TestBlobClient(BlobContainerClient blobContainerClient, ILogger<ScoreboardLogger> logger)
    {
        try
        {
            var blobClient = blobContainerClient.GetBlobClient($"connection-test-{DateTime.Now.Ticks}.txt");
            await blobClient.UploadAsync(BinaryData.FromString("Connection test successful"), overwrite: true);
            return Results.Ok(new { message = "Connection to blob storage successful!" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error testing blob storage connection");
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Error testing blob storage connection"
            );
        }
    }

    public static async Task<IResult> GetDefaultPlayers(IDefaultPlayersService defaultPlayersService)
    {
        var defaultPlayers = await defaultPlayersService.GetDefaultPlayersAsync();
        return Results.Json(defaultPlayers);
    }

    public static async Task<IResult> MovePlayer(HttpContext httpContext, IDefaultPlayersService defaultPlayersService)
    {
        var request = await httpContext.Request.ReadFromJsonAsync<PlayerMoveRequest>();
        if (request is null) return Results.BadRequest(new { error = "Invalid request" });

        var success = await defaultPlayersService.MovePlayerAsync(request.Id, request.Team);
        return success ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Player not found" });
    }

    public static async Task<IResult> AddPlayer(HttpContext httpContext, IDefaultPlayersService defaultPlayersService)
    {
        var request = await httpContext.Request.ReadFromJsonAsync<PlayerAddRequest>();
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Player name is required" });

        var newPlayer = await defaultPlayersService.AddPlayerAsync(request.Name.Trim(), request.Team);
        return Results.Ok(newPlayer);
    }

    public static async Task<IResult> DeletePlayer(HttpContext httpContext, IDefaultPlayersService defaultPlayersService)
    {
        var request = await httpContext.Request.ReadFromJsonAsync<PlayerDeleteRequest>();
        if (request is null) return Results.BadRequest(new { error = "Invalid request" });

        var success = await defaultPlayersService.DeletePlayerAsync(request.Id);
        return success ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Player not found" });
    }

    public static async Task<IResult> SaveDefaultPlayers(HttpContext httpContext, IDefaultPlayersService defaultPlayersService)
    {
        var players = await httpContext.Request.ReadFromJsonAsync<List<Player>>();
        if (players is null) return Results.BadRequest(new { error = "Invalid request" });

        await defaultPlayersService.SaveDefaultPlayersAsync(players);
        return Results.Ok(new { success = true });
    }

    private static async Task<MemoryStream?> SafeReadSmallRequestBody(HttpRequest request)
    {
        var memoryStream = new MemoryStream();

        // First check Content-Length header if available
        if (request.ContentLength.HasValue && request.ContentLength.Value > 512 * 1024) // 500 KB limit
        {
            return null;
        }

        // If no Content-Length or within limits, read in chunks with size tracking
        byte[] buffer = new byte[64 * 1024]; // 64KB chunks
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await request.Body.ReadAsync(buffer)) > 0)
        {
            totalBytesRead += bytesRead;

            // Check size limit during streaming
            if (totalBytesRead > 512 * 1024) // 500 KB limit
            {
                return null;
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        memoryStream.Position = 0;
        return memoryStream;
    }
}

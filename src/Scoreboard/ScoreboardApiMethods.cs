using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

using Scoreboard.Models;
using Scoreboard.Services;

namespace SharedTools.Scoreboard;

public static class ScoreboardApiMethods
{
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

    public static async Task<IResult> UploadPlayerImage(HttpContext httpContext, long id, IDefaultPlayersService defaultPlayersService, BlobContainerClient container)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart form data" });

        var form = await httpContext.Request.ReadFormAsync();
        var file = form.Files.GetFile("image");
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "No image file provided" });

        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { error = "File too large (max 10MB)" });

        try
        {
            using var inputStream = file.OpenReadStream();
            using var image = await Image.LoadAsync(inputStream);

            // Crop to center square
            var size = Math.Min(image.Width, image.Height);
            var cropX = (image.Width - size) / 2;
            var cropY = (image.Height - size) / 2;
            image.Mutate(x => x.Crop(new Rectangle(cropX, cropY, size, size)).Resize(96, 96));

            // Encode to PNG
            using var outputStream = new MemoryStream();
            await image.SaveAsPngAsync(outputStream);
            outputStream.Position = 0;

            // Upload to blob storage
            var blobName = $"_players/{id}.png";
            var blobClient = container.GetBlobClient(blobName);
            await blobClient.UploadAsync(outputStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" }
            });

            // Build the image URL
            var imageUrl = blobClient.Uri.ToString();

            // Update the player's ImageUrl
            await defaultPlayersService.UpdatePlayerImageAsync(id, imageUrl);

            return Results.Ok(new { imageUrl });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to process image: {ex.Message}");
        }
    }
}

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Scoreboard.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace SharedTools.Scoreboard;

/// <summary>
/// The whole server API. The browser reads and writes JSON documents; the server just stores them.
///   roster.json        { team1Name, team2Name, players: [...] }
///   current-game.json  { gameId }
///   games/{id}.json    full game state
/// </summary>
public static partial class ScoreboardApi
{
    private const string Json = "application/json";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/Scoreboard/api");

        api.MapGet("/info", GetInfo);

        api.MapGet("/roster", GetRoster);
        api.MapPut("/roster", (HttpRequest req, ScoreboardStore store) => SaveJson(req, store, "roster.json"));

        api.MapGet("/current-game", GetCurrentGame);
        api.MapPut("/current-game", (HttpRequest req, ScoreboardStore store) => SaveJson(req, store, "current-game.json"));

        api.MapGet("/games", GetGames);
        api.MapGet("/games/{id}", GetGame);
        api.MapPut("/games/{id}", (string id, HttpRequest req, ScoreboardStore store) =>
            ValidId(id) ? SaveJson(req, store, $"games/{id}.json") : Task.FromResult(Results.BadRequest()));

        api.MapPost("/players/{id:long}/image", UploadPlayerImage).DisableAntiforgery();
        api.MapGet("/players/{id:long}/image", GetPlayerImage);
        api.MapGet("/default-players/{id:long}/image", GetPlayerImage); // image URLs saved by older versions
    }

    // Read-only diagnostics: which folder is in use and how much is in it.
    private static async Task<IResult> GetInfo(ScoreboardStore store) => Results.Json(new
    {
        version = typeof(ScoreboardApi).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],
        dataFolder = await store.DataFolderAsync(),
        games = await store.CountGamesAsync(),
        hasRoster = await store.ReadAsync("roster.json") is not null
    });

    private static async Task<IResult> GetRoster(ScoreboardStore store)
    {
        var roster = await store.ReadAsync("roster.json");
        if (roster is not null) return Results.Bytes(roster.ToArray(), Json);

        // First run: seed from the bundled player list. "seeded" tells the browser nothing is stored yet,
        // so a device holding a roster from an older version can upload it.
        var seed = typeof(ScoreboardApi).Assembly.GetManifestResourceStream(
            $"{typeof(ScoreboardApi).Assembly.GetName().Name}.wwwroot.default-players.json");
        var players = seed is null ? [] : JsonSerializer.Deserialize<JsonElement[]>(seed) ?? [];
        return Results.Json(new { team1Name = "BLACK", team2Name = "WHITE", players, seeded = true });
    }

    private static async Task<IResult> GetCurrentGame(ScoreboardStore store)
    {
        var pointer = await store.ReadAsync("current-game.json")
            ?? await store.ReadAsync("active-game.json"); // name used by older versions
        if (pointer is null) return Results.NoContent();

        var gameId = JsonDocument.Parse(pointer).RootElement.TryGetProperty("gameId", out var g) ? g.GetString() : null;
        if (gameId is null || !ValidId(gameId)) return Results.NoContent();

        var game = await store.ReadAsync($"games/{gameId}.json");
        return game is null ? Results.NoContent() : Results.Bytes(game.ToArray(), Json);
    }

    private static async Task<IResult> GetGames(ScoreboardStore store)
    {
        var games = await store.ReadAllGamesAsync();
        // Stitch the stored documents into one JSON array without re-serializing them.
        var body = "[" + string.Join(",", games.Select(g => g.ToString())) + "]";
        return Results.Content(body, Json);
    }

    private static async Task<IResult> GetGame(string id, ScoreboardStore store)
    {
        if (!ValidId(id)) return Results.BadRequest();
        var game = await store.ReadAsync($"games/{id}.json");
        return game is null ? Results.NotFound() : Results.Bytes(game.ToArray(), Json);
    }

    // A full game with every basket tracked is ~50KB; anything far beyond that is not ours.
    private const int MaxJsonBytes = 1024 * 1024;

    private static async Task<IResult> SaveJson(HttpRequest request, ScoreboardStore store, string path)
    {
        if (request.ContentLength > MaxJsonBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var data = await BinaryData.FromStreamAsync(request.Body);
        if (data.ToMemory().Length > MaxJsonBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        try
        {
            using var _ = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Body must be JSON" });
        }

        await store.WriteAsync(path, data);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadPlayerImage(long id, IFormFile image, ScoreboardStore store)
    {
        if (image.Length == 0 || image.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { error = "Image must be under 10MB" });

        try
        {
            using var img = await Image.LoadAsync(image.OpenReadStream());
            var size = Math.Min(img.Width, img.Height);
            img.Mutate(x => x
                .AutoOrient()
                .Crop(new Rectangle((img.Width - size) / 2, (img.Height - size) / 2, size, size))
                .Resize(96, 96));

            using var png = new MemoryStream();
            await img.SaveAsPngAsync(png);
            await store.WriteAsync($"players/{id}.png", new BinaryData(png.ToArray()));

            // Cache-bust so a replaced photo shows up immediately.
            return Results.Ok(new { imageUrl = $"/Scoreboard/api/players/{id}/image?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Could not read image: {ex.Message}" });
        }
    }

    private static async Task<IResult> GetPlayerImage(long id, ScoreboardStore store)
    {
        var png = await store.ReadAsync($"players/{id}.png") ?? await store.ReadRootAsync($"_players/{id}.png");
        return png is null ? Results.NotFound() : Results.Bytes(png.ToArray(), "image/png");
    }

    private static bool ValidId(string id) => GameId().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,80}$")]
    private static partial Regex GameId();
}

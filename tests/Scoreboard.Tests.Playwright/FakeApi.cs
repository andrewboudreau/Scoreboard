using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Scoreboard.Tests.Playwright;

/// <summary>
/// In-memory stand-in for /Scoreboard/api so tests never read or write the real store.
/// Documents the page PUTs are served back on later GETs, so the app's periodic
/// server refresh sees its own saves.
/// </summary>
public partial class FakeApi
{
    private const string EmptyRoster = """{"team1Name":"BLACK","team2Name":"WHITE","players":[]}""";

    public Dictionary<string, string> Docs { get; } = new();

    public void AddGame(string json)
    {
        var id = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;
        Docs[$"games/{id}"] = json;
    }

    public Task AttachAsync(IPage page) => page.RouteAsync("**/Scoreboard/api/**", HandleAsync);

    private async Task HandleAsync(IRoute route)
    {
        var request = route.Request;
        var path = new Uri(request.Url).AbsolutePath;
        path = path[(path.IndexOf("/api/", StringComparison.Ordinal) + 5)..];

        if (request.Method == "PUT")
        {
            Docs[path] = request.PostData ?? "";
            await route.FulfillAsync(new() { Status = 204 });
            return;
        }

        if (request.Method != "GET")
        {
            await route.FulfillAsync(new() { Status = 404 });
            return;
        }

        switch (path)
        {
            case "roster":
                await Json(route, Docs.GetValueOrDefault("roster") ?? EmptyRoster);
                return;
            case "current-game":
                var gameId = Docs.TryGetValue("current-game", out var pointer)
                    ? JsonDocument.Parse(pointer).RootElement.GetProperty("gameId").GetString()
                    : null;
                if (gameId is not null && Docs.TryGetValue($"games/{gameId}", out var current))
                    await Json(route, current);
                else
                    await route.FulfillAsync(new() { Status = 204 });
                return;
            case "hidden-games":
                await Json(route, Docs.GetValueOrDefault("hidden-games") ?? """{"ids":[]}""");
                return;
            case "games":
                var games = Docs.Where(d => GameKey().IsMatch(d.Key)).Select(d => d.Value);
                await Json(route, "[" + string.Join(",", games) + "]");
                return;
        }

        if (GameKey().IsMatch(path) && Docs.TryGetValue(path, out var game))
            await Json(route, game);
        else
            await route.FulfillAsync(new() { Status = 404 });
    }

    private static Task Json(IRoute route, string body) =>
        route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });

    [GeneratedRegex(@"^games/[^/]+$")]
    private static partial Regex GameKey();
}

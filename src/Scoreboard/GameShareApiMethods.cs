using Microsoft.AspNetCore.Http;

using Scoreboard.Models;
using Scoreboard.Services;

namespace SharedTools.Scoreboard;

public static class GameShareApiMethods
{
    public static async Task<IResult> CreateShare(HttpContext httpContext, IGameShareService shareService)
    {
        var request = await httpContext.Request.ReadFromJsonAsync<ShareGameRequest>();
        if (request is null || string.IsNullOrWhiteSpace(request.GroupId) || string.IsNullOrWhiteSpace(request.GameId))
        {
            return Results.BadRequest(new { error = "groupId and gameId are required" });
        }

        var share = await shareService.CreateShareAsync(request.GroupId, request.GameId);
        var shareUrl = $"/Scoreboard/game?s={share.Code}";

        return Results.Ok(new { shareCode = share.Code, shareUrl });
    }

    public static async Task<IResult> GetShare(string code, IGameShareService shareService)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "Share code is required" });
        }

        var share = await shareService.GetShareAsync(code);
        if (share is null)
        {
            return Results.NotFound(new { error = "Share not found" });
        }

        var gameJson = await shareService.GetGameJsonAsync(share);
        if (gameJson is null)
        {
            return Results.NotFound(new { error = "Game data not found" });
        }

        return Results.Content(gameJson, "application/json");
    }
}

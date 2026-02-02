using Microsoft.AspNetCore.Http;

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
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Scoreboard.Models;
using Scoreboard.Services;

namespace Scoreboard.Pages;

public class ManagePlayersModel : PageModel
{
    private readonly IDefaultPlayersService defaultPlayersService;

    public ManagePlayersModel(IDefaultPlayersService defaultPlayersService)
    {
        this.defaultPlayersService = defaultPlayersService;
    }

    public List<Player> AllPlayers { get; set; } = new();
    public List<Player> Team1Players => [.. AllPlayers.Where(p => p.Team == "1")];
    public List<Player> Team2Players => [.. AllPlayers.Where(p => p.Team == "2")];
    public List<Player> NoTeamPlayers => [.. AllPlayers.Where(p => p.Team == "noteam")];

    public async Task OnGetAsync()
    {
        AllPlayers = await defaultPlayersService.GetDefaultPlayersAsync();
    }

    public async Task<IActionResult> OnPostMovePlayerAsync([FromBody] PlayerMoveRequest request)
    {
        var success = await defaultPlayersService.MovePlayerAsync(request.Id, request.Team);
        if (success)
        {
            return new JsonResult(new { success = true });
        }
        return BadRequest(new { success = false, message = "Player not found" });
    }

    public async Task<IActionResult> OnPostAddPlayerAsync([FromBody] PlayerAddRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { success = false, message = "Player name is required" });
        }

        var newPlayer = await defaultPlayersService.AddPlayerAsync(request.Name.Trim(), request.Team);
        return new JsonResult(newPlayer);
    }

    public async Task<IActionResult> OnPostDeletePlayerAsync([FromBody] PlayerDeleteRequest request)
    {
        var success = await defaultPlayersService.DeletePlayerAsync(request.Id);
        if (success)
        {
            return new JsonResult(new { success = true });
        }
        return BadRequest(new { success = false, message = "Player not found" });
    }

    public async Task<IActionResult> OnPostSavePlayersAsync([FromBody] List<Player> players)
    {
        try
        {
            await defaultPlayersService.SaveDefaultPlayersAsync(players);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
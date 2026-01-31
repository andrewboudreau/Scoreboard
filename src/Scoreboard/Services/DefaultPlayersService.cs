using Scoreboard.Models;
using System.Text.Json;
using System.Threading; // Added for System.Threading.Lock

namespace Scoreboard.Services;

public interface IDefaultPlayersService
{
    Task<List<Player>> GetDefaultPlayersAsync();
    Task SaveDefaultPlayersAsync(List<Player> players);
    Task<Player> AddPlayerAsync(string name, string team);
    Task<bool> MovePlayerAsync(long id, string team);
    Task<bool> DeletePlayerAsync(long id);
}

public class DefaultPlayersService : IDefaultPlayersService
{
    private readonly List<Player> defaultPlayers;
    private readonly Lock @lock = new(); // IDE0090: Simplified 'new' expression and IDE0330: Use 'System.Threading.Lock'

    public DefaultPlayersService()
    {
        // Initialize with the embedded default players
        defaultPlayers = GetInitialDefaultPlayers();
    }

    public Task<List<Player>> GetDefaultPlayersAsync()
    {
        using (@lock.EnterScope())
        {
            return Task.FromResult(defaultPlayers.Select(p => new Player
            {
                Id = p.Id,
                Name = p.Name,
                Team = p.Team,
                Active = p.Active,
                Points = p.Points
            }).ToList());
        }
    }

    public Task SaveDefaultPlayersAsync(List<Player> players)
    {
        using (@lock.EnterScope())
        {
            defaultPlayers.Clear();
            defaultPlayers.AddRange(players);
        }
        return Task.CompletedTask;
    }

    public Task<Player> AddPlayerAsync(string name, string team)
    {
        var newPlayer = new Player
        {
            Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = name,
            Team = team,
            Active = true,
            Points = 0
        };

        using (@lock.EnterScope())
        {
            defaultPlayers.Add(newPlayer);
        }

        return Task.FromResult(newPlayer);
    }

    public Task<bool> MovePlayerAsync(long id, string team)
    {
        using (@lock.EnterScope())
        {
            var player = defaultPlayers.FirstOrDefault(p => p.Id == id);
            if (player != null)
            {
                player.Team = team;
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task<bool> DeletePlayerAsync(long id)
    {
        using (@lock.EnterScope())
        {
            var player = defaultPlayers.FirstOrDefault(p => p.Id == id);
            if (player != null)
            {
                defaultPlayers.Remove(player);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    private static List<Player> GetInitialDefaultPlayers()
    {
        // Load from embedded resource or provide initial default players
        return
        [
            new() { Id = 1742075931014, Name = "Andrew", Team = "2", Active = true, Points = 0 },
            new() { Id = 1742075931914, Name = "Seth", Team = "2", Active = true, Points = 0 },
            new() { Id = 1742075933790, Name = "Jason", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742075935050, Name = "Nate", Team = "2", Active = true, Points = 0 },
            new() { Id = 1742075939280, Name = "Joe", Team = "1", Active = false, Points = 0 },
            new() { Id = 1742075941065, Name = "Ryan", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742075943344, Name = "JD", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742075954745, Name = "Frank", Team = "2", Active = true, Points = 0 },
            new() { Id = 1742075979391, Name = "Ricardo", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742075987612, Name = "Nick", Team = "2", Active = true, Points = 0 },
            new() { Id = 1742076001247, Name = "Loukas", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742076029522, Name = "Adam", Team = "2", Active = false, Points = 0 },
            new() { Id = 1742267332075, Name = "Rodney", Team = "1", Active = true, Points = 0 },
            new() { Id = 1742267457728, Name = "Mark", Team = "2", Active = true, Points = 0 }
        ];
    }
}
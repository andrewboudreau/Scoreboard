using System.Text.Json.Serialization;

namespace Scoreboard.Models;

/// <summary>
/// Mapping from a share code to a specific game blob.
/// </summary>
public class GameShare
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Request body for creating a game share link.
/// </summary>
public class ShareGameRequest
{
    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;
}

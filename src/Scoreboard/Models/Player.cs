using System.Text.Json.Serialization;

namespace Scoreboard.Models;

public class Player
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("team")]
    public string Team { get; set; } = "1"; // "1", "2", or "noteam"
    
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
    
    [JsonPropertyName("points")]
    public int Points { get; set; } = 0;
}

public class PlayerMoveRequest
{
    public long Id { get; set; }
    public string Team { get; set; } = string.Empty;
}

public class PlayerAddRequest
{
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = "noteam";
}

public class PlayerDeleteRequest
{
    public long Id { get; set; }
}
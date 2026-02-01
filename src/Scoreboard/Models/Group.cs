using System.Text.Json.Serialization;

namespace Scoreboard.Models;

public class Group
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("adminCode")]
    public string AdminCode { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("members")]
    public List<MemberAccess> Members { get; set; } = [];
}

public class MemberAccess
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

public class SasTokenSet
{
    [JsonPropertyName("readUrl")]
    public string ReadUrl { get; set; } = string.Empty;

    [JsonPropertyName("writeUrl")]
    public string? WriteUrl { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public class CreateGroupRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class AddMemberRequest
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

using System.Text.Json.Serialization;

namespace Scoreboard.Models;

/// <summary>
/// Group metadata persisted to blob storage.
/// </summary>
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

/// <summary>
/// Represents a member access code that can be activated/revoked.
/// </summary>
public class MemberAccess
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

/// <summary>
/// Container SAS URLs used by the client to access group blobs.
/// </summary>
public class SasTokenSet
{
    [JsonPropertyName("readUrl")]
    public string ReadUrl { get; set; } = string.Empty;

    [JsonPropertyName("writeUrl")]
    public string? WriteUrl { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Request body for creating a new group.
/// </summary>
public class CreateGroupRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Request body for adding a member to a group.
/// </summary>
public class AddMemberRequest
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

namespace SharedTools.Scoreboard;

/// <summary>
/// Settings from the module's own section, <c>Modules:Scoreboard</c>
/// (environment: <c>Modules__Scoreboard__ConnectionString</c> and so on).
/// The old root <c>BlobStorage:*</c> keys still work as a fallback.
/// </summary>
public sealed class ScoreboardOptions
{
    /// <summary>Azure Blob Storage connection string.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Blob container holding all scoreboard data (prod: <c>scoreboard</c>).</summary>
    public string? ContainerName { get; set; }

    /// <summary>Optional data folder inside the container. Unset means auto-detect (see ScoreboardStore).</summary>
    public string? DataFolder { get; set; }
}

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

    // ---- Photo page (/Scoreboard/photo): AI styles for player photos ----

    /// <summary>OpenAI API key for the AI photo styles. Unset: the photo page still works, without styles. Secret.</summary>
    public string? OpenAIKey { get; set; }

    /// <summary>OpenAI image model used for styles.</summary>
    public string OpenAIImageModel { get; set; } = "gpt-image-2";

    /// <summary>low | medium | high. Low is fastest and cheapest, plenty for a small avatar.</summary>
    public string OpenAIImageQuality { get; set; } = "low";

    /// <summary>Override for tests or a proxy.</summary>
    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>When set, AI styles only work from a link carrying <c>?k=&lt;code&gt;</c> (the link you post in the chat).</summary>
    public string? PhotoLinkCode { get; set; }

    /// <summary>Key for the usage page (/Scoreboard/photo/usage). Unset: the usage page is off. Secret.</summary>
    public string? PhotoAdminKey { get; set; }

    /// <summary>AI style attempts per phone per day (UTC). An IP address gets twice this.</summary>
    public int PhotoDailyLimitPerDevice { get; set; } = 30;

    /// <summary>AI style attempts per day across everyone: the cost ceiling.</summary>
    public int PhotoDailyLimitTotal { get; set; } = 300;
}

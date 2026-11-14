using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Scoreboard.Services;

using SharedTools.Web.Modules;

namespace SharedTools.Scoreboard;

public class ScoreboardModule : IApplicationPartModule
{
    public string Name => "Scoreboard";

    // The module's wwwroot is embedded in this assembly; HTML pages are served from here.
    private static readonly string PagePrefix =
        $"{typeof(ScoreboardModule).Assembly.GetName().Name}.wwwroot.";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration["BlobStorage:ConnectionString"];
            var containerName = configuration["BlobStorage:ContainerName"];

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("Blob storage connection string or container name is not configured.");
            }

            var container = new BlobContainerClient(connectionString, containerName);
            container.CreateIfNotExists();
            return container;
        });

        services.AddSingleton<ScoreboardStore>();
    }

    public void Configure(WebApplication app)
    {
        app.MapGet("/Scoreboard/", () => Page("index.html"));
        app.MapGet("/Scoreboard/docs", () => Page("docs.html"));
        app.MapGet("/Scoreboard/game", () => Page("game.html"));
        app.MapGet("/Scoreboard/stats", () => Page("stats.html"));
        // The team builder now lives on the scoreboard itself.
        app.MapGet("/Scoreboard/ManagePlayers", () => Results.Redirect("/Scoreboard/#teams"));

        ScoreboardApi.Map(app);
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
    }

    // Changes with every build, so phones pick up new app.js/styles.css instead of cached copies.
    private static readonly string BuildVersion =
        typeof(ScoreboardModule).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];

    // Serves an embedded HTML page from the module's wwwroot, or 404 if it is missing.
    private static IResult Page(string fileName)
    {
        using var stream = typeof(ScoreboardModule).Assembly.GetManifestResourceStream(PagePrefix + fileName);
        if (stream is null) return Results.NotFound();

        var html = new StreamReader(stream).ReadToEnd().Replace("{{v}}", BuildVersion);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}

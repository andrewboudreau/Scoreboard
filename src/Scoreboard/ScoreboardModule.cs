using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

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
        // Add HttpContextAccessor for the API methods
        services.AddHttpContextAccessor();

        // Add Azure Blob Storage
        services.AddSingleton<BlobContainerClient>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration["BlobStorage:ConnectionString"];
            var containerName = configuration["BlobStorage:ContainerName"];

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("Blob storage connection string or container name is not configured.");
            }

            var blobServiceClient = new BlobServiceClient(connectionString);

            // Configure CORS to allow the SPA to make direct blob requests
            try
            {
                var properties = blobServiceClient.GetProperties().Value;
                var hasWildcardCors = properties.Cors?.Any(r => r.AllowedOrigins == "*") ?? false;
                if (!hasWildcardCors)
                {
                    properties.Cors ??= [];
                    properties.Cors.Add(new BlobCorsRule
                    {
                        AllowedOrigins = "*",
                        AllowedMethods = "GET,PUT,POST,DELETE,HEAD,OPTIONS",
                        AllowedHeaders = "*",
                        ExposedHeaders = "*",
                        MaxAgeInSeconds = 3600
                    });
                    blobServiceClient.SetProperties(properties);
                }
            }
            catch { /* Non-fatal: CORS setup may fail in some environments */ }

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Create the container if it doesn't exist
            containerClient.CreateIfNotExists();
            return containerClient;
        });

        // Register the default players service as singleton so it persists across requests
        services.AddSingleton<IDefaultPlayersService, DefaultPlayersService>();

        // Register the group service for shared state management
        services.AddSingleton<IGroupService, GroupService>();

        // Register the game share service
        services.AddSingleton<IGameShareService, GameShareService>();
    }

    public void Configure(WebApplication app)
    {
        // Serve the module's HTML pages from the embedded wwwroot.
        app.MapGet("/Scoreboard/", () => Page("index.html"));
        app.MapGet("/Scoreboard/docs", () => Page("docs.html"));
        app.MapGet("/Scoreboard/ManagePlayers", () => Page("manage-players.html"));
        app.MapGet("/Scoreboard/game", () => Page("game.html"));
        app.MapGet("/Scoreboard/stats", () => Page("stats.html"));

        // Game share endpoints
        app.MapPost("/Scoreboard/api/games/share", GameShareApiMethods.CreateShare);
        app.MapGet("/Scoreboard/api/shares/{code}", GameShareApiMethods.GetShare);

        // Player management endpoints
        app.MapGet("/Scoreboard/api/default-players", ScoreboardApiMethods.GetDefaultPlayers);

        // Player management endpoints
        app.MapPost("/Scoreboard/api/default-players/move", ScoreboardApiMethods.MovePlayer);
        app.MapPost("/Scoreboard/api/default-players/add", ScoreboardApiMethods.AddPlayer);
        app.MapPost("/Scoreboard/api/default-players/delete", ScoreboardApiMethods.DeletePlayer);
        app.MapPost("/Scoreboard/api/default-players/save", ScoreboardApiMethods.SaveDefaultPlayers);
        app.MapPost("/Scoreboard/api/default-players/toggle-active", ScoreboardApiMethods.TogglePlayerActive);
        app.MapPost("/Scoreboard/api/default-players/rename", ScoreboardApiMethods.RenamePlayer);
        app.MapGet("/Scoreboard/api/default-players/team-names", ScoreboardApiMethods.GetTeamNames);
        app.MapPost("/Scoreboard/api/default-players/team-names", ScoreboardApiMethods.SaveTeamNames);
        app.MapPost("/Scoreboard/api/default-players/{id}/image", ScoreboardApiMethods.UploadPlayerImage);
        app.MapGet("/Scoreboard/api/default-players/{id}/image", ScoreboardApiMethods.GetPlayerImage);

        // Group management endpoints
        app.MapPost("/Scoreboard/api/groups", GroupApiMethods.CreateGroup);
        app.MapGet("/Scoreboard/api/groups/join", GroupApiMethods.JoinGroup);
        app.MapPost("/Scoreboard/api/groups/{id}/members", GroupApiMethods.AddMember);
        app.MapDelete("/Scoreboard/api/groups/{id}/members/{code}", GroupApiMethods.RevokeMember);
        app.MapGet("/Scoreboard/api/groups/{id}/sas/refresh", GroupApiMethods.RefreshSas);
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Usually empty - the framework handles this
    }

    // Serves an embedded HTML page from the module's wwwroot, or 404 if it is missing.
    private static IResult Page(string fileName)
    {
        var stream = typeof(ScoreboardModule).Assembly.GetManifestResourceStream(PagePrefix + fileName);
        return stream is not null
            ? Results.Stream(stream, "text/html; charset=utf-8")
            : Results.NotFound();
    }
}
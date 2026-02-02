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

    private static string htmlIndexContent = "";
    private static string htmlDocsContent = "";
    private static string htmlManagePlayersContent = "";
    private static string htmlGameContent = "";
    private static string htmlStatsContent = "";

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
        // Map the module's home page
        // map to the embedded resource directly as content
        app.MapGet("/Scoreboard/", () =>
        {
            if (string.IsNullOrEmpty(htmlIndexContent))
            {
                var assembly = typeof(ScoreboardModule).Assembly;
                var resourceName = "SharedTools.Scoreboard.wwwroot.index.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    return Results.Problem("Scoreboard home page not found.", statusCode: 404);
                }
                using var reader = new StreamReader(stream);
                htmlIndexContent = reader.ReadToEnd();
            }

            return Results.Content(htmlIndexContent, "text/html");
        });

        app.MapGet("/Scoreboard/docs", () =>
        {
            if (string.IsNullOrEmpty(htmlDocsContent))
            {
                var assembly = typeof(ScoreboardModule).Assembly;
                var resourceName = "SharedTools.Scoreboard.wwwroot.docs.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    return Results.Problem("Scoreboard docs page not found.", statusCode: 404);
                }
                using var reader = new StreamReader(stream);
                htmlDocsContent = reader.ReadToEnd();
            }

            return Results.Content(htmlDocsContent, "text/html");
        });

        app.MapGet("/Scoreboard/ManagePlayers", () =>
        {
            if (string.IsNullOrEmpty(htmlManagePlayersContent))
            {
                var assembly = typeof(ScoreboardModule).Assembly;
                var resourceName = "SharedTools.Scoreboard.wwwroot.manage-players.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    return Results.Problem("Manage Players page not found.", statusCode: 404);
                }
                using var reader = new StreamReader(stream);
                htmlManagePlayersContent = reader.ReadToEnd();
            }

            return Results.Content(htmlManagePlayersContent, "text/html");
        });

        // Shared game results page
        app.MapGet("/Scoreboard/game", () =>
        {
            if (string.IsNullOrEmpty(htmlGameContent))
            {
                var assembly = typeof(ScoreboardModule).Assembly;
                var resourceName = "SharedTools.Scoreboard.wwwroot.game.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    return Results.Problem("Game results page not found.", statusCode: 404);
                }
                using var reader = new StreamReader(stream);
                htmlGameContent = reader.ReadToEnd();
            }

            return Results.Content(htmlGameContent, "text/html");
        });

        // Stats / game history page
        app.MapGet("/Scoreboard/stats", () =>
        {
            if (string.IsNullOrEmpty(htmlStatsContent))
            {
                var assembly = typeof(ScoreboardModule).Assembly;
                var resourceName = "SharedTools.Scoreboard.wwwroot.stats.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    return Results.Problem("Stats page not found.", statusCode: 404);
                }
                using var reader = new StreamReader(stream);
                htmlStatsContent = reader.ReadToEnd();
            }

            return Results.Content(htmlStatsContent, "text/html");
        });

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
}
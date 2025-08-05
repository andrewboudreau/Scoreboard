using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SharedTools.Web.Modules;

using System.Threading.RateLimiting;

namespace Scoreboard;

public class ScoreboardModule : IApplicationPartModule
{
    public string Name => "Scoreboard";

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

        // Add Rate Limiter
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("ScoreboardEndpoints", httpContext =>
            {
                // Only apply rate limiting for paths that start with "/Scoreboard"
                if (httpContext.Request.Path.StartsWithSegments("/Scoreboard"))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 6,
                            Window = TimeSpan.FromMinutes(1)
                        });
                }

                // Skip rate limiting for other paths
                return RateLimitPartition.GetNoLimiter("default");
            });

            options.RejectionStatusCode = 429;
        });
    }

    public void Configure(WebApplication app)
    {
        app.UseRateLimiter();

        // Map the module's home page
        app.MapGet("/Scoreboard/", () => Results.Redirect("/_content/Scoreboard/index.html"));
        app.MapGet("/Scoreboard.App/", () => Results.Redirect("/Scoreboard/"));

        // Map API endpoints with module prefix
        app.MapPost("/Scoreboard/api/upload-history", ScoreboardApiMethods.UploadHistory)
            .RequireRateLimiting("ScoreboardEndpoints");

        app.MapGet("/Scoreboard/api/test-blob-connection", ScoreboardApiMethods.TestBlobClient)
            .RequireRateLimiting("ScoreboardEndpoints");
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Usually empty - the framework handles this
    }
}
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public static class ServiceExtensions
{
    public static void AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(x =>
        {
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
    }

    public static void AddRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 6,
                        Window = TimeSpan.FromMinutes(1)
                    }));
            options.RejectionStatusCode = 429;
        });
    }
}

public static class ApplicationBuilderExtensions
{
    public static void MapScoreboardEndpoints(this IApplicationBuilder app)
    {
        app.MapPost("/scoreboard/upload-history", async (HttpContext context, BlobContainerClient blobContainerClient) =>
        {
            try
            {
                // Read the request body
                using var reader = new StreamReader(context.Request.Body);
                var json = await reader.ReadToEndAsync();

                // Validate JSON
                var scoreHistory = JsonSerializer.Deserialize<JsonElement>(json);

                // Create a timestamp for the filename
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss");
                var filename = $"score-history-{timestamp}.json";

                // Upload to blob storage
                var blobClient = blobContainerClient.GetBlobClient(filename);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                await blobClient.UploadAsync(stream, overwrite: true);

                return Results.Ok(new { success = true, filename });
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error uploading score history");
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error uploading score history"
                );
            }
        });

        app.MapGet("/scoreboard/test-blob-connection", async (BlobContainerClient blobContainerClient) =>
        {
            try
            {
                var blobClient = blobContainerClient.GetBlobClient($"connection-test-{DateTime.Now.Ticks}.txt");

                await blobClient.UploadAsync(
                    BinaryData.FromString("Connection test successful"), overwrite: true);

                return Results.Ok("Connection to blob storage successful!");
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error testing blob storage connection");
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error testing blob storage connection"
                );
            }
        });
    }
}
using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Http.Json;

using SharedTools;

using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

// Add Azure Blob Storage client
builder.Services.AddBlobStorage(builder.Configuration);

// Add Rate Limiter
builder.Services.AddRateLimiter();

var app = builder.Build();

app.MapFavicon();
app.MapHtmlPage("/", "index.html");

app.MapResource("app.js");
app.MapResource("styles.css");
app.MapResource("audio/buzzer.mp3");
app.MapResource("icons/fullscreen.svg");
app.MapResource("icons/settings.svg");
app.MapResource("icons/players.svg");
app.MapResource("default-players.json");

// Map API endpoints
app.MapScoreboardEndpoints();

app.Run();

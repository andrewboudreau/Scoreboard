//using Azure.Storage.Blobs;

//using Scoreboard;

//using System.Threading.RateLimiting;

//namespace SharedTools;

//public static class ScoreboardExtensions
//{
//    public static WebApplicationBuilder AddAzureBlobClient(this WebApplicationBuilder builder)
//    {
//        builder.Services.AddSingleton(x =>
//        {
//            var connectionString = builder.Configuration["BlobStorage:ConnectionString"];
//            var containerName = builder.Configuration["BlobStorage:ContainerName"];

//            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(containerName))
//            {
//                throw new InvalidOperationException("Blob storage connection string or container name is not configured.");
//            }

//            var blobServiceClient = new BlobServiceClient(connectionString);
//            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

//            // Create the container if it doesn't exist
//            containerClient.CreateIfNotExists();
//            return containerClient;
//        });

//        return builder;
//    }

//    public static WebApplicationBuilder AddAntiHackerRateLimiter(this WebApplicationBuilder builder)
//    {
//        builder.Services.AddRateLimiter(options =>
//        {
//            options.AddPolicy("ScoreboardEndpoints", httpContext =>
//            {
//                // Only apply rate limiting for paths that start with "/scoreboard"
//                if (httpContext.Request.Path.StartsWithSegments("/scoreboard"))
//                {
//                    return RateLimitPartition.GetFixedWindowLimiter(
//                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
//                        factory: partition => new FixedWindowRateLimiterOptions
//                        {
//                            PermitLimit = 6,
//                            Window = TimeSpan.FromMinutes(1)
//                        });
//                }

//                // Skip rate limiting for other paths
//                return RateLimitPartition.GetNoLimiter("default");
//            });

//            options.RejectionStatusCode = 429;
//        });

//        return builder;
//    }

//    public static WebApplication MapScoreboard(this WebApplication app)
//    {
//        app.MapFavicon();
//        app.MapHtmlPage("/", "index.html");
//        app.MapResource("app.js");
//        app.MapResource("styles.css");
//        app.MapResource("audio/buzzer.mp3");
//        app.MapResource("icons/fullscreen.svg");
//        app.MapResource("icons/settings.svg");
//        app.MapResource("icons/players.svg");
//        app.MapResource("default-players.json");

//        app.MapPost("/scoreboard/upload-history", ScoreboardApiMethods.UploadHistory)
//            .RequireRateLimiting("ScoreboardEndpoints");

//        app.MapGet("/scoreboard/test-blob-connection", ScoreboardApiMethods.TestBlobClient)
//            .RequireRateLimiting("ScoreboardEndpoints");

//        return app;
//    }
//}

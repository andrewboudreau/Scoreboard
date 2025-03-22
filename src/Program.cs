using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http.Json;
using SharedTools;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

// Add Azure Blob Storage client
builder.Services.AddSingleton(x => 
{
    var connectionString = builder.Configuration["BlobStorage:ConnectionString"];
    var containerName = builder.Configuration["BlobStorage:ContainerName"];
    
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

// API endpoint to upload score history to blob storage
app.MapPost("/api/upload-history", async (HttpContext context, BlobContainerClient blobContainerClient) =>
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

// API endpoint to test blob storage connection
app.MapGet("/api/test-blob-connection", async (BlobContainerClient blobContainerClient) =>
{
    try
    {
        // Create a test file
        var testFilename = $"connection-test-{DateTime.UtcNow.Ticks}.txt";
        var blobClient = blobContainerClient.GetBlobClient(testFilename);
        
        // Upload a small test file
        var content = "Connection test successful";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await blobClient.UploadAsync(stream, overwrite: true);
        
        return Results.Ok(new { success = true, message = "Connection to blob storage successful!" });
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

app.Run();

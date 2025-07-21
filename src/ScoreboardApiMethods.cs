using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ScoreboardModule;

public class ScoreboardLogger { };

public static class ScoreboardApiMethods
{
    public static async Task<IResult> UploadHistory(IHttpContextAccessor contextAccessor, BlobContainerClient blobClient, ILogger<ScoreboardLogger> logger)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(contextAccessor.HttpContext);

            MemoryStream? stream = await SafeReadSmallRequestBody(contextAccessor.HttpContext.Request);
            if (stream is null)
            {
                return Results.Problem(detail: "Invalid History Data.", statusCode: 400, title: "Bad Request");
            }

            var filename = $"score-history-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.json";
            var blobClientInstance = blobClient.GetBlobClient(filename);

            await blobClientInstance.UploadAsync(stream, overwrite: true);
            await stream.DisposeAsync();

            return Results.Ok(filename);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading score history");
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Error uploading score history"
            );
        }
    }

    public static async Task<IResult> TestBlobClient(BlobContainerClient blobContainerClient, ILogger<ScoreboardLogger> logger)
    {
        try
        {
            var blobClient = blobContainerClient.GetBlobClient($"connection-test-{DateTime.Now.Ticks}.txt");
            await blobClient.UploadAsync(BinaryData.FromString("Connection test successful"), overwrite: true);
            return Results.Ok("Connection to blob storage successful!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error testing blob storage connection");
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Error testing blob storage connection"
            );
        }
    }

    private static async Task<MemoryStream?> SafeReadSmallRequestBody(HttpRequest request)
    {
        var memoryStream = new MemoryStream();

        // First check Content-Length header if available
        if (request.ContentLength.HasValue && request.ContentLength.Value > 512 * 1024) // 500 KB limit
        {
            return null;
        }

        // If no Content-Length or within limits, read in chunks with size tracking
        byte[] buffer = new byte[64 * 1024]; // 64KB chunks
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await request.Body.ReadAsync(buffer)) > 0)
        {
            totalBytesRead += bytesRead;

            // Check size limit during streaming
            if (totalBytesRead > 512 * 1024) // 500 KB limit
            {
                return null;
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        memoryStream.Position = 0;
        return memoryStream;
    }
}

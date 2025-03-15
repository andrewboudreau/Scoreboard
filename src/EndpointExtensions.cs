using System.Runtime.InteropServices;

namespace SharedTools;

public static class EndpointExtensions
{
    private const string wwwrootPath = "wwwroot/";

    private static readonly Dictionary<string, string> ContentTypeMap = new()
    {
        { ".css", "text/css" },
        { ".js", "text/javascript" },
        { ".ico", "image/x-icon" },
        { ".mp3", "audio/mpeg" },
        { ".svg", "image/svg+xml" },
        { ".jpeg", "image/jpeg" },
        { ".jpg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".png", "image/png" },
        { ".webm", "video/webm" },
        { ".bmp", "image/bmp" },
        { ".wav", "audio/wav" }
    };

    public static IEndpointConventionBuilder MapHtmlPage(this IEndpointRouteBuilder endpoints, string path, string filePath)
    {
        var content = Results.Content(File.ReadAllText(wwwrootPath + filePath), "text/html");
        return endpoints.MapGet(path, () => content);
    }

    public static IEndpointConventionBuilder MapResource(this IEndpointRouteBuilder endpoints, string requestPath, string? filePath = null)
    {
        filePath ??= requestPath;
        var extension = Path.GetExtension(filePath);

        if (!ContentTypeMap.TryGetValue(extension, out var contentType))
        {
            throw new InvalidOperationException($"Unsupported file type: {extension}");
        }

        // Path normalization: ensure it starts with "/" if not already
        string normalizedPath = requestPath.StartsWith('/') ? requestPath : "/" + requestPath;

        return endpoints.MapGet(normalizedPath, async context =>
        {
            context.Response.ContentType = contentType;
            await context.Response.SendFileAsync(wwwrootPath + filePath);
        });
    }

    public static IEndpointConventionBuilder MapFavicon(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapResource("favicon.ico");
    }

    public static IEndpointConventionBuilder MapDotNetVersion(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/dotnetversion", () => RuntimeInformation.FrameworkDescription);
    }
}
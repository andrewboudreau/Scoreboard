using System.Runtime.InteropServices;

namespace SharedTools;

public static class EndpointExtensions
{
    public static IEndpointConventionBuilder MapHtmlPage(this IEndpointRouteBuilder endpoints, string path, string filePath)
    {
        var content = Results.Content(File.ReadAllText("wwwroot/" + filePath), "text/html");
        return endpoints.MapGet(path, () => content);
    }

    public static IEndpointConventionBuilder MapCss(this IEndpointRouteBuilder endpoints, string path)
    {
        var content = Results.Content(File.ReadAllText("wwwroot/" + path), "text/css");
        return endpoints.MapGet(path, () => content);
    }

    public static IEndpointConventionBuilder MapJs(this IEndpointRouteBuilder endpoints, string path)
    {
        var content = Results.Content(File.ReadAllText("wwwroot/" + path), "text/javascript");
        return endpoints.MapGet(path, () => content);
    }

    public static IEndpointConventionBuilder MapAudioFile(this IEndpointRouteBuilder endpoints, string path)
    {
        return endpoints.MapGet("/" + path, async context =>
        {
            context.Response.ContentType = "audio/mpeg";
            await context.Response.SendFileAsync("wwwroot/" + path);
        });
    }

    public static IEndpointConventionBuilder MapFavicon(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/favicon.ico", async context =>
        {
            context.Response.ContentType = "image/x-icon";
            await context.Response.SendFileAsync("wwwroot/favicon.ico");
        });
    }

    public static IEndpointConventionBuilder MapDotNetVersion(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/dotnetversion", () => RuntimeInformation.FrameworkDescription);
    }
}

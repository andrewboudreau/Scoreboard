using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Scoreboard.Services;

using SharedTools.Web.Modules;

namespace SharedTools.Scoreboard;

public class ScoreboardModule : IApplicationPartModule
{
    public string Name => "Scoreboard";

    public void ConfigureServices(IServiceCollection services)
    {
        // Modules:Scoreboard:* from the module host; the old root BlobStorage:* keys still work.
        services.AddModuleOptions<ScoreboardOptions>(Name)
            .PostConfigure<IConfiguration>((o, config) =>
            {
                if (string.IsNullOrEmpty(o.ConnectionString)) o.ConnectionString = config["BlobStorage:ConnectionString"];
                if (string.IsNullOrEmpty(o.ContainerName)) o.ContainerName = config["BlobStorage:ContainerName"];
                if (string.IsNullOrEmpty(o.DataFolder)) o.DataFolder = config["BlobStorage:DataFolder"];
            });

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ScoreboardOptions>>().Value;
            if (string.IsNullOrEmpty(options.ConnectionString) || string.IsNullOrEmpty(options.ContainerName))
            {
                throw new InvalidOperationException(
                    "Scoreboard blob storage is not configured. Set Modules:Scoreboard:ConnectionString and ContainerName.");
            }

            var container = new BlobContainerClient(options.ConnectionString, options.ContainerName);
            container.CreateIfNotExists();
            return container;
        });

        services.AddSingleton<ScoreboardStore>();
        services.AddSingleton<PhotoUsage>();
    }

    public void Configure(WebApplication app)
    {
        var webRoot = this.WebRoot();
        var version = ContentVersion(webRoot);
        app.MapGet("/Scoreboard/", (HttpContext ctx) => Page(ctx, webRoot, version, "index.html"));
        app.MapGet("/Scoreboard/docs", (HttpContext ctx) => Page(ctx, webRoot, version, "docs.html"));
        app.MapGet("/Scoreboard/game", (HttpContext ctx) => Page(ctx, webRoot, version, "game.html"));
        app.MapGet("/Scoreboard/stats", (HttpContext ctx) => Page(ctx, webRoot, version, "stats.html"));
        // The team builder now lives on the scoreboard itself.
        app.MapGet("/Scoreboard/ManagePlayers", () => Results.Redirect("/Scoreboard/#teams"));

        // Photo page for the group chat: pick your name, crop a photo, optional AI style (PhotoBooth.cs)
        app.MapGet("/Scoreboard/photo", (HttpContext ctx) => Page(ctx, webRoot, version, "photo.html"));
        app.MapGet("/Scoreboard/photo/usage", (HttpContext ctx) => Page(ctx, webRoot, version, "photo-usage.html"));

        ScoreboardApi.Map(app);
        PhotoBooth.Map(app);
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
    }

    // A hash of everything in wwwroot (plus the assembly), so any change to app.js/styles.css
    // gives pages new script URLs and phones drop their cached copies.
    private static string ContentVersion(string webRoot)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(typeof(ScoreboardModule).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        if (Directory.Exists(webRoot))
        {
            foreach (var file in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(webRoot, file)));
                hash.AppendData(File.ReadAllBytes(file));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..8];
    }

    // Serves an HTML page from the drop's wwwroot, or 404 if it is missing.
    // Pages are tiny and always revalidated, so a phone never holds on to an old page
    // that points at old script versions.
    // Synchronous on purpose: a (HttpContext) => Task lambda binds to MapGet's RequestDelegate
    // overload, which would discard the returned IResult and send an empty page.
    private static IResult Page(HttpContext context, string webRoot, string version, string fileName)
    {
        var path = Path.Combine(webRoot, fileName);
        if (!File.Exists(path)) return Results.NotFound();

        context.Response.Headers.CacheControl = "no-cache";
        var html = File.ReadAllText(path).Replace("{{v}}", version);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}

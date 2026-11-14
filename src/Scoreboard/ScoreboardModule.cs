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
    }

    public void Configure(WebApplication app)
    {
        var webRoot = this.WebRoot();
        app.MapGet("/Scoreboard/", (HttpContext ctx) => Page(ctx, webRoot, "index.html"));
        app.MapGet("/Scoreboard/docs", (HttpContext ctx) => Page(ctx, webRoot, "docs.html"));
        app.MapGet("/Scoreboard/game", (HttpContext ctx) => Page(ctx, webRoot, "game.html"));
        app.MapGet("/Scoreboard/stats", (HttpContext ctx) => Page(ctx, webRoot, "stats.html"));
        // The team builder now lives on the scoreboard itself.
        app.MapGet("/Scoreboard/ManagePlayers", () => Results.Redirect("/Scoreboard/#teams"));

        ScoreboardApi.Map(app);
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
    }

    // Changes with every build, so phones pick up new app.js/styles.css instead of cached copies.
    private static readonly string BuildVersion =
        typeof(ScoreboardModule).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];

    // Serves an HTML page from the drop's wwwroot, or 404 if it is missing.
    // Pages are tiny and always revalidated, so a phone never holds on to an old page
    // that points at old script versions.
    // Synchronous on purpose: a (HttpContext) => Task lambda binds to MapGet's RequestDelegate
    // overload, which would discard the returned IResult and send an empty page.
    private static IResult Page(HttpContext context, string webRoot, string fileName)
    {
        var path = Path.Combine(webRoot, fileName);
        if (!File.Exists(path)) return Results.NotFound();

        context.Response.Headers.CacheControl = "no-cache";
        var html = File.ReadAllText(path).Replace("{{v}}", BuildVersion);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}

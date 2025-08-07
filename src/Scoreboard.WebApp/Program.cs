using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

await builder.AddApplicationPartModules([
    "SharedTools.Scoreboard",
//    "SharedTools.ModuleManagement"
]);

var app = builder.Build();

// Enable static file serving - this is crucial for module static content
app.UseStaticFiles();

app.UseApplicationPartModules();

// Add a simple home page that redirects to the Scoreboard module
app.MapGet("/", () => Results.Redirect("/Scoreboard/"));

app.Run();

using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages support
builder.Services.AddRazorPages();

await builder.AddApplicationPartModules([
    "SharedTools.Scoreboard",
    "SharedTools.ModuleManagement"
]);

var app = builder.Build();

// Use Razor Pages
app.MapRazorPages();

app.UseApplicationPartModules();

// Add a simple home page that redirects to the Scoreboard module
app.MapGet("/", () => Results.Redirect("/Scoreboard/"));

app.Run();

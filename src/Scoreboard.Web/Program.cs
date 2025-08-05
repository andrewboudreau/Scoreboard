using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

// Core services
builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// Load modules from configuration or default to Scoreboard
var modulesToLoad = builder.Configuration
    .GetSection("Modules")
    .Get<string[]>() ?? ["Scoreboard"];

await builder.AddApplicationPartModules(modulesToLoad);

var app = builder.Build();

// Environment-specific configuration
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Add a simple home page that redirects to the Scoreboard module
app.MapGet("/", () => Results.Redirect("/Scoreboard/"));

// Activate all loaded modules
app.UseApplicationPartModules();

app.Run();

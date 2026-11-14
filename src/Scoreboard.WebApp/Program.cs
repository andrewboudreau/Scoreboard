using SharedTools.ModuleManagement;
using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Loads module drops: everything under Modules:Root plus each Modules:Paths entry.
// appsettings.json points "scoreboard" at bin/drops/scoreboard, which the build publishes.
builder.AddModules();

// Module hub at /_modules: what loaded, which drop is serving, what failed.
builder.AddModule<ModuleManagementModule>();

var app = builder.Build();

app.UseStaticFiles();
app.MapRazorPages();

// Serves /_content/{Name}/ from each drop's wwwroot, then calls each module's Configure.
app.UseModules();

app.MapGet("/", () => Results.Redirect("/Scoreboard/"));

app.Run();

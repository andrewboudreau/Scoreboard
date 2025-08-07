using SharedTools.Web.Modules;
 
var app = await SelfHostingExtensions
    .CreateSelfHostedModuleAsync(
        args: args,
        packageIds: [
            "SharedTools.Scoreboard", 
            "SharedTools.ModuleManagement"
        ]);

// Add a simple home page that redirects to the Scoreboard module
app.MapGet("/", () => Results.Redirect("/Scoreboard/"));

app.Run();

using SharedTools;

var app = WebApplication.CreateBuilder(args)
    .AddAzureBlobClient()
    .AddAntiHackerRateLimiter()
    .Build();

app.UseRateLimiter();
app.MapScoreboard().Run();

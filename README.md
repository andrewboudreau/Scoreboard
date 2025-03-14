
# Scoreboard
A simple scoreboard application for keeping track of scores in games.

![Scoreboard Application Screenshot](demo.gif)

# Running the Application
To run the application, you need to have [.NET 9 installed](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). Next,
```bash
dotnet run
```

# C# .NET 9 Minimal Api
```csharp
using SharedTools;

var app = WebApplication.CreateBuilder(args).Build();

app.MapFavicon();
app.MapHtmlPage("/", "index.html");
app.MapResource("styles.css");

app.MapResource("audio/buzzer.mp3");
app.MapResource("icons/fullscreen.svg");
app.MapResource("icons/settings.svg");


app.Run();
```
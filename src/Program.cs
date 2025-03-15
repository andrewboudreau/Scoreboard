using SharedTools;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapFavicon();
app.MapHtmlPage("/", "index.html");

app.MapResource("app.js");
app.MapResource("styles.css");
app.MapResource("audio/buzzer.mp3");
app.MapResource("icons/fullscreen.svg");
app.MapResource("icons/settings.svg");

app.Run();

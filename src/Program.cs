using SharedTools;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapFavicon();
app.MapHtmlPage("/", "index.html");
app.MapAudioFile("buzzer.mp3");

app.Run();

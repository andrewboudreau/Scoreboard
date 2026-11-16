# Scoreboard

A basketball scoreboard for a weekly pickup game, built as a [SharedTools](https://github.com/andrewboudreau/SharedTools) module for ASP.NET Core. Every phone, tablet or laptop that opens it sees the same teams, score and history; the server stores everything in Azure Blob Storage.

![Scoreboard in action](demo.gif)

## Features

- **Scoreboard** - tap to score, long-press to undo, countdown timer with buzzer, quarter scores, fullscreen, keep-screen-on
- **Player scoring** - tap a player to credit them and their team
- **Team-only games** - type in the score at the end of each quarter when nobody is tracking baskets
- **Teams panel** - one tap per player for BLACK / Out / WHITE, Balance (most even teams from past results) or Shuffle, pins, player photos, share the teams to the group chat. A guided tour explains it ("?" button)
- **Shared across devices** - the server is the single source of truth; changes save automatically and retry when offline
- **Stats** - records, streaks, comebacks, by-quarter averages, player table with game logs, game history
- **Insights** - player impact (adjusted plus-minus from final scores, back-tested), next-game win prediction, best duos, scoring styles, attendance, trends
- **Game pages** - score chart, box score, play-by-play, live follow-along while a game is in progress
- **Data cleanup** - remove a test game from stats (reversible), and merge one person who ended up under two player ids
- **Photo page** - a phone page for the group chat: pick your name, take or crop a photo, optionally restyle it with AI (trading card, bobblehead, 8-bit, ...), save it as your avatar

Teams are BLACK and WHITE, reshuffled every week, so stats are kept per player and never as BLACK vs WHITE across games.

There is no login: anyone with the URL can view and edit. It's meant for a private group of friends.

## Pages

| URL | Page |
|-----|------|
| `/Scoreboard/` | The scoreboard. `?players` opens the Teams panel, `?players=edit` opens roster editing, `?players&tour` starts the tour |
| `/Scoreboard/stats` | Stats dashboard: overview, players, insights, game history |
| `/Scoreboard/game?id={gameId}` | One game |
| `/Scoreboard/photo` | Photo page. `?k=<code>` unlocks AI styles, `?p=<playerId>` skips the name list |
| `/Scoreboard/photo/usage` | Admin view of who used the AI styles (needs the admin key) |
| `/Scoreboard/docs` | User-facing docs |

## How it's built

The scoreboard is a SharedTools 3.0 web module (`src/Scoreboard`). It ships as a **drop**: its `dotnet publish` output (assembly, dependencies and `wwwroot/`), zipped and uploaded to a module host, which loads it at startup. `src/Scoreboard.WebApp` is a minimal local host that loads the same drop.

- **Drop, not a package** - nothing is published to NuGet; `deploy.ps1` uploads a new version
- **Isolated** - the module's dependencies (Azure Blob, ImageSharp) load in their own context
- **Static files** - the host serves the drop's `wwwroot/` at `/_content/Scoreboard/`
- **Thin server** - the API stores the JSON documents the browser sends; game logic and stats run in the browser as plain HTML/CSS/JS with no build step

```
Scoreboard/
├── src/
│   ├── Scoreboard/                 # The module (SharedTools.Scoreboard)
│   │   ├── ScoreboardModule.cs     # IApplicationPartModule: services, page routes
│   │   ├── ScoreboardApi.cs        # /Scoreboard/api endpoints
│   │   ├── PhotoBooth.cs           # /Scoreboard/api/photo endpoints, usage log and limits
│   │   ├── ScoreboardOptions.cs    # Modules:Scoreboard settings
│   │   ├── Services/
│   │   │   └── ScoreboardStore.cs  # Reads/writes blobs in the data folder
│   │   └── wwwroot/                # Static assets, published with the drop
│   │       ├── index.html, app.js, styles.css      # Scoreboard
│   │       ├── tour.js                             # Guided tour of the Teams panel
│   │       ├── stats.html, game.html               # Stats dashboard, single game
│   │       ├── stats-core.js, insights.js, stats.css   # Shared by the stats pages
│   │       ├── photo.html, photo.js, photo.css     # Photo page
│   │       ├── photo-usage.html                    # Photo usage admin page
│   │       ├── docs.html                           # User-facing docs
│   │       ├── default-players.json                # Seed roster for a fresh install
│   │       └── audio/  icons/
│   └── Scoreboard.WebApp/          # Minimal host (Program.cs)
├── tests/Scoreboard.Tests.Playwright/  # End-to-end tests
├── docs/FRONTEND.md                # Frontend architecture
└── deploy.ps1                      # Publish, zip and upload the drop
```

`ScoreboardModule.cs` registers the `BlobContainerClient`, `ScoreboardStore` and photo usage log, maps the HTML pages and calls `ScoreboardApi.Map` and `PhotoBooth.Map`. Pages are read from the drop's `wwwroot/`, and `{{v}}` in them is replaced with a hash of the wwwroot files so browsers pick up new scripts after every deploy.

See [docs/FRONTEND.md](docs/FRONTEND.md) for the client side.

## Quick start

### Prerequisites

- .NET 11 SDK
- An Azure Storage account, or [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) locally

### Running locally

1. Start Azurite (blob endpoint on port 10000).
2. Run the host. Its build publishes the module to `src/Scoreboard.WebApp/bin/drops/scoreboard`, which it loads through `Modules:Paths:scoreboard`:
   ```bash
   dotnet run --project src/Scoreboard.WebApp/Scoreboard.WebApp.csproj
   ```
3. Open http://localhost:5227/Scoreboard/ (module hub: http://localhost:5227/_modules)

Changes to `src/Scoreboard` need a host restart; `dotnet run` rebuilds and republishes the drop.

## Configuration

The module reads its own section, `Modules:Scoreboard` (environment: `Modules__Scoreboard__ConnectionString`, ...). In the host's `appsettings.json`:

```json
{
  "Modules": {
    "Scoreboard": {
      "ConnectionString": "UseDevelopmentStorage=true",
      "ContainerName": "scoreboard-data",
      "DataFolder": "main"
    }
  }
}
```

| Setting | Default | Meaning |
|---------|---------|---------|
| `ConnectionString` | required | Azure Blob Storage connection string |
| `ContainerName` | required | Container holding all data |
| `DataFolder` | auto | Folder inside the container. Unset: the folder of the old group with the most games (so history from earlier versions carries over), or `main` on a fresh install. `GET /Scoreboard/api/info` shows which folder is in use |
| `OpenAIKey` | unset | OpenAI API key for the AI photo styles. Unset: the photo page works without styles. **Secret** |
| `PhotoAdminKey` | unset | Key for the usage page and its API. Unset: the usage page is off. **Secret** |
| `PhotoLinkCode` | unset | When set, AI styles only work from a link carrying `?k=<code>` |
| `OpenAIImageModel` | `gpt-image-2` | Image model used for styles |
| `OpenAIImageQuality` | `low` | `low`, `medium` or `high` |
| `OpenAIBaseUrl` | `https://api.openai.com/v1` | Override for tests or a proxy |
| `PhotoDailyLimitPerDevice` | `30` | AI tries per phone per day (UTC). An IP address gets twice this |
| `PhotoDailyLimitTotal` | `300` | AI tries per day across everyone: the cost ceiling |
| `PhotoTryRetentionDays` | `14` | Days to keep AI tries and source photos nobody saved. `0` keeps everything; saved photos are never deleted |

The old root `BlobStorage:ConnectionString` / `ContainerName` / `DataFolder` keys still work as a fallback. Set the secrets in the host's environment, never in the repo.

### Storage layout

```text
{container}/{dataFolder}/
├── roster.json            # { team1Name, team2Name, players: [...] }
├── current-game.json      # { gameId }
├── games/{gameId}.json    # full game: scores, quarters, players, events
├── hidden-games.json      # { ids: [...] } games removed from stats
├── player-links.json      # { links: { fromId: toId } } one person under two ids
├── players/{id}.png       # 256x256 player photos
├── photo-archive/{playerId}/   # saved photos, AI tries and their source photos (JPEG)
├── photo-usage/{yyyy-MM}.json  # every AI try and photo save
├── photo-events/{yyyy-MM}.json # photo page visits, steps and clicks
└── photo-blocked.json     # blocked phone ids and IPs
```

## API

All under `/Scoreboard/api`. JSON bodies are capped at 1 MB; game ids must match `[A-Za-z0-9_-]{1,80}`.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/info` | Version, data folder, game count, whether a roster exists |
| GET | `/roster` | Roster; on a fresh install, the bundled default players with `seeded: true` |
| PUT | `/roster` | Replace the roster |
| GET | `/current-game` | The current game document, or 204 if none |
| PUT | `/current-game` | Set the pointer: `{ "gameId": "..." }` |
| GET | `/games` | Every saved game, as one JSON array |
| GET | `/games/{id}` | One game (404 if missing) |
| PUT | `/games/{id}` | Save a game |
| GET, PUT | `/hidden-games` | `{ ids }` of games removed from stats. The game files stay, so it can be undone |
| GET, PUT | `/player-links` | `{ links: { fromId: toId } }`; stats count `fromId` as `toId` |
| POST | `/players/{id}/image` | Upload a photo (multipart field `image`, max 10 MB); center-cropped to a 256x256 PNG. Returns `{ imageUrl }` |
| GET | `/players/{id}/image` | Player photo (PNG) |

### Photo page

Under `/Scoreboard/api/photo`. Each phone sends a random device id (kept in its localStorage), so AI usage is logged and limited per phone, per IP and in total per day. The OpenAI key never leaves the server.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/config` | Which styles this phone can use and how many tries it has left today |
| POST | `/stylize` | Cropped photo + style → styled photo (OpenAI image edit) |
| POST | `/save` | Saves the photo as `players/{id}.png`, sets the roster `imageUrl`, keeps a copy in `photo-archive/` |
| POST | `/events` | Page beacon: visits, steps and clicks |
| GET | `/usage` | Usage report. Admin |
| POST | `/block` | Block or unblock a phone or IP. Admin |
| POST | `/reset` | Give a phone its daily tries back. Admin |
| GET | `/archive`, `/archive/file` | List and fetch archived photos. Admin |

Admin endpoints need the `X-Admin-Key` header to match `PhotoAdminKey`.

## Tests

Playwright end-to-end tests (NUnit, page object `ScoreboardPage.cs`) in `tests/Scoreboard.Tests.Playwright/`. The host must be running to serve the pages; `FakeApi.cs` intercepts `/Scoreboard/api/**` with an in-memory fake, so the tests never touch real storage.

```bash
dotnet test
dotnet test --filter "TestName"                # one test
cmd /c "set HEADED=1 && dotnet test"           # visible browser
set SCOREBOARD_BASE_URL=http://localhost:5227  # different host
```

## Deploying

- Pushes to `master` bump the patch version in `Directory.Build.props`, then deploy the bumped commit with `deploy.ps1` (`.github/workflows/ci.yml`). `/Scoreboard/api/info` reports the running version. The deploy job uses the `production` environment, whose `MODULE_UPLOAD_KEY` secret is only available to `master`.
- `./deploy.ps1` can also be run locally to deploy the working tree. It publishes `src/Scoreboard` to `out/`, zips it, uploads it to the module host (default https://modules.blogfor.net, id `scoreboard`) and waits until the host reports the drop as loaded. If the drop fails to load, the script exits with an error and the host keeps serving the previous one.
- The upload key is `MODULE_UPLOAD_KEY`, from the environment or a gitignored `.env` at the repo root. It lets the holder run code on the host: never commit or share it.

## Hosting it elsewhere

To host the module in another ASP.NET Core app (SharedTools.Web 3.x), call `builder.AddModules()` and `app.UseModules()`, point `Modules:Paths:scoreboard` at a published drop (or upload it to the host's module store), and configure `Modules:Scoreboard` as above. `src/Scoreboard.WebApp/Program.cs` is a complete example:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddModules();                               // loads drops from Modules:Root and Modules:Paths
builder.AddModule<ModuleManagementModule>();        // module hub at /_modules

var app = builder.Build();
app.UseModules();                                   // serves /_content/{Name}/ and calls each module's Configure
app.Run();
```

## License

MIT - see [LICENSE.txt](LICENSE.txt).

## Related Documentation

- [Frontend Architecture](docs/FRONTEND.md) - pages, `app.js` classes, sync, stats and insights
- [User docs](src/Scoreboard/wwwroot/docs.html) - served at `/Scoreboard/docs`
- [SharedTools](https://github.com/andrewboudreau/SharedTools) - module drops and hosting
- [CLAUDE.md](CLAUDE.md) - notes for working on this repo with Claude Code

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the solution
dotnet build

# Run the host application (localhost:5227). Its build also publishes the module
# to src/Scoreboard.WebApp/bin/drops/scoreboard, which the host loads.
dotnet run --project src/Scoreboard.WebApp/Scoreboard.WebApp.csproj
```

There is no `dotnet pack` step: the module is not a NuGet package (`IsPackable=false`).

## Testing

Playwright end-to-end tests require the host application to be running first.

```bash
# Run all tests
dotnet test

# Run tests with visible browser
cmd /c "set HEADED=1 && dotnet test"

# Run a single test
dotnet test --filter "TestName"

# Point tests at a different host
set SCOREBOARD_BASE_URL=http://localhost:5227
dotnet test
```

Tests are in `tests/Scoreboard.Tests.Playwright/` using NUnit + Playwright with Page Object Model pattern (`ScoreboardPage.cs`). `FakeApi.cs` intercepts `/Scoreboard/api/**` and serves documents from memory, so tests never touch real storage; the host only serves the pages.

## Architecture

This is a SharedTools 3.0 module with two projects:

### Scoreboard (src/Scoreboard/) - The Module
An ASP.NET Core module implementing `IApplicationPartModule` (assembly `SharedTools.Scoreboard`). It ships as a **drop**: its `dotnet publish` output (assembly, dependencies, `wwwroot/`), which a module host loads at startup.
- **ScoreboardModule.cs**: Entry point - registers services, serves the HTML pages from the drop's `wwwroot/` (`Page()` replaces `{{v}}` with a hash of the wwwroot files for cache busting; it must stay synchronous)
- **ScoreboardApi.cs**: JSON API. The server is the single source of truth; there is no auth (private friend group) and no groups/share codes
- **ScoreboardOptions.cs**: Settings bound from `Modules:Scoreboard`
- **Services/ScoreboardStore.cs**: Reads/writes JSON blobs in one container under a data folder
- **wwwroot/**: `index.html` + `app.js` + `styles.css` (scoreboard SPA; `tour.js` is the guided tour of the Teams panel, started by its "?" button or a `?players&tour` link. `?players` / `?players=edit` open the panel / roster editing), `stats.html` / `game.html` (dashboard and single game, sharing `stats-core.js` + `insights.js` + `stats.css`), `photo.html` + `photo.js` + `photo.css` (phone page for the group chat: pick your name, crop a photo, optional AI style, save), `photo-usage.html` (admin view of who used the AI styles) and `docs.html` (user-facing docs). Plain HTML/CSS/JS, no build step
- **PhotoBooth.cs**: API behind the photo page. AI styles call OpenAI's image edit endpoint with the server-side key; each phone sends a random device id (localStorage) so usage is logged and limited per phone, per IP and in total per day

### Scoreboard.WebApp (src/Scoreboard.WebApp/) - The Host
Minimal ASP.NET Core app for local development. `Program.cs` calls `builder.AddModules()` / `app.UseModules()` and adds the module hub (`/_modules`). After every build it publishes the module to `bin/drops/scoreboard`, which `appsettings.json` points at through `Modules:Paths:scoreboard`.

### Module Loading Flow
1. Host loads every drop under `Modules:Root` plus each `Modules:Paths` entry
2. Each drop's assemblies load into their own isolated context (the host supplies `SharedTools.Web`)
3. Module's `ConfigureServices()` and `Configure()` are called
4. Host serves each drop's `wwwroot/` at `/_content/{Name}/`

### API Endpoints
All prefixed with `/Scoreboard/api/`:
- `GET /info` - version, data folder, game count
- `GET|PUT /roster` - players, team names, who's in
- `GET|PUT /current-game` - PUT `{gameId}` pointer; GET returns the game doc (204 if none)
- `GET /games`, `GET|PUT /games/{id}` - game docs (events: `score`, `score_set`, `period_end`)
- `GET|PUT /hidden-games` - `{ids}` of games removed from stats (test games, mistakes); reversible, game files stay
- `GET|PUT /player-links` - `{links: {fromId: toId}}`: one person under two ids (each iPad used to keep its own player list); set from a player's stats page
- `POST|GET /players/{id}/image` - player photos (256px PNG)
- `GET /photo/config`, `POST /photo/stylize`, `POST /photo/save` - photo page (`/Scoreboard/photo`, `?k=<code>` unlocks AI styles, `?p=<playerId>` skips the name list). Save writes `players/{id}.png`, sets the roster `imageUrl` and keeps a copy in `photo-archive/`
- `POST /photo/events` - photo page beacon (visits, steps, clicks), logged in `photo-events/yyyy-MM.json`
- `GET /photo/usage`, `POST /photo/block`, `POST /photo/reset`, `GET /photo/archive`, `GET /photo/archive/file` - usage report, block list, give a phone its daily tries back, browse archived photos; `X-Admin-Key` header (page: `/Scoreboard/photo/usage`). Log in `photo-usage/yyyy-MM.json`, blocks in `photo-blocked.json`

### Data
- Settings come from `Modules:Scoreboard` (`ConnectionString`, `ContainerName`, `DataFolder`; prod container: `scoreboard`). The old root `BlobStorage:*` keys still work as a fallback
- Data folder: `DataFolder`, else the legacy group folder with the most games, else `main`
- Teams are BLACK (`'1'`) and WHITE (`'2'`), reshuffled weekly with colors picked at random: stats never add up BLACK vs WHITE across games, only per player (records, streaks, impact). Games may have per-player points, or only team scores typed per quarter; stats handle both
- Clients poll the server every 15s; localStorage only holds device prefs (old keys are migrated to the server once)

### Static Assets
`wwwroot/` ships as files in the drop; the host serves it from `/_content/Scoreboard/*`.

## Development Workflow

1. Make changes to `src/Scoreboard/`
2. Run host: `dotnet run --project src/Scoreboard.WebApp/Scoreboard.WebApp.csproj` (rebuilds and republishes the drop)
3. Test at http://localhost:5227/Scoreboard/ (module hub: http://localhost:5227/_modules)
4. Changes require a host restart

## Configuration

- **Directory.Build.props**: Centralized versioning (VersionPrefix)
- **nuget.config**: nuget.org only (`nuget.ci.config` is the copy CI restores with)
- **src/Scoreboard.WebApp/appsettings.json**: `Modules:Paths:scoreboard` (the local drop) and `Modules:Scoreboard` (blob storage; uses Azurite by default: `UseDevelopmentStorage=true`, container `scoreboard-data`)
- **Photo page secrets** (module host env, never in the repo): `Modules__Scoreboard__OpenAIKey` (AI styles off when unset), `Modules__Scoreboard__PhotoAdminKey` (usage page off when unset), `Modules__Scoreboard__PhotoLinkCode` (optional `?k=` code). Also `OpenAIImageModel` (gpt-image-2), `OpenAIImageQuality` (low), `OpenAIBaseUrl`, `PhotoDailyLimitPerDevice` (30, an IP gets 2x), `PhotoDailyLimitTotal` (300), `PhotoTryRetentionDays` (14; unsaved AI tries and their source photos are deleted after this)

## CI/CD

- **ci.yml** (the only workflow): on push to master, bumps the patch version in `Directory.Build.props`, commits it, then runs `deploy.ps1` on the bumped commit
- **deploy.ps1**: publishes `src/Scoreboard` to `out/`, zips it, uploads it to the module host (default https://modules.blogfor.net, id `scoreboard`) and waits until the host reports the drop as loaded. If the drop fails to load, the host keeps serving the previous one
- The upload key is `MODULE_UPLOAD_KEY` (the `production` environment secret in CI; locally the environment or a gitignored `.env`). It lets the holder run code on the host: never commit, print or share it

### Releasing

Every merge to master is a release: CI bumps the version and deploys it. When the user says "release" or "deploy", merge the work to master (or run `./deploy.ps1` to deploy the working tree) and check that `/Scoreboard/api/info` on production reports the new version. Nothing is published to NuGet.

## Troubleshooting

**Module not loading (404 on /Scoreboard/)**: Open http://localhost:5227/_modules to see what loaded and what failed; check that `src/Scoreboard.WebApp/bin/drops/scoreboard` exists (it is written by the host's build)

**Blob storage errors**: Ensure Azurite is running (blob endpoint on port 10000), check `Modules:Scoreboard` in the host's appsettings.json

**Static assets not found**: Verify files exist in wwwroot/ and in the drop, check browser DevTools network tab

**Old scripts after a change**: pages reference `app.js?v={{v}}`; the hash is computed at startup, so restart the host

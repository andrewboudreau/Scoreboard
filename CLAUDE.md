# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the solution
dotnet build

# Pack module for local development (outputs to C:\LocalNuGet)
dotnet pack src/Scoreboard/Scoreboard.csproj -c Debug

# Run the host application (localhost:5227)
dotnet run --project src/Scoreboard.WebApp/Scoreboard.WebApp.csproj
```

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

Tests are in `tests/Scoreboard.Tests.Playwright/` using NUnit + Playwright with Page Object Model pattern (`ScoreboardPage.cs`).

## Architecture

This is a SharedTools module system application with two main components:

### Scoreboard (src/Scoreboard/) - The Module
A dynamically loadable ASP.NET Core module implementing `IApplicationPartModule`:
- **ScoreboardModule.cs**: Entry point - registers services, serves the embedded HTML pages (`Page()` replaces `{{v}}` with the build id for cache busting)
- **ScoreboardApi.cs**: JSON API. The server is the single source of truth; there is no auth (private friend group) and no groups/share codes
- **Services/ScoreboardStore.cs**: Reads/writes JSON blobs in one container under a data folder
- **wwwroot/**: `index.html` + `app.js` + `styles.css` (scoreboard SPA; `tour.js` is the guided tour of the Teams panel, started by its "?" button or a `?players&tour` link. `?players` / `?players=edit` open the panel / roster editing), `stats.html` / `game.html` (dashboard and single game, sharing `stats-core.js` + `stats.css`)

### Scoreboard.WebApp (src/Scoreboard.WebApp/) - The Host
Minimal ASP.NET Core app that loads and hosts modules. Program.cs explicitly loads `SharedTools.Scoreboard` and `SharedTools.ModuleManagement` from NuGet.

### Module Loading Flow
1. Host reads module names from configuration
2. NuGet packages fetched from configured sources (C:\LocalNuGet for dev, nuget.org for prod)
3. Assemblies loaded into isolated context
4. Module's `ConfigureServices()` and `Configure()` called

### API Endpoints
All prefixed with `/Scoreboard/api/`:
- `GET /info` - version, data folder, game count
- `GET|PUT /roster` - players, team names, who's in
- `GET|PUT /current-game` - PUT `{gameId}` pointer; GET returns the game doc (204 if none)
- `GET /games`, `GET|PUT /games/{id}` - game docs (events: `score`, `score_set`, `period_end`)
- `GET|PUT /hidden-games` - `{ids}` of games removed from stats (test games, mistakes); reversible, game files stay
- `GET|PUT /player-links` - `{links: {fromId: toId}}`: one person under two ids (each iPad used to keep its own player list); set from a player's stats page
- `POST|GET /players/{id}/image` - player photos

### Data
- Blob container from `BlobStorage:ContainerName` (prod: `scoreboard`), connection from `BlobStorage:ConnectionString`
- Data folder: `BlobStorage:DataFolder`, else the legacy group folder with the most games, else `main`
- Teams are BLACK (`'1'`) and WHITE (`'2'`), reshuffled weekly with colors picked at random: stats never add up BLACK vs WHITE across games, only per player (records, streaks, impact). Games may have per-player points, or only team scores typed per quarter; stats handle both
- Clients poll the server every 15s; localStorage only holds device prefs (old keys are migrated to the server once)

### Static Assets
Assets embedded in module assembly, served from `/_content/Scoreboard/*` via `StaticWebAssetBasePath`.

## Development Workflow

1. Make changes to `src/Scoreboard/`
2. Pack module: `dotnet pack src/Scoreboard/Scoreboard.csproj -c Debug`
3. Run host: `dotnet run --project src/Scoreboard.WebApp/Scoreboard.WebApp.csproj`
4. Test at http://localhost:5227/Scoreboard/
5. Changes require repack and restart

## Configuration

- **Directory.Build.props**: Centralized versioning (VersionPrefix)
- **nuget.config**: NuGet sources (C:\LocalNuGet for local dev, nuget.org for releases)
- **appsettings.json**: Blob storage connection (uses Azure Storage Emulator by default: `UseDevelopmentStorage=true`)

## CI/CD

- **ci.yml**: Auto-bumps patch version on push to master
- **tag-and-publish.yml**: Manual workflow to tag and publish to NuGet.org

### Releasing

When the user says "release", "tag", or "tag and release", run the **Tag and Publish** GitHub Actions workflow:

```bash
gh workflow run "Tag and Publish"
```

Production picks up the new stable nuget.org version when its container restarts (`blogfor_net`, managed by the hosting-service session). Prerelease versions are not picked up.

## Troubleshooting

**Module not loading (404 on /Scoreboard/)**: Clear NuGet cache with `dotnet nuget locals all --clear`, verify package exists in C:\LocalNuGet

**Blob storage errors**: Ensure Azure Storage Emulator is running, check connection string in appsettings.json

**Static assets not found**: Verify files exist in wwwroot/, check browser DevTools network tab

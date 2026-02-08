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
- **ScoreboardModule.cs**: Entry point - registers services, maps endpoints, configures rate limiting
- **Services/**: `DefaultPlayersService`, `GameShareService`, `GroupService` for business logic
- **API endpoints**: `ScoreboardApiMethods.cs`, `GameShareApiMethods.cs`, `GroupApiMethods.cs`
- **wwwroot/**: Embedded static assets (SPA with app.js, styles.css, HTML pages)

### Scoreboard.WebApp (src/Scoreboard.WebApp/) - The Host
Minimal ASP.NET Core app that loads and hosts modules. Program.cs explicitly loads `SharedTools.Scoreboard` and `SharedTools.ModuleManagement` from NuGet.

### Module Loading Flow
1. Host reads module names from configuration
2. NuGet packages fetched from configured sources (C:\LocalNuGet for dev, nuget.org for prod)
3. Assemblies loaded into isolated context
4. Module's `ConfigureServices()` and `Configure()` called

### API Endpoints
All endpoints prefixed with `/Scoreboard/`:
- `POST /Scoreboard/api/games/share` - Create shareable game link
- `GET /Scoreboard/api/shares/{code}` - Retrieve shared game (public)
- `/Scoreboard/api/default-players/**` - Player CRUD
- `/Scoreboard/api/groups/**` - Group management

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

## Troubleshooting

**Module not loading (404 on /Scoreboard/)**: Clear NuGet cache with `dotnet nuget locals all --clear`, verify package exists in C:\LocalNuGet

**Blob storage errors**: Ensure Azure Storage Emulator is running, check connection string in appsettings.json

**Static assets not found**: Verify files exist in wwwroot/, check browser DevTools network tab

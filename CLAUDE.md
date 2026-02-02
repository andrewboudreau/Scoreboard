# Scoreboard - Architecture Overview and Development Guide

This document provides a comprehensive technical overview of the Scoreboard application architecture, implementation details, and development guidelines.

## Architecture Overview

The Scoreboard application demonstrates the SharedTools module system, implementing a complete web application as a dynamically loadable module. The architecture separates concerns between the module (feature implementation) and the host (infrastructure).

### Key Components

#### 1. Scoreboard.App (The Module)

The core scoreboard functionality packaged as a SharedTools module:

- **Module Entry Point**: `ScoreboardModule.cs` implements `IApplicationPartModule`
- **API Layer**: `ScoreboardApiMethods.cs` provides HTTP endpoints
- **Client Assets**: HTML, CSS, JavaScript, and audio files in `wwwroot/`
- **Dependencies**: Azure.Storage.Blobs for persistent storage

#### 2. Scoreboard.Web (The Host)

A minimal ASP.NET Core application that loads and hosts modules:

- **Program.cs**: Configures services and loads modules
- **Configuration**: Provides settings for loaded modules
- **Infrastructure**: Static file serving, routing, error handling

### Module Loading Process

1. **Discovery**: Host reads module names from configuration
2. **Download**: NuGet packages fetched from configured sources
3. **Loading**: Assemblies loaded into isolated context
4. **Integration**: Module registered with ASP.NET Core
5. **Activation**: Services configured and endpoints mapped

## Implementation Details

### ScoreboardModule Class

```csharp
public class ScoreboardModule : IApplicationPartModule
{
    public string Name => "Scoreboard";
    
    public void ConfigureServices(IServiceCollection services)
    {
        // Service registration
    }
    
    public void Configure(WebApplication app)
    {
        // Endpoint mapping
    }
}
```

Key responsibilities:
- Register Azure Blob Storage client
- Configure rate limiting policy
- Map API endpoints with proper prefixes
- Set up redirects for user-friendly URLs

### API Design

The module exposes RESTful endpoints:

- **POST /Scoreboard/api/games/share**
  - Creates a shareable link for a game
  - Stores share mapping in blob storage
  - Returns share code and URL

- **GET /Scoreboard/api/shares/{code}**
  - Retrieves shared game data (no auth required)
  - Reads game blob via server's storage client

- **Group management** endpoints for create, join, members, SAS refresh
- **Player management** endpoints for default player CRUD

### Client-Side Architecture

The frontend is a single-page application:

- **index.html**: Main scoreboard UI
- **game.html**: Read-only shared game results page
- **stats.html**: Self-contained game history page (list + detail views)
- **app.js**: Game logic, state management, SyncManager, and consolidated blob sync
- **styles.css**: Responsive design
- **Audio feedback**: Buzzer sounds for events

### Static Asset Serving

Assets are embedded in the module assembly and served via:
- Path pattern: `/_content/Scoreboard/*`
- Configured via `StaticWebAssetBasePath` in .csproj
- No extraction required - served from memory

## Development Guidelines

### Module Development Workflow

1. **Make changes** to Scoreboard.App
2. **Pack locally**: `dotnet pack -c Debug`
3. **Test in host**: Run Scoreboard.Web
4. **Iterate**: Changes require repack and restart

### Best Practices

#### 1. Module Isolation

- Keep all module code in Scoreboard.App
- Don't reference host-specific code
- Use configuration for environment-specific values

#### 2. Dependency Management

- Framework reference: `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
- SharedTools.Web: Always use `<PrivateAssets>all</PrivateAssets>`
- Other dependencies: Standard PackageReference

#### 3. API Design

- Prefix all endpoints with module name: `/Scoreboard/`
- Use rate limiting for public endpoints
- Return proper HTTP status codes

#### 4. Configuration

- Read from IConfiguration in ConfigureServices
- Don't hardcode connection strings
- Provide sensible defaults

#### 5. Error Handling

- Log errors appropriately
- Return user-friendly error messages
- Handle missing configuration gracefully

### Testing Strategy

1. **Unit Tests**: Test API methods in isolation
2. **Integration Tests**: Test module loading in host
3. **Playwright Tests**: `tests/Scoreboard.Tests.Playwright/` — NUnit + Playwright end-to-end tests covering core scoreboard features (scoring, timer, players, periods, settings). Run with `dotnet test`. Set `SCOREBOARD_BASE_URL` env var to point at the running app.
4. **Manual Testing**: Verify UI functionality
5. **Performance Testing**: Validate rate limiting

### Debugging Tips

1. **Enable detailed logging**:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "SharedTools.Web.Modules": "Debug",
         "Scoreboard": "Debug"
       }
     }
   }
   ```

2. **Check module loading**:
   - Look for "Loading module: Scoreboard.App" in logs
   - Verify assembly is in C:\LocalNuGet

3. **Validate endpoints**:
   - Check that routes are registered
   - Test API endpoints directly

4. **Asset serving issues**:
   - Verify StaticWebAssetBasePath setting
   - Check browser network tab for 404s

## Common Issues and Solutions

### Module Not Loading

**Symptoms**: 404 on /Scoreboard/
**Solutions**:
- Clear NuGet cache: `dotnet nuget locals all --clear`
- Verify module name in appsettings.json
- Check package exists in local feed

### Blob Storage Errors

**Symptoms**: 500 errors on upload
**Solutions**:
- Verify connection string in appsettings.json
- Ensure container name is valid
- Check Azure Storage Emulator is running

### Static Assets Not Found

**Symptoms**: CSS/JS not loading
**Solutions**:
- Verify files in wwwroot folder
- Check StaticWebAssetBasePath in .csproj
- Ensure paths use /_content/Scoreboard/

### Rate Limiting Issues

**Symptoms**: 429 Too Many Requests
**Solutions**:
- Adjust rate limit in ScoreboardModule
- Test with different IP addresses
- Check rate limiter configuration

## Future Enhancements

### Potential Improvements

1. **Multi-game Support**: Track multiple games simultaneously
2. **Player Profiles**: Save player statistics over time
3. **Game Templates**: Predefined scoring rules for different games
4. **Real-time Sync**: WebSockets for multi-device synchronization
5. **Export Features**: Download game history as CSV/PDF

### Module System Enhancements

1. **Hot Reload**: Reload modules without restart
2. **Version Management**: Support multiple module versions
3. **Module UI**: Admin interface for module management
4. **Health Checks**: Monitor module status

## Performance Considerations

### Current Optimizations

- **Embedded Resources**: No file extraction overhead
- **Rate Limiting**: Prevents API abuse
- **Minimal Host**: Lightweight infrastructure
- **Async Operations**: Non-blocking blob storage calls

### Monitoring

Key metrics to track:
- Module load time
- API response times
- Blob storage latency
- Rate limit hit frequency

## Security Considerations

1. **Input Validation**: Sanitize game history data
2. **Rate Limiting**: Prevent DoS attacks
3. **CORS Policy**: Configure appropriately for production
4. **Blob Storage**: Use SAS tokens in production
5. **HTTPS**: Always use in production

## Deployment

### Local Development

```bash
cd src/Scoreboard.Web
dotnet run
```

### Production Deployment

1. Configure production blob storage
2. Set appropriate rate limits
3. Enable HTTPS redirection
4. Configure logging providers
5. Set up monitoring/alerting

### Docker Support

Create Dockerfile for containerized deployment:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY published/ .
ENTRYPOINT ["dotnet", "Scoreboard.Web.dll"]
```

## Module Publishing

### Version Management

Use Directory.Build.props for centralized versioning:
```xml
<PropertyGroup>
  <VersionPrefix>1.0.0</VersionPrefix>
  <Authors>Your Name</Authors>
  <Company>Your Company</Company>
</PropertyGroup>
```

### GitHub Actions

Automated publishing via workflows:
- **build.yml**: CI validation
- **tag-and-publish.yml**: Release to NuGet

## Conclusion

The Scoreboard application showcases the power of the SharedTools module system, demonstrating how complex web applications can be packaged as reusable, dynamically loadable components. This architecture enables rapid development, easy distribution, and flexible deployment scenarios.
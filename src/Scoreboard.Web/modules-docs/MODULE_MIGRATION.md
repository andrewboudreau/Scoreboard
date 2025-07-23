# Migration Guide: Converting ASP.NET Core Projects to SharedTools Modules

This guide helps you convert existing ASP.NET Core projects into SharedTools modules that can be dynamically loaded from NuGet packages.

## Quick Reference for Claude Code

**Module Creation Checklist:**
1. Update .csproj: Change SDK, add SharedTools.Web, configure packaging
2. Create module class implementing IApplicationPartModule
3. Update namespaces to match module name
4. Fix static asset paths to use /_content/{ModuleName}/
5. Update API paths to /{ModuleName}/api/
6. Clean up old Program.cs and extension files
7. Create host application for testing

**Common Patterns:**
- Module namespace: `{ModuleName}` (without "Module" suffix)
- Package ID: `{ModuleName}Module` or just `{ModuleName}`
- Entry point: `/{ModuleName}/`
- API endpoints: `/{ModuleName}/api/*`
- Static assets: `/_content/{PackageId}/*`

## Overview

SharedTools modules are self-contained ASP.NET Core components that:
- Implement the `IApplicationPartModule` interface
- Are packaged as NuGet packages
- Can include Minimal APIs, Razor Pages, static assets, and dependencies
- Are loaded dynamically at application startup

## Migration Steps

### 1. Project File Configuration

#### For Projects WITHOUT Razor Pages (Minimal API only)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>YourModuleName</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Framework reference for ASP.NET Core -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    
    <!-- SharedTools.Web reference (not included in package) -->
    <PackageReference Include="SharedTools.Web" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- Your other dependencies (will be included in package) -->
    <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
  </ItemGroup>

  <!-- Include static assets as embedded resources -->
  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>
</Project>
```

#### For Projects WITH Razor Pages

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>YourModuleName</PackageId>
    <Version>1.0.0</Version>
    
    <!-- Razor compilation settings -->
    <RazorCompileOnBuild>true</RazorCompileOnBuild>
    <RazorCompileOnPublish>true</RazorCompileOnPublish>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    
    <PackageReference Include="SharedTools.Web" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
  </ItemGroup>

  <!-- Include static assets as embedded resources -->
  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>
</Project>
```

#### Important: Static Asset Configuration

**Embedded resources are the only supported approach for static assets.** All modules must use embedded resources for their static content:

```xml
<EmbeddedResource Include="wwwroot\**\*" />
```

This approach provides:
- Single file deployment - everything in the DLL
- No extraction step - faster module loading  
- Atomic deployment - guaranteed file availability
- Better for containerized environments

**Do not use StaticWebAssetBasePath** as static web assets extraction has been removed from the module loading system.

### 2. Implement IApplicationPartModule

Create a module class that implements `IApplicationPartModule`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.Web.Modules;

namespace YourModuleName;

public class YourModule : IApplicationPartModule
{
    public string Name => "Your Module Display Name";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register your services
        services.AddSingleton<IYourService, YourService>();
        
        // If using Azure Blob Storage
        services.Configure<YourBlobStorageOptions>(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.ContainerName = "your-container";
        });
        services.AddSingleton<IYourBlobStorage, YourBlobStorage>();
        
        // If using Razor Pages
        services.AddRazorPages();
    }

    public void Configure(WebApplication app)
    {
        // Map your Minimal API endpoints
        app.MapGet("/YourModuleName/", () => "Welcome to Your Module!");
        
        app.MapGet("/YourModuleName/api/data", async (IYourService service) =>
        {
            var data = await service.GetDataAsync();
            return Results.Ok(data);
        });
        
        // If using Razor Pages, they're automatically mapped
        // through the ApplicationPartManager
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Usually empty - the framework handles this
        // Override only if you need custom ApplicationPart configuration
    }
}
```

### 3. Organize Static Assets

Place static files in the `wwwroot` folder:

```
YourModuleName/
├── wwwroot/
│   ├── css/
│   │   └── module.css
│   ├── js/
│   │   └── module.js
│   └── images/
│       └── logo.png
├── YourModule.cs
└── YourModuleName.csproj
```

These will be served from `/_content/YourModuleName/css/module.css` etc.

**Important**: Always use the `/_content/{ModuleName}/` URL pattern to reference static assets. The SharedTools framework automatically maps embedded resources and static files to this location. Do NOT try to serve embedded resources directly through custom endpoints.

### 4. Testing Your Module

1. Build and pack your module:
   ```bash
   dotnet pack -c Debug
   ```

2. Create a test host application:
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   
   // Load your module
   await builder.AddApplicationPartModules(["YourModuleName"]);
   
   var app = builder.Build();
   
   app.UseStaticFiles();
   app.UseApplicationPartModules();
   
   app.Run();
   ```

3. Run and navigate to `/{YourModuleName}/`

### 5. Best Practices

1. **Keep Modules Focused**: One feature area per module
2. **Minimize Dependencies**: Only essential packages
3. **Use Conventions**: Follow naming and routing patterns
4. **Isolate Configuration**: Module-specific settings only
5. **Document Dependencies**: List all required services
6. **Test in Isolation**: Verify module works standalone
7. **Version Carefully**: Follow semantic versioning
8. **Always use `/_content/{ModuleName}/` for static assets**: Don't create custom endpoints

## Example Module Structure

```
YourModuleName/
├── wwwroot/
│   ├── css/
│   ├── js/
│   └── images/
├── Pages/ (optional)
│   └── YourModuleName/
│       └── Index.cshtml
├── Services/
│   ├── IYourService.cs
│   └── YourService.cs
├── YourModule.cs
├── YourModuleName.csproj
└── README.md
```

This structure provides a clean, self-contained module that can be easily packaged and loaded by SharedTools.
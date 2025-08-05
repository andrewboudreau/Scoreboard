# SharedTools Module Template

This module is built using [SharedTools](https://github.com/andrewboudreau/SharedTools) - a dynamic web module loading library for ASP.NET Core.

## Overview

SharedTools enables ASP.NET Core applications to dynamically load and integrate new features at runtime. This module is packaged as a standard NuGet package and can be loaded by any SharedTools-compatible host application.

## Features

- **Runtime Loading**: Module is loaded dynamically from NuGet at application startup
- **Assembly Isolation**: Prevents version conflicts with host application and other modules
- **Static Assets**: Embedded static files served at `/_content/{ModuleName}/`
- **Dependency Injection**: Full DI container integration with the host application

## Getting Started

### Prerequisites

- .NET 10.0 or later
- ASP.NET Core host application with SharedTools.Web

### Installation

Add this module to your SharedTools host application:

```csharp
// In your host application's Program.cs
await builder.AddApplicationPartModules(["YourModuleName"]);

var app = builder.Build();
app.UseApplicationPartModules();
```

### Configuration

Configure this module in your host application's `appsettings.json`:

```json
{
  "Modules": {
    "YourModuleName": {
      "Setting1": "value",
      "Setting2": 42
    }
  }
}
```

## Usage

Once loaded, this module will be available at:

- **Entry Point**: `/{ModuleName}/`
- **API Endpoints**: `/{ModuleName}/api/*`
- **Static Assets**: `/_content/{ModuleName}/*`

## Development

### Local Development

1. **Pack the module**:
   ```bash
   dotnet pack -c Debug
   ```

2. **Set up local NuGet feed**:
   ```xml
   <!-- nuget.config -->
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <add key="Local" value="C:\LocalNuGet" />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
     </packageSources>
   </configuration>
   ```

3. **Test in host application**:
   ```bash
   cd ../YourHostApp
   dotnet run
   ```

### Building

```bash
# Build the module
dotnet build

# Pack for distribution
dotnet pack -c Release

# Run tests
dotnet test
```

### Project Structure

```
YourModuleName/
├── wwwroot/                    # Static assets (embedded)
│   ├── css/
│   ├── js/
│   └── images/
├── Pages/ (optional)           # Razor Pages
│   └── YourModuleName/
├── Services/                   # Business logic
│   ├── IYourService.cs
│   └── YourService.cs
├── Models/                     # Data models
├── YourModule.cs               # IApplicationPartModule implementation
├── YourModuleName.csproj       # Project configuration
└── README.md                   # This file
```

## API Reference

### Module Implementation

This module implements the `IApplicationPartModule` interface:

```csharp
public class YourModule : IApplicationPartModule
{
    public string Name => "Your Module Display Name";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register module services
    }

    public void Configure(WebApplication app)
    {
        // Configure endpoints and middleware
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Optional: Advanced configuration
    }
}
```

### Configuration Options

```csharp
public class YourModuleOptions
{
    public const string SectionName = "YourModuleName";
    
    [Required]
    public string RequiredSetting { get; set; } = string.Empty;
    
    public int OptionalSetting { get; set; } = 10;
}
```

## Static Assets

Static assets are embedded in the module and served automatically:

- CSS files: `/_content/{ModuleName}/css/`
- JavaScript: `/_content/{ModuleName}/js/`
- Images: `/_content/{ModuleName}/images/`

Reference them in your Razor Pages or HTML:

```html
<link rel="stylesheet" href="~/_content/YourModuleName/css/styles.css" />
<script src="~/_content/YourModuleName/js/module.js"></script>
<img src="~/_content/YourModuleName/images/logo.png" />
```

## Dependencies

This module depends on:

- **SharedTools.Web**: Core module loading functionality (build-time only)
- **Microsoft.AspNetCore.App**: ASP.NET Core framework (provided by host)
- Add your module-specific dependencies here

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- Documentation: [SharedTools Documentation](https://github.com/andrewboudreau/SharedTools)
- Issues: [GitHub Issues](https://github.com/yourusername/YourModuleName/issues)
- Discussions: [GitHub Discussions](https://github.com/andrewboudreau/SharedTools/discussions)
# Build script for Scoreboard SharedTools Module
param(
    [Parameter(Position=0)]
    [ValidateSet('Build', 'Pack', 'Clean', 'Run')]
    [string]$Target = 'Build',
    
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Write-Header {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

switch ($Target) {
    'Clean' {
        Write-Header "Cleaning solution"
        dotnet clean
        if (Test-Path "artifacts") {
            Remove-Item -Path "artifacts" -Recurse -Force
        }
        if (Test-Path "C:\LocalNuGet\ScoreboardModule.*.nupkg") {
            Remove-Item -Path "C:\LocalNuGet\ScoreboardModule.*.nupkg" -Force
        }
    }
    
    'Build' {
        Write-Header "Building Scoreboard Module"
        dotnet restore
        dotnet build src/Scoreboard.csproj --configuration $Configuration
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Build succeeded!" -ForegroundColor Green
            
            if ($Configuration -eq 'Debug') {
                Write-Host "Package created at: C:\LocalNuGet\" -ForegroundColor Yellow
            }
        }
    }
    
    'Pack' {
        Write-Header "Packing Scoreboard Module"
        dotnet restore
        dotnet build src/Scoreboard.csproj --configuration Release --no-restore
        
        # For explicit packing (CI scenario)
        $env:CI = "true"
        dotnet pack src/Scoreboard.csproj --configuration Release --no-build --output ./artifacts
        $env:CI = $null
        
        Write-Host "Packages created in ./artifacts/" -ForegroundColor Green
    }
    
    'Run' {
        Write-Header "Running Scoreboard Host"
        
        # First ensure the module is built
        Write-Host "Building module..." -ForegroundColor Gray
        dotnet build src/Scoreboard.csproj --configuration Debug
        
        # Run the host
        Write-Host "Starting host application..." -ForegroundColor Gray
        Set-Location ScoreboardHost
        dotnet run
        Set-Location ..
    }
}

# Show usage
if ($Target -eq 'Build' -and $Configuration -eq 'Debug') {
    Write-Host "`nUsage examples:" -ForegroundColor Yellow
    Write-Host "  .\build.ps1              # Build in Debug mode"
    Write-Host "  .\build.ps1 Pack         # Create NuGet package"
    Write-Host "  .\build.ps1 Run          # Run the host application"
    Write-Host "  .\build.ps1 Clean        # Clean all outputs"
}
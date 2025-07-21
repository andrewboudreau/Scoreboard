#!/bin/bash

# Build script for Scoreboard SharedTools Module

# Default values
TARGET=${1:-Build}
CONFIGURATION=${2:-Debug}

# Colors for output
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Error handling
set -e

write_header() {
    echo -e "\n${CYAN}==== $1 ====${NC}"
}

case "$TARGET" in
    clean|Clean)
        write_header "Cleaning solution"
        dotnet clean
        rm -rf artifacts
        rm -f /c/LocalNuGet/ScoreboardModule.*.nupkg 2>/dev/null || true
        ;;
    
    build|Build)
        write_header "Building Scoreboard Module"
        dotnet restore
        dotnet build src/Scoreboard.csproj --configuration $CONFIGURATION
        
        if [ $? -eq 0 ]; then
            echo -e "${GREEN}Build succeeded!${NC}"
            
            if [ "$CONFIGURATION" = "Debug" ]; then
                echo -e "${YELLOW}Package created at: C:\\LocalNuGet\\${NC}"
            fi
        fi
        ;;
    
    pack|Pack)
        write_header "Packing Scoreboard Module"
        dotnet restore
        dotnet build src/Scoreboard.csproj --configuration Release --no-restore
        
        # For explicit packing (CI scenario)
        CI=true dotnet pack src/Scoreboard.csproj --configuration Release --no-build --output ./artifacts
        
        echo -e "${GREEN}Packages created in ./artifacts/${NC}"
        ;;
    
    run|Run)
        write_header "Running Scoreboard Host"
        
        # First ensure the module is built
        echo -e "${GRAY}Building module...${NC}"
        dotnet build src/Scoreboard.csproj --configuration Debug
        
        # Run the host
        echo -e "${GRAY}Starting host application...${NC}"
        cd ScoreboardHost
        dotnet run
        cd ..
        ;;
    
    *)
        echo "Usage: ./build.sh [target] [configuration]"
        echo "Targets: Build, Pack, Run, Clean"
        echo "Configurations: Debug, Release"
        echo ""
        echo "Examples:"
        echo "  ./build.sh              # Build in Debug mode"
        echo "  ./build.sh Pack         # Create NuGet package"
        echo "  ./build.sh Run          # Run the host application"
        echo "  ./build.sh Clean        # Clean all outputs"
        exit 1
        ;;
esac

# Show usage for default build
if [ "$TARGET" = "Build" ] && [ "$CONFIGURATION" = "Debug" ]; then
    echo -e "\n${YELLOW}Usage examples:${NC}"
    echo "  ./build.sh              # Build in Debug mode"
    echo "  ./build.sh Pack         # Create NuGet package"
    echo "  ./build.sh Run          # Run the host application"
    echo "  ./build.sh Clean        # Clean all outputs"
fi
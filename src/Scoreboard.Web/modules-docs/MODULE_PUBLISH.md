# Publishing SharedTools Modules to NuGet

This guide explains how to set up automated NuGet package publishing using GitHub Actions for SharedTools modules.

## Overview

The publishing process uses two GitHub Actions workflows:
1. **Tag and Publish** - Manual workflow to create a git tag and publish to NuGet
2. **CI Build** - Automatic workflow on push to validate the build

## Prerequisites

1. **NuGet API Key**: Get from https://www.nuget.org/account/apikeys
2. **GitHub Repository Secrets**: Add your NuGet API key as `NUGET_API_KEY`
3. **Version Management**: Use Directory.Build.props for centralized versioning

## Setting Up Directory.Build.props

Create a `Directory.Build.props` file in your repository root to centralize version management:

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>
    <Authors>Your Name</Authors>
    <Company>Your Company</Company>
    <Product>YourModuleName</Product>
    <Copyright>Copyright © 2025</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/yourusername/YourModuleName</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yourusername/YourModuleName.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>modules;aspnetcore</PackageTags>
  </PropertyGroup>
</Project>
```

## GitHub Actions Workflows

### 1. Tag and Publish Workflow

Create `.github/workflows/tag-and-publish.yml`:

```yaml
name: Tag and Publish

on:
  workflow_dispatch:

jobs:
  tag_and_publish:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      packages: write
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Get version
        id: get_version
        run: |
          file=Directory.Build.props
          version=$(grep -oP '(?<=<VersionPrefix>).*?(?=</VersionPrefix>)' "$file")
          echo "version=$version" >> "$GITHUB_OUTPUT"

      - name: Create Git Tag
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git tag "v${{ steps.get_version.outputs.version }}"
          git push origin "v${{ steps.get_version.outputs.version }}"

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build project
        run: dotnet build --configuration Release --no-restore

      - name: Pack YourModuleName
        run: dotnet pack YourModuleName/YourModuleName.Web.csproj --configuration Release --no-build --output ./artifacts

      - name: Pack Modules
        run: |
          # Pack each module project
          for project in $(find . -name "*.csproj" -path "*/Modules/*" -o -name "*Module.csproj"); do
            echo "Packing $project"
            dotnet pack "$project" --configuration Release --no-build --output ./artifacts
          done

      - name: Push to NuGet
        run: |
          for package in ./artifacts/*.nupkg; do
            echo "Pushing $package"
            dotnet nuget push "$package" --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
          done

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v1
        with:
          tag_name: v${{ steps.get_version.outputs.version }}
          name: Release v${{ steps.get_version.outputs.version }}
          draft: false
          prerelease: false
          files: |
            ./artifacts/*.nupkg
            ./artifacts/*.snupkg
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### 2. CI Build Workflow

Create `.github/workflows/build.yml`:

```yaml
name: Bump Version

on:
  push:
    branches:
      - main

jobs:
  bump_version:
    runs-on: ubuntu-latest
    # This job only runs for pushes to main, not for tags or bot commits
    if: github.actor != 'github-actions[bot]'
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore dependencies
        run: dotnet restore --configfile nuget.ci.config

      - name: Build
        run: dotnet build --no-restore --configfile nuget.ci.config

      - name: Bump version
        id: bump
        run: |
          file=Directory.Build.props
          version=$(grep -oP '(?<=<VersionPrefix>).*?(?=</VersionPrefix>)' "$file")
          IFS='.' read -r major minor patch <<< "$version"
          patch=$((patch + 1))
          new_version="$major.$minor.$patch"
          sed -i "s/<VersionPrefix>$version<\/VersionPrefix>/<VersionPrefix>$new_version<\/VersionPrefix>/" "$file"
          echo "new_version=$new_version" >> "$GITHUB_OUTPUT"

      - name: Commit version bump
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add Directory.Build.props
          git commit -m "chore: bump version to ${{ steps.bump.outputs.new_version }}"
          git push
```

## Publishing Process

### Publishing All Packages

1. Update version in `Directory.Build.props`
2. Commit and push changes
3. Go to Actions → Tag and Publish → Run workflow
4. Workflow will:
   - Create git tag
   - Build all projects
   - Pack all packages
   - Push to NuGet
   - Create GitHub release

### Publishing Individual Module

1. Go to Actions → Publish Module → Run workflow
2. Enter module name (e.g., `ExampleWebModule`)
3. Optionally add version suffix for pre-release
4. Workflow will build and publish just that module

## Best Practices

1. **Version Management**:
   - Use semantic versioning
   - Update Directory.Build.props before publishing
   - Consider pre-release suffixes for beta versions

2. **Package Metadata**:
   - Add README.md to each module
   - Include package icon
   - Write clear package descriptions

3. **Dependencies**:
   - Keep SharedTools.Web dependency with `PrivateAssets="all"`
   - Minimize external dependencies
   - Use version ranges carefully

4. **Testing**:
   - Run CI build before publishing
   - Test packages locally first
   - Consider integration tests

## Local Testing

Before publishing, test packages locally:

```bash
# Pack locally
dotnet pack -c Release -o ./local-packages

# Test in a new project
dotnet new web -n TestHost
cd TestHost
dotnet add package YourModule --source ../local-packages
```

This ensures packages work correctly before publishing to NuGet.org.
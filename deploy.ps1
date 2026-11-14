#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes the Scoreboard module (src/Scoreboard), uploads it to a module host as a drop, and waits until the host reports it.

.DESCRIPTION
    1. dotnet publish -c Release into ./out
    2. zip ./out
    3. PUT the zip to {HostUrl}/api/modules/{Id} with X-Api-Key
    4. poll GET {HostUrl}/api/modules/{Id} (tolerating errors while the host restarts) until it reports
       this drop as loaded or failed

    The upload key is read from $env:MODULE_UPLOAD_KEY, else from MODULE_UPLOAD_KEY=... in a .env file
    in this folder or the repository root. Never commit it: it lets the holder run code on the host.

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -HostUrl http://localhost:5000
#>
[CmdletBinding()]
param(
    # The module's id on the host: lowercase letters, digits and dashes.
    [string]$Id = "scoreboard",

    # The module host. Defaults to $env:MODULE_HOST_URL, then https://modules.blogfor.net.
    [string]$HostUrl = $(if ($env:MODULE_HOST_URL) { $env:MODULE_HOST_URL } else { "https://modules.blogfor.net" }),

    # How long to wait for the host to restart and report the drop.
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-UploadKey {
    if ($env:MODULE_UPLOAD_KEY) { return $env:MODULE_UPLOAD_KEY }

    $candidates = @(Join-Path $PSScriptRoot ".env")
    $repoRoot = git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $repoRoot) { $candidates += Join-Path $repoRoot ".env" }

    foreach ($file in $candidates | Select-Object -Unique) {
        if (Test-Path $file) {
            $line = Get-Content $file | Where-Object { $_ -match '^\s*MODULE_UPLOAD_KEY\s*=' } | Select-Object -First 1
            if ($line) { return ($line -split '=', 2)[1].Trim().Trim('"', "'") }
        }
    }

    throw "MODULE_UPLOAD_KEY is not set. Put it in the environment or in a gitignored .env file."
}

if ($Id -notmatch '^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$') {
    throw "Id '$Id' must be lowercase letters, digits and dashes."
}

$key = Get-UploadKey
$headers = @{ "X-Api-Key" = $key }
$HostUrl = $HostUrl.TrimEnd('/')

# 1. Publish
$out = Join-Path $PSScriptRoot "out"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Write-Host "Publishing to $out"
dotnet publish (Join-Path $PSScriptRoot "src/Scoreboard/Scoreboard.csproj") -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# 2. Zip (forward-slash entry names on every platform)
$zip = Join-Path ([System.IO.Path]::GetTempPath()) "$Id-$([Guid]::NewGuid().ToString('N')).zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("Drop: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))

# 3. Upload
try {
    $response = Invoke-RestMethod -Method Put -Uri "$HostUrl/api/modules/$Id" -Headers $headers `
        -ContentType "application/zip" -InFile $zip
}
finally {
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}
$drop = $response.drop
Write-Host "Uploaded drop $drop; waiting for the host to restart"

# 4. Poll until the host reports this drop
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$status = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    try {
        $status = Invoke-RestMethod -Uri "$HostUrl/api/modules/$Id" -Headers $headers
    }
    catch {
        continue # 502 or connection refused while the host restarts
    }
    if ($status.drop -eq $drop -and $status.status -in @("loaded", "failed")) { break }
}

if (-not $status -or $status.drop -ne $drop -or $status.status -notin @("loaded", "failed")) {
    throw "Timed out after $TimeoutSeconds s waiting for drop $drop. Check $HostUrl/_modules."
}

if ($status.status -eq "failed") {
    Write-Host "Drop $drop FAILED during $($status.phase): $($status.error)" -ForegroundColor Red
    Write-Host "Serving: $(if ($status.serving) { $status.serving } else { 'nothing' })"
    exit 1
}

Write-Host "Drop $drop loaded and serving." -ForegroundColor Green

<#
.SYNOPSIS
Runs a cross-compiled Linux AOT probe inside Docker.

.EXAMPLE
./scripts/run-aot-docker.ps1 -RuntimeIdentifier linux-x64

.EXAMPLE
./scripts/run-aot-docker.ps1 -RuntimeIdentifier linux-musl-arm64 -OutputDirectory artifacts/aot/linux-musl-arm64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64')]
    [string]$RuntimeIdentifier,

    [string]$OutputDirectory = "",
    [string]$Image = ""
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "artifacts/aot/$RuntimeIdentifier"
}

if (-not $Image) {
    $Image = if ($RuntimeIdentifier.StartsWith('linux-musl-')) {
        'mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine'
    }
    else {
        'mcr.microsoft.com/dotnet/runtime-deps:8.0'
    }
}

if (-not (Test-Path (Join-Path $OutputDirectory 'SiameseCat.AotProbe'))) {
    throw "The AOT probe was not found in '$OutputDirectory'. Publish it first."
}

$source = (Resolve-Path $OutputDirectory).Path
$platform = if ($RuntimeIdentifier.EndsWith('arm64')) { 'linux/arm64' } else { 'linux/amd64' }

Write-Host "Running $RuntimeIdentifier probe in $Image ($platform)"

# Docker writes pull progress to stderr; merge it so Windows PowerShell does not treat it as a terminating error.
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $dockerOutput = & docker run --rm --platform $platform --mount "type=bind,source=$source,target=/app,readonly" --workdir /app $Image ./SiameseCat.AotProbe 2>&1
    $dockerExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

$dockerOutput | ForEach-Object { Write-Host $_ }

if ($dockerExitCode -ne 0) {
    throw "AOT probe failed in Docker for $RuntimeIdentifier."
}

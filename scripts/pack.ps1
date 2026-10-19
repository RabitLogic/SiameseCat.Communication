<#
.SYNOPSIS
    Builds the six SiameseCat NuGet packages and verifies their layout.

.DESCRIPTION
    Restores and builds the solution in Release, packs every library into -Output, checks that each
    package carries both target frameworks plus the bundled README, and prints size and SHA256 so the
    artefacts can be attached to a release or pushed to a feed.

.PARAMETER Version
    Optional package version override (for example 1.0.0-preview.1). Defaults to the version in the
    project files.

.PARAMETER Output
    Output folder. Defaults to artifacts/.

.PARAMETER Push
    Optional NuGet source (URL or local folder) to push the .nupkg and .snupkg files to.
    The API key is read from NUGET_API_KEY when pushing to nuget.org.

.EXAMPLE
    ./scripts/pack.ps1

.EXAMPLE
    ./scripts/pack.ps1 -Version 1.0.0-preview.1

.EXAMPLE
    $env:NUGET_API_KEY = '...'; ./scripts/pack.ps1 -Push https://api.nuget.org/v3/index.json
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Output,
    [string] $Push,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptRoot '..')).Path

if (-not $Output) {
    $Output = Join-Path $root 'artifacts'
}

$projects = @('SiameseCat.Communication')
if (Test-Path $Output) {
    Remove-Item -Recurse -Force $Output
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "Building $Configuration..." -ForegroundColor Cyan
dotnet build (Join-Path $root 'SiameseCat.slnx') --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'The build failed; no packages were produced.'
}

$versionArgs = @()
if ($Version) {
    $versionArgs = @("-p:Version=$Version", '-p:PackageValidationBaselineVersion=')
}

foreach ($project in $projects) {
    Write-Host "Packing $project..." -ForegroundColor Cyan
    $projectPath = Join-Path $root "src/$project/$project.csproj"
    dotnet pack $projectPath --configuration $Configuration --no-build --output $Output @versionArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Packing $project failed."
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = Get-ChildItem $Output -Filter *.nupkg | Sort-Object Name
$failures = 0

Write-Host ''
Write-Host 'Package layout' -ForegroundColor Cyan

$requiredFrameworks = @('net8.0', 'net10.0', 'netstandard2.0')

foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = $archive.Entries | ForEach-Object { $_.FullName }
    }
    finally {
        $archive.Dispose()
    }

    $missing = @()
    foreach ($tfm in $requiredFrameworks) {
        $found = @($entries | Where-Object { $_ -like "lib/$tfm/*.dll" })
        if ($found.Count -eq 0) {
            $missing += $tfm
        }
    }

    $readme = @($entries | Where-Object { $_ -eq 'README.md' }).Count
    $ok = $missing.Count -eq 0 -and $readme -eq 1

    if (-not $ok) {
        $failures++
    }

    $hash = (Get-FileHash $package.FullName -Algorithm SHA256).Hash
    $present = @()
    foreach ($tfm in $requiredFrameworks) {
        if ($missing -contains $tfm) {
            $present += "missing:$tfm"
        }
        else {
            $present += $tfm
        }
    }

    '{0,-42} {1,9:N1} KB  tfm:[{2}] readme:{3}  sha256:{4}' -f `
        $package.Name, ($package.Length / 1KB), ($present -join ' '), $readme, $hash.Substring(0, 16) | Write-Host
}

$symbols = @(Get-ChildItem $Output -Filter *.snupkg)

Write-Host ''
Write-Host ("{0} package(s), {1} symbol package(s) in {2}" -f @($packages).Count, $symbols.Count, $Output) -ForegroundColor Green

if ($failures -gt 0) {
    throw "$failures package(s) are missing an expected target framework or README.md."
}

Write-Host ''
Write-Host 'SHA256' -ForegroundColor Cyan
Get-ChildItem $Output -Filter *.nupkg | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name | Write-Host
}

if ($Push) {
    $apiKey = if ($env:NUGET_API_KEY) { $env:NUGET_API_KEY } else { 'azure' }
    $isRemoteFeed = $Push -match '^https?://'
    Write-Host ''
    Write-Host "Pushing to $Push..." -ForegroundColor Cyan

    foreach ($package in @($packages)) {
        dotnet nuget push $package.FullName --source $Push --api-key $apiKey --skip-duplicate
        if ($LASTEXITCODE -ne 0) {
            throw "Pushing $($package.Name) failed."
        }
    }

    # Symbol packages are only accepted by package servers (nuget.org, Artifactory, Azure Artifacts);
    # a plain folder feed stores just the .nupkg files.
    if ($isRemoteFeed) {
        foreach ($symbolsPackage in @($symbols)) {
            dotnet nuget push $symbolsPackage.FullName --source $Push --api-key $apiKey --skip-duplicate
            if ($LASTEXITCODE -ne 0) {
                throw "Pushing $($symbolsPackage.Name) failed."
            }
        }
    }
    else {
        Write-Host 'Folder feed detected: only .nupkg files are published; symbol packages stay in the output folder.' -ForegroundColor Yellow
    }

    Write-Host 'Push complete.' -ForegroundColor Green
}

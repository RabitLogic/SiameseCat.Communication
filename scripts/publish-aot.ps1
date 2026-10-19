<#
.SYNOPSIS
Publishes the SiameseCat Native AOT probe, including cross-OS targets.

.DESCRIPTION
Cross-compilation is provided by the PublishAotCross.Tools package (Zig for Linux/macOS,
xwin for Windows), so every supported runtime identifier can be produced from any host.

.EXAMPLE
./scripts/publish-aot.ps1

.EXAMPLE
./scripts/publish-aot.ps1 -All

.EXAMPLE
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64,linux-musl-x64 -Docker

.EXAMPLE
./scripts/publish-aot.ps1 -All -Run
#>
[CmdletBinding()]
param(
    [string[]]$RuntimeIdentifier = @(),
    [switch]$All,
    [string]$OutputDirectory = "",
    [switch]$Run,
    [switch]$Docker,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj'
$supported = @(
    'win-x64',
    'win-arm64',
    'linux-x64',
    'linux-arm64',
    'linux-musl-x64',
    'linux-musl-arm64',
    'osx-x64',
    'osx-arm64'
)

function Get-HostRuntimeIdentifier {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-$architecture"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return "osx-$architecture"
    }

    return "linux-$architecture"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts/aot'
}

if ($All) {
    $RuntimeIdentifier = $supported
}
elseif ($RuntimeIdentifier.Count -eq 0) {
    $RuntimeIdentifier = @(Get-HostRuntimeIdentifier)
}

$hostRid = Get-HostRuntimeIdentifier

foreach ($rid in $RuntimeIdentifier) {
    if ($supported -notcontains $rid) {
        throw "Unsupported runtime identifier '$rid'. Supported: $($supported -join ', ')."
    }

    $output = Join-Path $OutputDirectory $rid
    Write-Host "Publishing Native AOT for $rid -> $output"

    $arguments = @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', $rid,
            '-p:PublishAotCrossDebug=false',
        '--output', $output
    )

    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AOT publish failed for $rid."
    }

    if ($Docker -and $rid.StartsWith('linux-')) {
        & (Join-Path $PSScriptRoot 'run-aot-docker.ps1') -RuntimeIdentifier $rid -OutputDirectory $output
        if ($LASTEXITCODE -ne 0) {
            throw "AOT probe failed in Docker for $rid."
        }
    }
    elseif ($Run -and $rid -eq $hostRid) {
        $executable = if ($rid.StartsWith('win-')) {
            Join-Path $output 'SiameseCat.AotProbe.exe'
        }
        else {
            Join-Path $output 'SiameseCat.AotProbe'
        }

        & $executable
        if ($LASTEXITCODE -ne 0) {
            throw "AOT probe failed for $rid."
        }
    }
    elseif ($Run -and $rid -ne $hostRid) {
        Write-Host "Skipping execution of $rid because the host is $hostRid. Use -Docker for Linux targets."
    }
}

Write-Host "Native AOT publish completed for: $($RuntimeIdentifier -join ', ')."

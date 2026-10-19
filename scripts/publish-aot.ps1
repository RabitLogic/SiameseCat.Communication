<#
.SYNOPSIS
Publishes and optionally runs the SiameseCat Native AOT probe for the host runtime.

.DESCRIPTION
The probe is published for the runtime identifier of the current machine. Cross-OS publishing is not
part of the build: CI runs the probe natively on Windows, Linux and macOS.

.EXAMPLE
./scripts/publish-aot.ps1

.EXAMPLE
./scripts/publish-aot.ps1 -Run

.EXAMPLE
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64
#>
[CmdletBinding()]
param(
    [string[]]$RuntimeIdentifier = @(),
    [string]$OutputDirectory = "",
    [switch]$Run,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj'
$supported = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')

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

$hostRid = Get-HostRuntimeIdentifier
if ($RuntimeIdentifier.Count -eq 0) {
    $RuntimeIdentifier = @($hostRid)
}

foreach ($rid in $RuntimeIdentifier) {
    if ($supported -notcontains $rid) {
        throw "Unsupported runtime identifier '$rid'. Supported: $($supported -join ', ')."
    }

    $output = Join-Path $OutputDirectory $rid
    Write-Host "Publishing Native AOT for $rid -> $output"

    $arguments = @('publish', $project, '--configuration', 'Release', '--runtime', $rid, '--output', $output)
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AOT publish failed for $rid."
    }

    if (-not $Run) {
        continue
    }

    if ($rid -ne $hostRid) {
        Write-Host "Skipping execution of $rid because the host is $hostRid."
        continue
    }

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

Write-Host "Native AOT publish completed for: $($RuntimeIdentifier -join ', ')."

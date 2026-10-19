<#
.SYNOPSIS
    Stores the NuGet API key as the GitHub Actions secret used by the release workflow.

.DESCRIPTION
    Prompts for the key without echoing it, then hands it to `gh secret set`. The value never appears in
    the terminal, in the shell history or in this script's output.

    Requires an authenticated GitHub CLI (`gh auth login`) with admin access to the repository, and a
    NuGet.org API key that allows "Push new packages and package versions" for
    SiameseCat.Communication (https://www.nuget.org/account/apikeys).

.PARAMETER Repository
    Target repository as owner/name. Defaults to the origin remote of the current folder.

.PARAMETER SecretName
    Secret name. Defaults to NUGET_API_KEY, which is what .github/workflows/release.yml reads.

.PARAMETER ApiKey
    The key itself. Omit it to be prompted (recommended: a prompted value is not kept in the history).

.PARAMETER Verify
    Lists the repository secrets afterwards so you can confirm the name is present.

.EXAMPLE
    ./scripts/set-nuget-secret.ps1

.EXAMPLE
    ./scripts/set-nuget-secret.ps1 -Repository RabitLogic/SiameseCat -Verify
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Repository,
    [string] $SecretName = 'NUGET_API_KEY',
    [string] $ApiKey,
    [switch] $Verify
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is not installed. Install it from https://cli.github.com/ and run "gh auth login".'
}

& $env:ComSpec /c 'gh auth status >nul 2>&1'
$authCode = $LASTEXITCODE
if ($authCode -ne 0) {
    throw "The GitHub CLI is not authenticated. Run 'gh auth login' first, then re-run this script."
}

if (-not $Repository) {
    $remote = (& git config --get remote.origin.url) 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remote)) {
        throw 'Cannot detect the repository: pass -Repository owner/name or run this inside a clone with an origin remote.'
    }

    # Handles https://github.com/owner/name.git, git@github.com:owner/name.git and owner/name.
    $Repository = $remote.Trim()
    $Repository = $Repository -replace '^https?://[^/]+/', ''
    $Repository = $Repository -replace '^git@[^:]+:', ''
    $Repository = $Repository -replace '\.git$', ''
}

Write-Host "Repository : $Repository"
Write-Host "Secret     : $SecretName"

if (-not $ApiKey) {
    $secure = Read-Host -AsSecureString -Prompt 'Paste the NuGet API key (input is hidden)'
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $ApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw 'The API key is empty; nothing was stored.'
}

if (-not $PSCmdlet.ShouldProcess("$Repository ($SecretName)", 'Set GitHub Actions secret')) {
    return
}

try {
    $ApiKey | & gh secret set $SecretName --repo $Repository
    if ($LASTEXITCODE -ne 0) {
        throw "gh secret set failed with exit code $LASTEXITCODE."
    }
}
finally {
    $ApiKey = $null
}

Write-Host "Secret $SecretName stored for $Repository." -ForegroundColor Green
Write-Host 'Next: git tag v1.0.0; git push origin v1.0.0'

if ($Verify) {
    Write-Host ''
    Write-Host 'Repository secrets:' -ForegroundColor Cyan
    & gh secret list --repo $Repository
}

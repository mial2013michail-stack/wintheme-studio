<#
.SYNOPSIS
    Creates the GitHub repository if needed, pushes the source and publishes
    the release assets with GitHub CLI.

.DESCRIPTION
    Requires an authenticated GitHub CLI session:
        gh auth login
    The script never asks for or stores a password or token.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\publish-github.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoName = 'wintheme-studio',
    [ValidateSet('public', 'private')]
    [string]$Visibility = 'public',
    [string]$Tag = 'v1.2.0',
    [string]$Title = 'WinTheme Studio 1.2.0'
)

$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseDir = Join-Path $root 'release'
$notes = Join-Path $releaseDir 'RELEASE_NOTES.md'
$assets = @(
    (Join-Path $releaseDir 'WinThemeStudio.exe'),
    (Join-Path $releaseDir 'WinThemeStudio-1.2.0-win-x64.zip'),
    (Join-Path $releaseDir 'SHA256SUMS.txt')
)

if (-not (Test-Path -LiteralPath $notes)) {
    throw "Release notes not found: $notes"
}

foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset)) {
        throw "Release asset not found: $asset"
    }
}

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($ghCommand) { $ghCommand.Source } else { $null }
if (-not $gh) {
    $portable = Join-Path $env:LOCALAPPDATA 'GitHubCLI\bin\gh.exe'
    if (Test-Path -LiteralPath $portable) {
        $gh = $portable
    }
}

if (-not $gh) {
    $wingetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path -LiteralPath $wingetRoot) {
        $wingetGh = Get-ChildItem -LiteralPath $wingetRoot -Filter 'GitHub.cli_*' -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'bin\gh.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($wingetGh) {
            $gh = $wingetGh
        }
    }
}

if (-not $gh) {
    throw 'GitHub CLI not found. Install it with: winget install --id GitHub.cli -e'
}

& $gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run: gh auth login'
}

$login = (& $gh api user --jq .login).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($login)) {
    throw 'Could not read the GitHub account. Run: gh auth login'
}

if ([string]::IsNullOrWhiteSpace((& git -C $root config user.name))) {
    & git -C $root config user.name $login
}

if ([string]::IsNullOrWhiteSpace((& git -C $root config user.email))) {
    & git -C $root config user.email "$login@users.noreply.github.com"
}

$dirty = (& git -C $root status --porcelain)
if (-not [string]::IsNullOrWhiteSpace(($dirty -join ''))) {
    & git -C $root add .
    & git -C $root commit -m "WinTheme Studio $Tag"
}

$fullName = "$login/$RepoName"
$remote = (& git -C $root remote get-url origin 2>$null)
$hasRemote = $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($remote)

if (-not $hasRemote) {
    & $gh repo view $fullName --json name 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & git -C $root remote add origin "https://github.com/$fullName.git"
    } else {
        if ($Visibility -eq 'public') {
            & $gh repo create $RepoName --public --source $root --remote origin --push
        } else {
            & $gh repo create $RepoName --private --source $root --remote origin --push
        }

        if ($LASTEXITCODE -ne 0) {
            throw 'Could not create the GitHub repository.'
        }
    }
}

& git -C $root push -u origin HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'Could not push the source to GitHub.'
}

& $gh release view $Tag --repo $fullName --json url --jq .url 2>$null | Out-Null
$releaseExists = $LASTEXITCODE -eq 0

if ($releaseExists) {
    & $gh release upload $Tag @assets --repo $fullName --clobber
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not upload the release assets.'
    }

    & $gh release edit $Tag --repo $fullName --title $Title --notes-file $notes
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not update the release notes.'
    }
} else {
    & $gh release create $Tag @assets --repo $fullName --title $Title --notes-file $notes
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the release.'
    }
}

$url = (& $gh release view $Tag --repo $fullName --json url --jq .url).Trim()
Write-Host "Repository: https://github.com/$fullName"
Write-Host "Release:    $url"

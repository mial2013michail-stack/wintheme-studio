<#
.SYNOPSIS
    Publishes WinTheme Studio as one self-contained win-x64 executable.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src\WinThemeStudio\WinThemeStudio.csproj'
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'dist'
}
$outputPath = [IO.Path]::GetFullPath($Output)

$candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $candidate) {
    $dotnetPath = $candidate
} else {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $dotnetPath = $dotnet.Source
    } else {
        throw 'The .NET SDK was not found. Install .NET 10 SDK: https://dotnet.microsoft.com/'
    }
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Remove-Item -LiteralPath (Join-Path $outputPath 'WinThemeStudio.exe') -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $outputPath -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Publishing $Configuration / $Runtime..."
& $dotnetPath publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $outputPath 'WinThemeStudio.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published executable not found: $exe"
}

foreach ($document in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    $source = Join-Path $root $document
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $outputPath -Force
    }
}

$size = [Math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB)"
Write-Host "Smoke tests:"
Write-Host "  `"$exe`" --selftest `"$outputPath\qa`""
Write-Host "  `"$exe`" --screenshot `"$outputPath\screenshots`""

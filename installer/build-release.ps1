<#
.SYNOPSIS
    Builds the WharfBar release assets: the Inno Setup installer, the portable zip, and SHA-256
    checksums.

.DESCRIPTION
    One script for both a local release build and .github/workflows/release.yml. It publishes the
    app self-contained for Windows x64 (single-file WharfBar.exe, no .NET install needed), stages
    the portable zip, compiles installer/WharfBar.iss, and writes SHA256SUMS.txt.

    Requirements: Windows, the .NET SDK 8 or 9, and Inno Setup 6.3 or newer (ISCC.exe in its default
    install directory or on PATH).

    Output (artifacts/release/):
        WharfBar-Setup-<version>-x64.exe
        WharfBar-<version>-win-x64-portable.zip
        SHA256SUMS.txt

.PARAMETER Version
    Release version, SemVer without the leading "v": "1.0.0-beta.2", "1.0.0". A leading "v" is
    accepted and stripped. It is passed to MSBuild as Version + InformationalVersion (so About
    matches the tag) and to the installer as its display version.

.PARAMETER RuntimeIdentifier
    Publish runtime. Only win-x64 is supported for v1 (the AppBar/tray surface is x64-only here).

.PARAMETER OutputRoot
    Directory for the final assets. Defaults to artifacts/release.

.PARAMETER InnoSetupCompiler
    Explicit path to ISCC.exe. Defaults to the usual Inno Setup 6 install locations, then PATH.

.EXAMPLE
    pwsh installer/build-release.ps1 -Version 1.0.0-beta.2
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$OutputRoot,

    [string]$InnoSetupCompiler
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedVersion {
    param([string]$Raw)

    $normalized = $Raw.Trim()
    if ($normalized.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    $match = [regex]::Match(
        $normalized,
        '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$')
    if (-not $match.Success) {
        throw "Version '$Raw' is not SemVer. Expected MAJOR.MINOR.PATCH[-prerelease], e.g. 1.0.0-beta.2 or 1.0.0."
    }

    return [pscustomobject]@{
        SemVer  = $normalized
        Numeric = '{0}.{1}.{2}.0' -f $match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value
    }
}

function Get-InnoSetupCompiler {
    param([string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) {
            throw "ISCC.exe not found at '$Explicit'."
        }

        return (Resolve-Path -LiteralPath $Explicit).Path
    }

    $programFilesX86 = ${env:ProgramFiles(x86)}
    $candidates = @()
    if ($programFilesX86) {
        $candidates += (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe')
    }

    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $onPath = Get-Command -Name 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        return $onPath.Source
    }

    throw 'Inno Setup 6.3+ (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php, or pass -InnoSetupCompiler.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\release'
}

$versionInfo = Get-NormalizedVersion -Raw $Version
$semVer = $versionInfo.SemVer
$numericVersion = $versionInfo.Numeric

$projectPath = Join-Path $repoRoot 'src\WharfBar\WharfBar.csproj'
$publishDir = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier"
$stagingRoot = Join-Path $repoRoot 'artifacts\staging'
$portableBaseName = "WharfBar-$semVer-$RuntimeIdentifier-portable"
$setupBaseName = "WharfBar-Setup-$semVer-x64"
$portableStage = Join-Path $stagingRoot $portableBaseName
$portableZip = Join-Path $OutputRoot "$portableBaseName.zip"
$setupExe = Join-Path $OutputRoot "$setupBaseName.exe"
$checksumFile = Join-Path $OutputRoot 'SHA256SUMS.txt'

Write-Host "WharfBar release $semVer (numeric $numericVersion), $RuntimeIdentifier"

foreach ($directory in @($OutputRoot, $stagingRoot, $publishDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# --- Publish: self-contained single-file exe; Wallpapers\ stays a content folder next to it. ---
$publishArgs = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', $RuntimeIdentifier,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$semVer",
    "-p:InformationalVersion=$semVer",
    # Keep the version resources (and therefore About and the exe properties) exactly at the tag
    # instead of appending "+<git sha>" build metadata.
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    '-o', $publishDir
)

Write-Host "dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$appExe = Join-Path $publishDir 'WharfBar.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Publish did not produce $appExe."
}

# The MIT licence ships with the binaries, in the installer payload and in the portable zip alike.
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $publishDir -Force

# --- Portable zip: exe + bundled Wallpapers + LICENSE, extract-and-run, no installer. ---
New-Item -ItemType Directory -Path $portableStage -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableStage -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $portableStage,
    $portableZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

# --- Installer: Inno Setup over the same publish payload. ---
$iscc = Get-InnoSetupCompiler -Explicit $InnoSetupCompiler
$isccArgs = @(
    (Join-Path $PSScriptRoot 'WharfBar.iss'),
    "/DSourceDir=$publishDir",
    "/DOutputDir=$OutputRoot",
    "/DAppVersion=$semVer",
    "/DNumericVersion=$numericVersion",
    "/DOutputBaseName=$setupBaseName"
)

Write-Host "ISCC $($isccArgs -join ' ')"
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $setupExe)) {
    throw "Inno Setup did not produce $setupExe."
}

# --- Checksums (sha256sum-compatible: "<hash>  <file name>"). ---
$assets = Get-ChildItem -LiteralPath $OutputRoot -File |
    Where-Object { $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name

if ($assets.Count -ne 2) {
    throw "Expected exactly the setup exe and the portable zip in '$OutputRoot', found $($assets.Count) file(s)."
}

$checksumLines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, $asset.Name
}

Set-Content -LiteralPath $checksumFile -Value $checksumLines -Encoding ascii

Write-Host ''
Write-Host 'Release assets:'
foreach ($asset in Get-ChildItem -LiteralPath $OutputRoot -File | Sort-Object Name) {
    Write-Host ('  {0,-52} {1,8:N1} MB' -f $asset.Name, ($asset.Length / 1MB))
}

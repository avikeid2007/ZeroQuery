#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes the ZeroQuery.Desktop MSIX package for a single architecture into a known,
    deterministic output directory, then packages the result into a .msixupload bundle
    ready for Microsoft Partner Center ingestion.

.DESCRIPTION
    Earlier CI iterations tried to locate the dotnet-publish-generated .msix/.msixbundle
    by recursively scanning the whole repo afterwards, which proved unreliable (directory
    cache coherency delays, ambiguous path patterns, multiple matching candidates). This
    script instead pins AppxPackageDir to an architecture-specific folder under PackageRoot
    so there is never any ambiguity about where to look.

.PARAMETER Platform
    The MSBuild Platform to publish (x64 or ARM64).

.PARAMETER PackageRoot
    The root output directory. A '<Platform>' subfolder is created under it.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'

$outputDir = Join-Path $PackageRoot $Platform
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$appxPackageDir = (Resolve-Path $outputDir).Path + '\'

Write-Host "==> Publishing $Platform Store MSIX package into: $appxPackageDir"

dotnet publish Desktop/ZeroQuery.Desktop/ZeroQuery.Desktop.csproj `
    -p:Platform=$Platform `
    -c Release `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxPackageDir="$appxPackageDir"

if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::dotnet publish ($Platform) failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# dotnet publish for a packaged WinUI app emits a .msix (single-architecture) and/or a
# .msixbundle (when RuntimeIdentifiers lists multiple RIDs) directly under AppxPackageDir.
$packages = @(Get-ChildItem -Path $outputDir -Recurse -File |
    Where-Object { $_.Extension -in '.msix', '.msixbundle' })

if ($packages.Count -eq 0) {
    Write-Host "::error::No .msix/.msixbundle produced for $Platform under $outputDir"
    Write-Host "Full contents of ${outputDir}:"
    Get-ChildItem -Path $outputDir -Recurse -File | Select-Object FullName, Length | Format-Table -AutoSize
    exit 1
}

Write-Host "==> Found $($packages.Count) package(s) for ${Platform}:"
$packages | Select-Object FullName, Length | Format-Table -AutoSize

foreach ($package in $packages) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($package.Name)
    $uploadZip = Join-Path $package.DirectoryName "$baseName.msixupload"
    Write-Host "==> Generating $uploadZip"
    Compress-Archive -Path $package.FullName -DestinationPath $uploadZip -Force
}

Write-Host "==> $Platform Store package preparation complete."

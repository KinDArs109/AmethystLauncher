<#
.SYNOPSIS
    Publishes Amethyst Launcher and packages it into a Velopack installer + update feed,
    then (optionally) uploads the result as a GitHub Release.

.EXAMPLE
    ./build/release.ps1 -Version 1.0.1
    ./build/release.ps1 -Version 1.0.1 -Upload
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$repoUrl   = "https://github.com/KinDArs109/AmethystLauncher"
$publishDir = Join-Path $repoRoot "build\publish"
$releaseDir = Join-Path $repoRoot "build\releases"
$project    = "src/Launcher.App/Launcher.App.csproj"
$icon       = "src/Launcher.App/Assets/logo.ico"

Write-Host "==> Publishing $project (win-x64, self-contained)..." -ForegroundColor Magenta
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $publishDir

# Ensure vpk (Velopack CLI) is available.
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "==> Installing Velopack CLI (vpk)..." -ForegroundColor Magenta
    dotnet tool install -g vpk
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

Write-Host "==> Packing Velopack release $Version..." -ForegroundColor Magenta
vpk pack `
    --packId AmethystLauncher `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "Launcher.App.exe" `
    --packTitle "Amethyst Launcher" `
    --icon $icon `
    --outputDir $releaseDir

Write-Host "==> Done. Installer + feed are in $releaseDir" -ForegroundColor Green

if ($Upload) {
    Write-Host "==> Uploading GitHub Release v$Version..." -ForegroundColor Magenta
    vpk upload github `
        --repoUrl $repoUrl `
        --publish `
        --releaseName "Amethyst Launcher $Version" `
        --tag "v$Version" `
        --outputDir $releaseDir `
        --token $env:GITHUB_TOKEN
    Write-Host "==> Release published." -ForegroundColor Green
} else {
    Write-Host "Skip upload. Re-run with -Upload (and `$env:GITHUB_TOKEN set) to publish." -ForegroundColor Yellow
}

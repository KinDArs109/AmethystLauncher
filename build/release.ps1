<#
.SYNOPSIS
    Publishes Amethyst Launcher and packages it into a Velopack installer + update feed,
    then (optionally) uploads the result as a GitHub Release.

.EXAMPLE
    ./build/release.ps1 -Version 0.0.2
    ./build/release.ps1 -Version 0.0.2 -Upload
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

Write-Host "==> Publishing $project (win-x64, framework-dependent)..." -ForegroundColor Magenta
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# Framework-dependent: keeps the download small and lets the installer pull in the .NET 8 Desktop
# Runtime itself (see --framework on vpk pack below) instead of requiring the user to install it.
dotnet publish $project -c Release -r win-x64 --self-contained false `
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
    --framework net8.0-x64-desktop `
    --outputDir $releaseDir

Write-Host "==> Done. Installer + feed are in $releaseDir" -ForegroundColor Green

if ($Upload) {
    # Prefer an explicit token; otherwise borrow the one the GitHub CLI is already logged in with.
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token) -and (Get-Command gh -ErrorAction SilentlyContinue)) {
        $token = (gh auth token).Trim()
    }
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No GitHub token. Run 'gh auth login' or set `$env:GITHUB_TOKEN."
    }

    Write-Host "==> Uploading GitHub Release v$Version..." -ForegroundColor Magenta
    vpk upload github `
        --repoUrl $repoUrl `
        --publish `
        --releaseName "Amethyst Launcher $Version" `
        --tag "v$Version" `
        --outputDir $releaseDir `
        --token $token
    Write-Host "==> Release published." -ForegroundColor Green
} else {
    Write-Host "Skip upload. Re-run with -Upload to publish the release." -ForegroundColor Yellow
}

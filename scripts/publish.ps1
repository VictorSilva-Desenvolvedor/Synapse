<#
.SYNOPSIS
    Script de publicação self-contained do Synapse para Windows x64 (ADR-013, TECH-04).
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $OutputDir = Join-Path $repoRoot "dist\Synapse"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Publicacao de Release" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$dotnet = if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" } else { "dotnet" }

if (Test-Path $OutputDir) {
    Write-Host "Limpando diretorio anterior: $OutputDir" -ForegroundColor Gray
    Remove-Item -Recurse -Force $OutputDir
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$repoRoot = Split-Path $PSScriptRoot -Parent
$hostCsproj = Join-Path $repoRoot "src\Synapse.Host\Synapse.Host.csproj"
$trayCsproj = Join-Path $repoRoot "src\Synapse.Tray\Synapse.Tray.csproj"

Write-Host "1. Publicando Synapse.Host (Windows Service)..." -ForegroundColor Yellow
& $dotnet publish $hostCsproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$OutputDir\Service"

Write-Host "2. Publicando Synapse.Tray (System Tray App)..." -ForegroundColor Yellow
& $dotnet publish $trayCsproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$OutputDir\Tray"

# Copia scripts de instalacao para a distribuicao
Copy-Item "$PSScriptRoot\install.ps1" "$OutputDir\install.ps1"
Copy-Item "$PSScriptRoot\uninstall.ps1" "$OutputDir\uninstall.ps1"

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Publicacao concluida com sucesso em:" -ForegroundColor Green
Write-Host " $OutputDir" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

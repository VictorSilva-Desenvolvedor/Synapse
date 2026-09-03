param (
    [string]$VaultPath = $(if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" })
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Instalação do Plugin Obsidian Synapse" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

if (-not (Test-Path $VaultPath)) {
    Write-Error "Cofre não encontrado em: $VaultPath"
    exit 1
}

$pluginsDir = Join-Path $VaultPath ".obsidian\plugins\synapse"
if (-not (Test-Path $pluginsDir)) {
    New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
    Write-Host "Criada pasta do plugin: $pluginsDir" -ForegroundColor Green
}

$pluginSrc = Join-Path $PSScriptRoot "..\plugins\obsidian-synapse"
Copy-Item (Join-Path $pluginSrc "manifest.json") -Destination $pluginsDir -Force
Copy-Item (Join-Path $pluginSrc "main.js") -Destination $pluginsDir -Force
Write-Host "Arquivos manifest.json e main.js copiados com sucesso." -ForegroundColor Green

$commPluginsFile = Join-Path $VaultPath ".obsidian\community-plugins.json"
$enabledList = @()

if (Test-Path $commPluginsFile) {
    $raw = Get-Content $commPluginsFile -Raw
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $enabledList = @($raw | ConvertFrom-Json)
    }
}

if ($enabledList -notcontains "synapse") {
    $enabledList += "synapse"
    $json = $enabledList | ConvertTo-Json
    Set-Content -Path $commPluginsFile -Value $json -Encoding UTF8
    Write-Host "Plugin 'synapse' habilitado em community-plugins.json." -ForegroundColor Green
} else {
    Write-Host "Plugin 'synapse' já estava habilitado em community-plugins.json." -ForegroundColor Yellow
}

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Plugin Synapse instalado no Obsidian!" -ForegroundColor Green
Write-Host " Cofre: $VaultPath" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

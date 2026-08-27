<#
.SYNOPSIS
    Script de compilação do instalador gráfico do Synapse via Inno Setup (V3.1, ADR-013).
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Gerador de Instalador" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Publica os binários se necessário
Write-Host "1. Publicando binários self-contained..." -ForegroundColor Yellow
& "$PSScriptRoot\publish.ps1" -Configuration $Configuration

# 2. Localiza o compilador do Inno Setup (ISCC.exe)
$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "ISCC.exe"
)

$isccExe = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $isccExe = $path
        break
    }
}

$issFile = Join-Path $PSScriptRoot "..\packaging\inno\Synapse.iss"

if ($isccExe) {
    Write-Host "2. Compilando instalador gráfico com Inno Setup ($isccExe)..." -ForegroundColor Yellow
    & $isccExe $issFile
    Write-Host "Instalador gerado com sucesso em: dist\Installer\" -ForegroundColor Green
} else {
    Write-Host "Inno Setup (ISCC.exe) não encontrado no caminho padrão." -ForegroundColor DarkYellow
    Write-Host "O script Synapse.iss está preparado em: $issFile" -ForegroundColor Cyan
    Write-Host "Para compilar o instalador, instale o Inno Setup ou execute o workflow do GitHub Actions." -ForegroundColor Gray
}

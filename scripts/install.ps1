<#
.SYNOPSIS
    Script de instalacao e configuracao do Synapse como Windows Service e inicializacao da Bandeja (ADR-006, ADR-013).
#>

[CmdletBinding()]
param (
    [string]$ServiceName = "Synapse",
    [string]$DisplayName = "Synapse Obsidian Sync Service"
)

$ErrorActionPreference = "Stop"

# Verifica se esta executando como Administrador
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script precisa ser executado como Administrador para registrar o servico do Windows."
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Instalacao do Servico" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$serviceDir = "$PSScriptRoot\Service"
$serviceExe = "$serviceDir\Synapse.Host.exe"
$trayExe = "$PSScriptRoot\Tray\Synapse.Tray.exe"

if (-not (Test-Path $serviceExe)) {
    # Tenta caminho local de desenvolvimento
    $devHost = Resolve-Path "$PSScriptRoot\..\src\Synapse.Host\bin\Release\net8.0\win-x64\publish\Synapse.Host.exe" -ErrorAction SilentlyContinue
    if ($devHost) { $serviceExe = $devHost.Path }
    $devTray = Resolve-Path "$PSScriptRoot\..\src\Synapse.Tray\bin\Release\net8.0\win-x64\publish\Synapse.Tray.exe" -ErrorAction SilentlyContinue
    if ($devTray) { $trayExe = $devTray.Path }
}

if (-not (Test-Path $serviceExe)) {
    Write-Error "Executavel do servico nao encontrado. Execute o script publish.ps1 antes de instalar."
    exit 1
}

# 1. Para e remove servico anterior se existir
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Parando servico existente..." -ForegroundColor Yellow
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "Removendo servico existente..." -ForegroundColor Yellow
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# 2. Cria o Windows Service com inicializacao automatica
Write-Host "Criando o Windows Service '$ServiceName'..." -ForegroundColor Green
& sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null

# 3. Configura politica de recuperacao em caso de falha (ADR-006, SRS 3.8)
Write-Host "Configurando politica de auto-recuperacao..." -ForegroundColor Green
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null

# 4. Inicia o servico
Write-Host "Iniciando o servico $ServiceName..." -ForegroundColor Green
& sc.exe start $ServiceName | Out-Null

# 5. Configura a inicializacao da Bandeja (Synapse.Tray) no logon do usuario
if (Test-Path $trayExe) {
    Write-Host "Registrando Synapse.Tray na inicializacao do usuario..." -ForegroundColor Green
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $runKey -Name "SynapseTray" -Value "`"$trayExe`""

    # Inicia a bandeja imediatamente se nao estiver rodando
    $trayProcess = Get-Process -Name "Synapse.Tray" -ErrorAction SilentlyContinue
    if (-not $trayProcess) {
        Write-Host "Iniciando aplicativo da bandeja..." -ForegroundColor Green
        Start-Process $trayExe
    }
}

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Synapse instalado e em execucao!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

<#
.SYNOPSIS
    Script de desinstalacao do Synapse (para o servico, remove o registro e limpa a bandeja).
#>

[CmdletBinding()]
param (
    [string]$ServiceName = "Synapse"
)

$ErrorActionPreference = "Stop"

# Verifica se esta executando como Administrador
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script precisa ser executado como Administrador para remover o servico do Windows."
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Desinstalacao do Servico" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Encerra a bandeja se estiver rodando
$trayProcess = Get-Process -Name "Synapse.Tray" -ErrorAction SilentlyContinue
if ($trayProcess) {
    Write-Host "Encerrando processo da bandeja..." -ForegroundColor Yellow
    Stop-Process -Name "Synapse.Tray" -Force -ErrorAction SilentlyContinue
}

# 2. Remove da inicializacao do usuario
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Get-ItemProperty -Path $runKey -Name "SynapseTray" -ErrorAction SilentlyContinue) {
    Write-Host "Removendo Synapse.Tray da inicializacao do Windows..." -ForegroundColor Yellow
    Remove-ItemProperty -Path $runKey -Name "SynapseTray" -ErrorAction SilentlyContinue
}

# 3. Para e remove o Windows Service
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Parando servico $ServiceName..." -ForegroundColor Yellow
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "Excluindo servico $ServiceName..." -ForegroundColor Yellow
    & sc.exe delete $ServiceName | Out-Null
}

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Synapse desinstalado com sucesso." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

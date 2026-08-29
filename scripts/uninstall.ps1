<#
.SYNOPSIS
    Script de desinstalacao do Synapse: encerra os processos e remove o autostart do login.
#>

[CmdletBinding()]
param ()

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Desinstalacao" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Encerra os processos se estiverem rodando
Write-Host "Encerrando processos do Synapse..." -ForegroundColor Yellow
Get-Process -Name "Synapse.Host", "Synapse.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 2. Remove da inicializacao do usuario
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
foreach ($name in @("SynapseHost", "SynapseTray")) {
    if (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue) {
        Write-Host "Removendo $name da inicializacao do Windows..." -ForegroundColor Yellow
        Remove-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue
    }
}

# 3. Remove um Windows Service "Synapse" de versoes antigas, se existir (best-effort,
#    precisa de Administrador - versoes atuais nao criam mais servico nenhum)
$existingService = Get-Service -Name "Synapse" -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Encontrado servico 'Synapse' de uma instalacao antiga. Tentando remover..." -ForegroundColor Yellow
    try {
        & sc.exe stop Synapse | Out-Null
        Start-Sleep -Seconds 2
        & sc.exe delete Synapse | Out-Null
    } catch {
        Write-Warning "Nao foi possivel remover o servico antigo (rode como Administrador pra remover manualmente com 'sc.exe delete Synapse')."
    }
}

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Synapse desinstalado com sucesso." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

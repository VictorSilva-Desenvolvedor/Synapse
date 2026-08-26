#Requires -RunAsAdministrator
<#
    Para e remove o Windows Service do Synapse, conforme ADR-013
    (ADR - Synapse.md). Nao apaga o executavel publicado nem o cofre
    do usuario -- so o registro do servico.
#>
param(
    [string]$ServiceName = "Synapse"
)

$ErrorActionPreference = "Stop"

$existing = sc.exe query $ServiceName 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Servico '$ServiceName' nao esta instalado -- nada a fazer."
    exit 0
}

Write-Host "Parando servico '$ServiceName'..."
sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2

Write-Host "Removendo servico '$ServiceName'..."
sc.exe delete $ServiceName | Out-Null

Write-Host "Desinstalacao concluida."

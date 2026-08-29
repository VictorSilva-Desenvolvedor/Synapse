<#
.SYNOPSIS
    Script de instalacao do Synapse: registra Synapse.Host e Synapse.Tray para iniciarem
    automaticamente no login do usuario e ja inicia os dois processos.

.NOTES
    Historico: a primeira versao deste script registrava Synapse.Host como um Windows
    Service (LocalSystem). Isso se mostrou incompativel com a forma como o app guarda
    configuracao/token: tudo (synapse_config.json, github_token.dat protegido por DPAPI,
    o pipe nomeado usado pelo Tray/plugin do Obsidian) esta preso ao perfil e ao nivel de
    integridade do USUARIO interativo. Rodando como LocalSystem, o Host nao conseguia
    decifrar o token do GitHub (DPAPI CurrentUser e criptografado especificamente pra
    conta do usuario, LocalSystem nao consegue abrir isso por design) nem aceitar
    conexoes no Named Pipe vindas de um processo do usuario normal (EPERM por Mandatory
    Integrity Control). Por isso a instalacao agora usa o mesmo mecanismo simples que ja
    funcionava pro Tray: autostart via HKCU Run no login do usuario, pros dois processos.
    Nao precisa mais rodar como Administrador.
#>

[CmdletBinding()]
param ()

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Instalacao (autostart no login)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$hostExe = "$PSScriptRoot\Service\Synapse.Host.exe"
$trayExe = "$PSScriptRoot\Tray\Synapse.Tray.exe"

if (-not (Test-Path $hostExe)) {
    # Tenta caminho local de desenvolvimento
    $devHost = Resolve-Path "$PSScriptRoot\..\src\Synapse.Host\bin\Release\net8.0\win-x64\publish\Synapse.Host.exe" -ErrorAction SilentlyContinue
    if ($devHost) { $hostExe = $devHost.Path }
    $devTray = Resolve-Path "$PSScriptRoot\..\src\Synapse.Tray\bin\Release\net8.0\win-x64\publish\Synapse.Tray.exe" -ErrorAction SilentlyContinue
    if ($devTray) { $trayExe = $devTray.Path }
}

if (-not (Test-Path $hostExe)) {
    Write-Error "Executavel do Synapse.Host nao encontrado. Execute o script publish.ps1 antes de instalar."
    exit 1
}

if (-not (Test-Path $trayExe)) {
    Write-Error "Executavel do Synapse.Tray nao encontrado. Execute o script publish.ps1 antes de instalar."
    exit 1
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

# 1. Encerra instancias antigas rodando (de qualquer local) antes de trocar de versao
Write-Host "Encerrando instancias antigas, se houver..." -ForegroundColor Yellow
Get-Process -Name "Synapse.Host", "Synapse.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 2. Registra Synapse.Host na inicializacao do usuario
Write-Host "Registrando Synapse.Host na inicializacao do usuario..." -ForegroundColor Green
Set-ItemProperty -Path $runKey -Name "SynapseHost" -Value "`"$hostExe`""

# 3. Registra Synapse.Tray na inicializacao do usuario
Write-Host "Registrando Synapse.Tray na inicializacao do usuario..." -ForegroundColor Green
Set-ItemProperty -Path $runKey -Name "SynapseTray" -Value "`"$trayExe`""

# 4. Inicia os dois processos imediatamente (nao precisa esperar o proximo login)
Write-Host "Iniciando Synapse.Host..." -ForegroundColor Green
Start-Process -FilePath $hostExe -WindowStyle Hidden

Start-Sleep -Seconds 2

Write-Host "Iniciando Synapse.Tray..." -ForegroundColor Green
Start-Process -FilePath $trayExe

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Synapse instalado e em execucao!" -ForegroundColor Green
Write-Host " (inicia sozinho no proximo login, sem precisar de terminal aberto)" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

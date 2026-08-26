#Requires -RunAsAdministrator
<#
    Publica Synapse.Host como executavel self-contained de arquivo unico e o
    registra como Windows Service, conforme ADR-013 (ADR - Synapse.md).
#>
param(
    [string]$ServiceName = "Synapse",
    [string]$DisplayName = "Synapse - Sincronizacao Obsidian/Google Drive",
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\publish"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$hostProject = Join-Path $PSScriptRoot "..\src\Synapse.Host\Synapse.Host.csproj"

Write-Host "Publicando Synapse.Host (self-contained, win-x64)..."
dotnet publish $hostProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

$exePath = Join-Path $PublishDir "Synapse.Host.exe"
if (-not (Test-Path $exePath)) {
    throw "Publicacao falhou: nao encontrei $exePath"
}

$existing = sc.exe query $ServiceName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Servico '$ServiceName' ja existe -- parando e removendo antes de reinstalar..."
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host "Registrando servico '$ServiceName'..."
sc.exe create $ServiceName binPath= "`"$exePath`"" DisplayName= "$DisplayName" start= auto | Out-Null
sc.exe description $ServiceName "Sincroniza um cofre Obsidian local com o Google Drive e executa automacoes de notas." | Out-Null

Write-Host "Iniciando servico..."
sc.exe start $ServiceName | Out-Null

Write-Host "Instalacao concluida. Use 'Get-Service $ServiceName' para verificar o status."

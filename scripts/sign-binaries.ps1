<#
.SYNOPSIS
    Script utilitário para assinatura digital Authenticode dos executáveis do Synapse (V3.2, ADR-013).
    Requer certificado de assinatura de código válido de uma Autoridade Certificadora (CA).
#>

[CmdletBinding()]
param (
    [string]$TargetDir = "$PSScriptRoot\..\dist\Synapse",
    [string]$CertificatePath = $env:SYNAPSE_CERT_PATH,
    [string]$CertificatePassword = $env:SYNAPSE_CERT_PASSWORD,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Synapse - Assinatura Digital Authenticode" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

if ([string]::IsNullOrWhiteSpace($CertificatePath) -or (-not (Test-Path $CertificatePath))) {
    Write-Warning "Certificado digital não especificado ou arquivo .pfx não encontrado."
    Write-Host "Para assinar os binários:" -ForegroundColor Gray
    Write-Host "1. Obtenha um certificado Code Signing de uma Autoridade Certificadora reconhecida (DigiCert, Sectigo, etc.)." -ForegroundColor Gray
    Write-Host "2. Defina as variáveis de ambiente SYNAPSE_CERT_PATH e SYNAPSE_CERT_PASSWORD ou passe os parâmetros." -ForegroundColor Gray
    exit 0
}

# Localiza signtool.exe nos SDKs do Windows
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe"
)

$signtool = Resolve-Path $signtoolPaths -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty Path

if (-not $signtool) {
    Write-Error "signtool.exe não encontrado nos Windows Kits. Instale o Windows 10/11 SDK."
    exit 1
}

$filesToSign = Get-ChildItem -Path $TargetDir -Recurse -Include "*.exe", "*.dll"

foreach ($file in $filesToSign) {
    Write-Host "Assinando digitalmente: $($file.FullName)..." -ForegroundColor Yellow
    & $signtool sign /fd SHA256 /tr $TimestampServer /td SHA256 /f $CertificatePath /p $CertificatePassword $file.FullName
}

Write-Host "Assinatura digital concluída com sucesso." -ForegroundColor Green

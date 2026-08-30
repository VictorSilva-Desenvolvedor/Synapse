<#
.SYNOPSIS
    Captura screenshots das telas WPF do Synapse.Tray via RenderTargetBitmap.

.DESCRIPTION
    Executa os testes de captura em tests/Synapse.Tests/UI/WpfCaptureTests.cs e grava
    os PNGs em artifacts/screenshots/. Usado pelo agente pixel-art-frontend no ciclo
    capturar -> pontuar -> patchar -> recapturar.

    A captura usa RenderTargetBitmap em 96 DPI fixo (1 DIP = 1 pixel do PNG), entao
    o resultado nao depende do DPI do monitor. As janelas sao mostradas fora da tela
    para nao roubar o foco.

.PARAMETER Screen
    Nome da janela a capturar, ex.: OnboardingWindow, QuickCaptureWindow, ChatVaultWindow.
    Aceita nome parcial. Omita para capturar todas.

.PARAMETER SaveBefore
    Copia os PNGs atuais para artifacts/screenshots/before/ antes de recapturar.

.PARAMETER SkipBuild
    Nao recompila antes de capturar.

.EXAMPLE
    powershell -File scripts/capture-ui.ps1 -SaveBefore -Screen QuickCaptureWindow
.EXAMPLE
    powershell -File scripts/capture-ui.ps1
#>
[CmdletBinding()]
param(
    [string]$Screen,
    [switch]$SaveBefore,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$shotsDir  = Join-Path $repoRoot 'artifacts\screenshots'
$beforeDir = Join-Path $shotsDir 'before'
$testProj  = Join-Path $repoRoot 'tests\Synapse.Tests\Synapse.Tests.csproj'

New-Item -ItemType Directory -Force -Path $shotsDir  | Out-Null
New-Item -ItemType Directory -Force -Path $beforeDir | Out-Null

if ($SaveBefore) {
    $existing = Get-ChildItem -Path $shotsDir -Filter '*.png' -File
    if ($existing) {
        $existing | Copy-Item -Destination $beforeDir -Force
        Write-Host "[before] $($existing.Count) screenshot(s) preservado(s) em artifacts/screenshots/before/"
    } else {
        Write-Host "[before] nenhum screenshot anterior encontrado"
    }
}

if (-not $SkipBuild) {
    Write-Host "[build] compilando Synapse.Tray..."
    dotnet build (Join-Path $repoRoot 'src\Synapse.Tray\Synapse.Tray.csproj') -v quiet --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build falhou. Corrija a compilacao antes de capturar." }
}

$filter = if ($Screen) { "FullyQualifiedName~Captura_$Screen" }
          else         { "FullyQualifiedName~Synapse.Tests.UI" }

Write-Host "[captura] filtro: $filter"
dotnet test $testProj --filter $filter -v quiet --nologo
$testExit = $LASTEXITCODE

$shots = Get-ChildItem -Path $shotsDir -Filter '*.png' -File | Sort-Object Name
if (-not $shots) {
    throw "Nenhum PNG gerado em $shotsDir. A captura falhou - investigue antes de editar UI."
}

Write-Host ""
Write-Host "[resultado] screenshots em artifacts/screenshots/:"
foreach ($s in $shots) {
    $age = [int]((Get-Date) - $s.LastWriteTime).TotalSeconds
    $tag = if ($age -lt 120) { 'NOVO' } else { 'antigo' }
    Write-Host ("  {0,-6} {1,-34} {2,6:N1} KB  {3}" -f $tag, $s.Name, ($s.Length / 1KB), $s.LastWriteTime.ToString('HH:mm:ss'))
}

if ($testExit -ne 0) {
    Write-Warning "Algum teste de captura falhou (exit $testExit). Os PNGs marcados NOVO ainda sao validos."
}
exit $testExit

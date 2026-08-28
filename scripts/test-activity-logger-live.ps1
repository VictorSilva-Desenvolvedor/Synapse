$vaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST"

Write-Host "========================================="
Write-Host " TESTE DO SISTEMA DE LOGS DO SYNAPSE"
Write-Host "========================================="

$coreDll = "C:\Users\victo\Repos\Pessoal\Synapse\Synapse\src\Synapse.Core\bin\Debug\net8.0\Synapse.Core.dll"
Add-Type -Path $coreDll

$logger = [Synapse.Core.Logging.SynapseActivityLogger]::Instance
$logger.SetVaultPath($vaultPath)

Write-Host "Registrando clique e acoes de usuario..."
$logger.LogClickAsync("TrayMenu", "OpenChatVault", "Usuario abriu a janela de chat").GetAwaiter().GetResult()
$logger.LogClickAsync("ChatVault", "BtnSend", "Pergunta: quem sao meus amigos?").GetAwaiter().GetResult()

Write-Host "Registrando interacao de Chat / IA com tempo de resposta..."
$logger.LogChatAsync(
    "quem sao meus amigos?",
    "Com base nas notas do seu cofre, o amigo registrado e o [[Felipe]] na nota [[Lista de Amigos]].",
    1280,
    "Success",
    @("Lista de Amigos"),
    "Pessoas/Lista de Amigos.md").GetAwaiter().GetResult()

Write-Host "Registrando evento de timeout (> 2 min)..."
$logger.LogActionAsync(
    "BrainEngine",
    "HeavyIndex",
    "Indexacao massiva de notas",
    "Timeout",
    125000,
    "A operacao excedeu o tempo limite de 2 minutos").GetAwaiter().GetResult()

Write-Host ""
Write-Host "========================================="
Write-Host " VERIFICACAO DOS ARQUIVOS DE LOG GERADOS"
Write-Host "========================================="

$localJsonl = Join-Path $env:LOCALAPPDATA "Synapse\Logs\synapse_activity.jsonl"
$localLog = Join-Path $env:LOCALAPPDATA "Synapse\Logs\synapse_activity.log"
$vaultJsonl = Join-Path $vaultPath ".synapse\logs\synapse-activity.jsonl"

$logsDir = Join-Path $vaultPath "Synapse\Logs"
$vaultMdFiles = Get-ChildItem -Path $logsDir -Filter "*.md" -ErrorAction SilentlyContinue

Write-Host "1. Local JSONL: $localJsonl (Existe: $(Test-Path $localJsonl))"
Write-Host "2. Local TXT Log: $localLog (Existe: $(Test-Path $localLog))"
Write-Host "3. Vault JSONL: $vaultJsonl (Existe: $(Test-Path $vaultJsonl))"
Write-Host "4. Vault Markdown Files Count: $($vaultMdFiles.Count)"

foreach ($f in $vaultMdFiles) {
    Write-Host ""
    Write-Host "--- ARQUIVO DENTRO DO OBSIDIAN: $($f.FullName) ---"
    Get-Content $f.FullName | ForEach-Object { Write-Host $_ }
}

Write-Host ""
Write-Host "--- ULTIMAS LINHAS DO LOG LOCAL TXT ---"
if (Test-Path $localLog) {
    Get-Content $localLog | Select-Object -Last 6 | ForEach-Object { Write-Host $_ }
}

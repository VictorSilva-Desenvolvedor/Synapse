$appData = [Environment]::GetFolderPath("LocalApplicationData")
$configFile = Join-Path $appData "Synapse\synapse_config.json"

if (Test-Path $configFile) {
    $config = Get-Content $configFile | ConvertFrom-Json
    $config.GeminiApiKey = "$env:GEMINI_API_KEY"
    $config.GeminiModel = "gemini-3.6-flash"
    $config.VaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST"
    
    $json = $config | ConvertTo-Json -Depth 5
    Set-Content -Path $configFile -Value $json -Encoding UTF8
    Write-Host "synapse_config.json corrigido com sucesso com gemini-3.6-flash!" -ForegroundColor Green
}

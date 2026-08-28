$apiKey = "$env:GEMINI_API_KEY"
$model = "gemini-3.6-flash"

$appData = [Environment]::GetFolderPath("LocalApplicationData")
$configFile = Join-Path $appData "Synapse\synapse_config.json"

if (Test-Path $configFile) {
    $config = Get-Content $configFile | ConvertFrom-Json
    $config.GeminiApiKey = $apiKey
    $config.GeminiModel = $model
    
    $json = $config | ConvertTo-Json -Depth 5
    Set-Content -Path $configFile -Value $json -Encoding UTF8
    Write-Host "synapse_config.json atualizado com a chave do Gemini!" -ForegroundColor Green
}

[Environment]::SetEnvironmentVariable("GEMINI_API_KEY", $apiKey, "User")
[Environment]::SetEnvironmentVariable("GEMINI_API_KEY", $apiKey, "Process")
Write-Host "Variável GEMINI_API_KEY configurada com sucesso!" -ForegroundColor Green

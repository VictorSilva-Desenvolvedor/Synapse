$appData = [Environment]::GetFolderPath("LocalApplicationData")
$configFile = Join-Path $appData "Synapse\synapse_config.json"

if (Test-Path $configFile) {
    $config = Get-Content $configFile | ConvertFrom-Json
    $oldPath = $config.VaultPath
    $config.VaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST"
    
    $json = $config | ConvertTo-Json -Depth 5
    Set-Content -Path $configFile -Value $json -Encoding UTF8
    Write-Host "VaultPath atualizado com sucesso:" -ForegroundColor Green
    Write-Host " De:   $oldPath" -ForegroundColor Yellow
    Write-Host " Para: $($config.VaultPath)" -ForegroundColor Green
}

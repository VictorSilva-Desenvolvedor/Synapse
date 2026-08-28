$appData = [Environment]::GetFolderPath("LocalApplicationData")
$configFile = Join-Path $appData "Synapse\synapse_config.json"

if (Test-Path $configFile) {
    $config = Get-Content $configFile | ConvertFrom-Json
    Write-Host "Configured VaultPath: $($config.VaultPath)" -ForegroundColor Cyan
    Write-Host "Configured Owner:     $($config.Owner)" -ForegroundColor Cyan
    Write-Host "Configured Repo:      $($config.Repository)" -ForegroundColor Cyan
    Write-Host "Configured Branch:    $($config.Branch)" -ForegroundColor Cyan
}

$obsidianJson = "$env:APPDATA\obsidian\obsidian.json"
if (Test-Path $obsidianJson) {
    $obsidian = Get-Content $obsidianJson | ConvertFrom-Json
    Write-Host "`nObsidian Open Vaults:" -ForegroundColor Yellow
    foreach ($k in $obsidian.vaults.PSObject.Properties) {
        Write-Host " - Key: $($k.Name) -> Path: $($k.Value.path)"
    }
}

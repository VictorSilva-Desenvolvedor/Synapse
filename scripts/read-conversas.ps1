$vaultRoot = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }
$vaultPath = Join-Path $vaultRoot "Brain\Conversas"
Get-ChildItem -Path $vaultPath | ForEach-Object {
    Write-Host "=== FILE: $($_.Name) ===" -ForegroundColor Cyan
    Get-Content $_.FullName
}

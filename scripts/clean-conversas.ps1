$vaultPath = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }
$conversasDir = Join-Path $vaultPath "Brain\Conversas"
if (Test-Path $conversasDir) {
    Remove-Item -Path "$conversasDir\*" -Force -Recurse
    Write-Host "Pasta Brain/Conversas limpa com sucesso!" -ForegroundColor Green
}

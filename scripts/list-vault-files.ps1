$vaultPath = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }
Get-ChildItem -Path $vaultPath -Recurse | Select-Object FullName, Length, LastWriteTime | Format-Table -AutoSize

$vaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST"
Get-ChildItem -Path $vaultPath -Recurse | Select-Object FullName, Length, LastWriteTime | Format-Table -AutoSize

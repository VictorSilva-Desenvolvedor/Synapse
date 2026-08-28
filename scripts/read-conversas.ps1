$vaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST\Brain\Conversas"
Get-ChildItem -Path $vaultPath | ForEach-Object {
    Write-Host "=== FILE: $($_.Name) ===" -ForegroundColor Cyan
    Get-Content $_.FullName
}

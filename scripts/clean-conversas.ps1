$conversasDir = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST\Brain\Conversas"
if (Test-Path $conversasDir) {
    Remove-Item -Path "$conversasDir\*" -Force -Recurse
    Write-Host "Pasta Brain/Conversas limpa com sucesso!" -ForegroundColor Green
}

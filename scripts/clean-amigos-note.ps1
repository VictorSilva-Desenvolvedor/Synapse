$noteContent = @"
---
titulo: "Lista de Amigos"
categoria: "Pessoas"
criado_em: "2026-08-28 15:58:52"
status: processado
tags:
  - pessoas
  - amigos
  - contatos
---

# Lista de Amigos

Área dedicada ao registro e acompanhamento de conexões pessoais e amigos.

## Registro de Amigos

| Nome | Relação | Observações / Detalhes |
| :--- | :--- | :--- |
| Felipe | Amigo | Contato adicionado inicial |

## Conexões
- [[Pessoas]]
- [[Felipe]]
"@

$vaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST\Pessoas\Lista de Amigos.md"
Set-Content -Path $vaultPath -Value $noteContent -Encoding UTF8
Write-Host "Lista de Amigos.md regravada com sucesso e limpa!" -ForegroundColor Green

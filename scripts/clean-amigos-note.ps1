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
| Fulano | Amigo | Contato adicionado inicial |

## Conexões
- [[Pessoas]]
- [[Fulano]]
"@

$vaultRoot = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }
$vaultPath = Join-Path $vaultRoot "Pessoas\Lista de Amigos.md"
Set-Content -Path $vaultPath -Value $noteContent -Encoding UTF8
Write-Host "Lista de Amigos.md regravada com sucesso e limpa!" -ForegroundColor Green

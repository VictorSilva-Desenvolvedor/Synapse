$apiKey = "$env:GEMINI_API_KEY"
$vaultPath = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " TESTE REAL DE PERGUNTA RAG AO COFRE" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Lê as notas reais do cofre para compor o contexto
$noteContent = Get-Content "$vaultPath\Pessoas\Lista de Amigos.md" -Raw
$context = "--- INÍCIO DA NOTA: [[Lista de Amigos]] ---`n$noteContent`n--- FIM DA NOTA ---"

$question = "Quem são as pessoas que eu conheço cadastradas no cofre?"
$prompt = @"
Você é o assistente inteligente de Segundo Cérebro do usuário no Obsidian.
Com base no contexto das notas do cofre abaixo, responda à pergunta de forma direta, clara e bem estruturada em Markdown, citando as notas com wikilinks [[Nome da Nota]].

Notas do cofre:
$context

Pergunta:
$question
"@

$url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key=$apiKey"
$body = @{
    contents = @( @{ parts = @( @{ text = $prompt } ) } )
} | ConvertTo-Json -Depth 5

$res = Invoke-RestMethod -Uri $url -Method Post -Headers @{"Content-Type"="application/json"} -Body $body
$answer = $res.candidates[0].content.parts[0].text

Write-Host "Pergunta: $question" -ForegroundColor Cyan
Write-Host "`nResposta da IA:" -ForegroundColor Green
Write-Host $answer

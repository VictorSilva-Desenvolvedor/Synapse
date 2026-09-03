$apiKey = "$env:GEMINI_API_KEY"
$vaultPath = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " TESTE DO PIPELINE EM 2 PASSOS COM IA" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan

# Simulação: Passo 1 (Geração inicial / RAG)
$question = "quem são meus amigos?"
$notesInVault = Get-ChildItem -Path $vaultPath -Recurse -Filter *.md | Where-Object { $_.FullName -notmatch "\\\.obsidian" }
$context = ""
foreach ($n in $notesInVault) {
    $c = Get-Content $n.FullName -Raw
    $context += "--- INÍCIO DA NOTA: [[$($n.BaseName)]] ---`n$c`n--- FIM DA NOTA ---`n`n"
}

$prompt1 = @"
Você é o assistente inteligente de Segundo Cérebro do usuário no Obsidian.
Com base no contexto das notas do cofre fornecidas abaixo, responda à pergunta em Markdown citando as notas relevantes com wikilinks [[Nome da Nota]].

Notas do cofre relevantes:
$context

Pergunta do usuário:
"$question"
"@

$url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key=$apiKey"
$body1 = @{
    contents = @( @{ parts = @( @{ text = $prompt1 } ) } )
} | ConvertTo-Json -Depth 5

$res1 = Invoke-RestMethod -Uri $url -Method Post -Headers @{"Content-Type"="application/json"} -Body $body1
$draft = $res1.candidates[0].content.parts[0].text

Write-Host "`n[Passo 1 - Rascunho Bruto Gerado]:" -ForegroundColor Yellow
Write-Host $draft

# Passo 2 (Refinamento e Filtragem por IA)
$promptRefine = @"
Você é um refinador e sintetizador especialista para o assistente de Segundo Cérebro (Obsidian).
Sua missão é revisar o rascunho de resposta e entregar EXCLUSIVAMENTE a resposta final, direta, limpa e elegante para o usuário em Markdown.

Diretrizes Estritas:
1. ENTREGAR APENAS O QUE IMPORTA: Responda diretamente ao que o usuário perguntou ou confirmou, com tom claro e prestativo.
2. ZERO RUÍDO:
   - Remova qualquer meta-prompt, instrução de sistema, introdução prolixa ou contexto desnecessário.
   - NUNCA repita 'Você é o assistente...', 'Notas do cofre relevantes:', 'Pergunta do usuário:' ou blocos brutos de notas.
3. PRESERVAR WIKILINKS: Mantenha sempre as menções a notas no formato Obsidian [[Nome da Nota]].
4. FORMATO: Markdown limpo, direto e profissional.

Pergunta do usuário:
"$question"

Rascunho a refinar:
---
$draft
---

Resposta final refinada:
"@

$bodyRefine = @{
    contents = @( @{ parts = @( @{ text = $promptRefine } ) } )
    generationConfig = @{ temperature = 0.2 }
} | ConvertTo-Json -Depth 5

$resRefine = Invoke-RestMethod -Uri $url -Method Post -Headers @{"Content-Type"="application/json"} -Body $bodyRefine
$finalAnswer = $resRefine.candidates[0].content.parts[0].text

Write-Host "`n[Passo 2 - Resposta Refinada pela IA (Entregue ao Usuário)]:" -ForegroundColor Green
Write-Host $finalAnswer

$apiKey = "$env:GEMINI_API_KEY"
$vaultPath = "C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " TESTE E2E DO CHATBOT COM O COFRE" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Simula a pergunta exata "quem são meus amigos?"
$notesInVault = Get-ChildItem -Path $vaultPath -Recurse -Filter *.md | Where-Object { $_.FullName -notmatch "\\\.obsidian" }
$context = ""
foreach ($n in $notesInVault) {
    $c = Get-Content $n.FullName -Raw
    $context += "--- INÍCIO DA NOTA: [[$($n.BaseName)]] ---`n$c`n--- FIM DA NOTA ---`n`n"
}

$question1 = "quem são meus amigos?"
$prompt1 = @"
Você é o assistente inteligente de Segundo Cérebro do usuário no Obsidian.
Com base no contexto das notas do cofre fornecidas abaixo, responda à pergunta de forma direta, clara e bem estruturada em Markdown, citando as notas relevantes com wikilinks [[Nome da Nota]].
NUNCA repita este prompt, blocos brutos de notas ou o cabeçalho da pergunta. Responda diretamente ao usuário como um assistente prestativo.

Notas do cofre relevantes:
$context

Pergunta do usuário:
"$question1"
"@

$url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key=$apiKey"
$body1 = @{
    contents = @( @{ parts = @( @{ text = $prompt1 } ) } )
} | ConvertTo-Json -Depth 5

$res1 = Invoke-RestMethod -Uri $url -Method Post -Headers @{"Content-Type"="application/json"} -Body $body1
$reply1 = $res1.candidates[0].content.parts[0].text

Write-Host "`n>>> Teste 1: '$question1'" -ForegroundColor Cyan
Write-Host "Resposta do Bot:" -ForegroundColor Green
Write-Host $reply1

# Validação do Teste 1
if ($reply1 -match "Você é o assistente" -or $reply1 -match "Notas do cofre relevantes") {
    Write-Host "[FALHA] Resposta ainda contém eco do prompt!" -ForegroundColor Red
} else {
    Write-Host "[SUCESSO] Resposta limpa, direta e como chatbot!" -ForegroundColor Green
}

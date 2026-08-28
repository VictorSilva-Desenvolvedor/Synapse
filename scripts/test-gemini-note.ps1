$apiKey = "$env:GEMINI_API_KEY"
$model = "gemini-3.6-flash"

$prompt = @"
Você é o assistente inteligente de Segundo Cérebro (PKM) do Synapse para Obsidian.
Analise a anotação abaixo e responda ESTRITAMENTE em formato JSON com o seguinte schema:
{
  "title": "Título conciso, elegante e descritivo para a nota no Obsidian",
  "category": "Conceito | Ideia | Referencia | Projeto | Tarefa | Resumo | Pessoas",
  "tags": ["tag1", "tag2", "tag3"],
  "summary": "Resumo executivo de 1 a 2 frases",
  "keyPoints": ["Ponto principal 1", "Ponto principal 2"],
  "bodyMarkdown": "Texto formatado em Markdown com tabelas ou listas estruturadas",
  "suggestedConnections": ["Pessoas", "Amigos"]
}

Conteúdo bruto a processar:
---
tenho 1 amigo com nome felipe adicione na lista de pessoas que conheço se não existir crie uma
---
"@

$url = "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=$apiKey"
$headers = @{ "Content-Type" = "application/json" }

$body = @{
    contents = @(
        @{ parts = @( @{ text = $prompt } ) }
    )
    generationConfig = @{
        response_mime_type = "application/json"
        temperature = 0.2
    }
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body
$jsonText = $response.candidates[0].content.parts[0].text
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Resultado da IA com a Chave Gemini" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host $jsonText -ForegroundColor Yellow

$apiKey = "$env:GEMINI_API_KEY"
$vaultPath = if ($env:SYNAPSE_VAULT_PATH) { $env:SYNAPSE_VAULT_PATH } else { "$env:USERPROFILE\Obsidian\Vault" }
$userMessage = "ola tenho um amigo chamado felipe crie uma area para salvar meus amigos"

$headers = @{ "Content-Type" = "application/json" }

$categoryFolders = @("Pessoas", "Tarefas", "Ideias", "Projetos")
$categoryFoldersList = $categoryFolders -join ", "

$prompt = @"
Você é o arquiteto inteligente do Segundo Cérebro (PKM) do Synapse integrado ao Obsidian.
Sua missão é analisar a mensagem do usuário, compreender sua intenção e tomar a melhor decisão estrutural para o cofre.

Diretrizes de Tomada de Decisão:
1. CAPTURAR OU ORGANIZAR (ShouldCapture=true):
   - Quando o usuário informar fatos, ideias, contatos, tarefas, amigos, reuniões, dados de projetos ou pedir para criar áreas/listas/tabelas (ex.: 'tenho um amigo chamado felipe', 'crie uma área para salvar meus amigos', 'adicione na lista X').
   - Decisões estruturais:
     * "category": escolha uma pasta semântica lógica ('Pessoas' para amigos/contatos/pessoas, 'Tarefas' para afazeres/prazos, 'Projetos' para iniciativas, 'Conceito' para definições, 'Ideias' para pensamentos rápidos). Se já existir uma pasta relevante ($categoryFoldersList), use-a.
     * "title": título específico, conciso e elegante (ex.: 'Amigos', 'Lista de Amigos', 'Fulano', 'Planejamento Q4'). Nunca use títulos genéricos como 'Nova Anotação'.
     * "tags": tags relevantes sem '#' (ex.: ['pessoas', 'amigos', 'contatos']).
     * "bodyMarkdown": ESTRUTURA PROFISSIONAL EM MARKDOWN. Se for uma lista, área ou catálogo de informações, use tabelas Markdown elegantes (| Nome | Relação | Detalhes | Data |) e tópicos bem formatados. NUNCA inclua saudações ('Olá'), conversas, perguntas, meta-prompts ou repetições da instrução do usuário dentro do corpo da nota. Apenas os dados refinados e organizados.
     * "keyPoints": lista de pontos-chave sintetizados.
     * "suggestedConnections": nomes de notas do cofre para conexão com wikilinks [[...]].

2. RESPONDER COM BASE NO COFRE (ShouldAnswer=true):
   - Quando a mensagem for estritamente uma pergunta ou busca sobre notas existentes no cofre. Responda em "replyMessage" de forma clara citando as notas com [[Nome da Nota]].

3. CAMPO "replyMessage" (Resposta no chat):
   - Se ShouldCapture=true: Explique de forma breve, elegante e prestativa a decisão tomada no cofre (ex.: 'Criei a área de Pessoas e adicionei o Fulano na sua lista de amigos com uma tabela estruturada.').
   - Se apenas conversa/saudação: Responda amigavelmente sem capturar nada (ShouldCapture=false).

Responda ESTRITAMENTE em formato JSON com o seguinte schema:
{
  "shouldCapture": true,
  "title": "Título específico da nota",
  "category": "Pessoas",
  "tags": ["pessoas", "amigos"],
  "bodyMarkdown": "Conteúdo Markdown puro e estruturado da nota",
  "keyPoints": ["ponto 1"],
  "suggestedConnections": ["Pessoas"],
  "shouldAnswer": false,
  "replyMessage": "Resposta ou confirmação amigável da ação tomada"
}

Mensagem do usuário:
---
$userMessage
---
"@

$url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key=$apiKey"
$body = @{
    contents = @( @{ parts = @( @{ text = $prompt } ) } )
    generationConfig = @{ response_mime_type = "application/json"; temperature = 0.2 }
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body
$jsonText = $response.candidates[0].content.parts[0].text
$result = $jsonText | ConvertFrom-Json

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " DECISÃO TOMADA PELO SEGUNDO CÉREBRO" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Resposta no Chat:" -ForegroundColor Cyan
Write-Host $result.replyMessage -ForegroundColor Yellow
Write-Host "`nPasta Destino: $($result.category)/" -ForegroundColor Cyan
Write-Host "Título da Nota: $($result.title).md" -ForegroundColor Cyan
Write-Host "Tags: $( $result.tags -join ', ' )" -ForegroundColor Cyan
Write-Host "`nConteúdo Gerado para o Cofre:" -ForegroundColor Cyan
Write-Host $result.bodyMarkdown -ForegroundColor Green

# Grava no cofre do usuário na pasta correta
$targetDir = Join-Path $vaultPath $result.category
if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }

$notePath = Join-Path $targetDir "$($result.title).md"
$frontmatter = @"
---
titulo: "$($result.title)"
categoria: "$($result.category)"
criado_em: "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
status: processado
tags:
$( ($result.tags | ForEach-Object { "  - $_" }) -join "`n" )
---

# $($result.title)

$($result.bodyMarkdown)

## Conexões
$( ($result.suggestedConnections | ForEach-Object { "- [[$_]]" }) -join "`n" )
"@

Set-Content -Path $notePath -Value $frontmatter -Encoding UTF8
Write-Host "`n[SUCESSO] Nota gravada em: $notePath" -ForegroundColor Green

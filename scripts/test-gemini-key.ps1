param (
    [string]$ApiKey = "$env:GEMINI_API_KEY"
)

$modelsToTest = @("gemini-2.0-flash", "gemini-1.5-flash", "gemini-2.5-flash", "gemini-3.6-flash")

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   Testando Gemini API Key" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$headers = @{
    "Content-Type" = "application/json"
}

$body = @{
    contents = @(
        @{
            parts = @(
                @{ text = "Ola! Responda apenas com a palavra: FUNCIONANDO" }
            )
        }
    )
} | ConvertTo-Json -Depth 5

$success = $false

foreach ($model in $modelsToTest) {
    $url = "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=$ApiKey"
    Write-Host "Testando modelo: $model..." -NoNewline
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body -ErrorAction Stop
        $text = $response.candidates[0].content.parts[0].text
        Write-Host " [OK]" -ForegroundColor Green
        Write-Host "Resposta da API ($model): $text" -ForegroundColor Green
        $success = $true
        $workingModel = $model
        break
    } catch {
        Write-Host " [FALHA]" -ForegroundColor Red
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errBody = $reader.ReadToEnd()
            Write-Host "Erro HTTP ($($_.Exception.Response.StatusCode)): $errBody" -ForegroundColor DarkRed
        } else {
            Write-Host "Erro: $($_.Exception.Message)" -ForegroundColor DarkRed
        }
    }
}

# Também tenta listar os modelos disponíveis com a chave
Write-Host "`nConsultando lista de modelos suportados pela chave..." -ForegroundColor Cyan
$listUrl = "https://generativelanguage.googleapis.com/v1beta/models?key=$ApiKey"
try {
    $modelsList = Invoke-RestMethod -Uri $listUrl -Method Get -ErrorAction Stop
    Write-Host "Modelos disponíveis na conta:" -ForegroundColor Green
    foreach ($m in $modelsList.models) {
        if ($m.name -like "*gemini*") {
            Write-Host " - $($m.name) (display: $($m.displayName))"
        }
    }
} catch {
    Write-Host "Não foi possível listar modelos: $($_.Exception.Message)" -ForegroundColor Yellow
}

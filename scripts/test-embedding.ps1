$apiKey = "$env:GEMINI_API_KEY"
$models = @("gemini-embedding-2", "gemini-embedding-001", "text-embedding-004")

foreach ($m in $models) {
    Write-Host "Testando embedding model: $m..." -NoNewline
    $url = "https://generativelanguage.googleapis.com/v1beta/models/${m}:embedContent?key=$apiKey"
    $body = @{
        model = "models/$m"
        content = @{
            parts = @( @{ text = "Teste de busca semântica" } )
        }
    } | ConvertTo-Json -Depth 5

    try {
        $res = Invoke-RestMethod -Uri $url -Method Post -Headers @{"Content-Type"="application/json"} -Body $body
        Write-Host " [OK] Dimensions: $($res.embedding.values.Count)" -ForegroundColor Green
    } catch {
        Write-Host " [FALHA: $($_.Exception.Message)]" -ForegroundColor Red
    }
}

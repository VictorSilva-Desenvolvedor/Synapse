try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "synapse-ipc", [System.IO.Pipes.PipeDirection]::InOut)
    Write-Host "Connecting to \\.\pipe\synapse-ipc..."
    $pipe.Connect(3000)
    Write-Host "Connected to pipe!" -ForegroundColor Green

    $writer = New-Object System.IO.StreamWriter($pipe)
    $reader = New-Object System.IO.StreamReader($pipe)

    $request = '{"versao":1,"tipo":"GetStatus"}'
    $writer.WriteLine($request)
    $writer.Flush()

    $response = $reader.ReadLine()
    Write-Host "Response received:" -ForegroundColor Yellow
    Write-Host $response

    $pipe.Close()
} catch {
    Write-Host "IPC Error: $($_.Exception.Message)" -ForegroundColor Red
}

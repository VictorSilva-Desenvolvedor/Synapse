$appData = [Environment]::GetFolderPath("LocalApplicationData")
$synapseDir = Join-Path $appData "Synapse"

Write-Host "=== 1. Checking Synapse AppData Folder ===" -ForegroundColor Cyan
if (Test-Path $synapseDir) {
    Get-ChildItem $synapseDir | Format-Table Name, Length, LastWriteTime -AutoSize
} else {
    Write-Host "Synapse AppData dir ($synapseDir) does not exist." -ForegroundColor Yellow
}

$configFile = Join-Path $synapseDir "config.json"
Write-Host "=== 2. Checking config.json ===" -ForegroundColor Cyan
if (Test-Path $configFile) {
    $configJson = Get-Content $configFile -Raw
    Write-Host "config.json content:"
    Write-Host $configJson
} else {
    Write-Host "config.json NOT FOUND in $synapseDir" -ForegroundColor Yellow
}

$tokenFile = Join-Path $synapseDir "auth_token.dat"
Write-Host "=== 3. Checking auth_token.dat (DPAPI) ===" -ForegroundColor Cyan
if (Test-Path $tokenFile) {
    Write-Host "auth_token.dat exists ($( (Get-Item $tokenFile).Length ) bytes)." -ForegroundColor Green
} else {
    Write-Host "auth_token.dat NOT FOUND." -ForegroundColor Yellow
}

Write-Host "=== 4. Checking Running Processes ===" -ForegroundColor Cyan
Get-Process -Name "*Synapse*" -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, Path -AutoSize

Write-Host "=== 5. Checking Windows Service Status ===" -ForegroundColor Cyan
Get-Service -Name "Synapse" -ErrorAction SilentlyContinue | Format-Table Name, Status, DisplayName -AutoSize

Write-Host "=== 6. Checking Logs ===" -ForegroundColor Cyan
$logsDir = Join-Path $synapseDir "logs"
if (Test-Path $logsDir) {
    $logFiles = Get-ChildItem $logsDir | Sort-Object LastWriteTime -Descending
    foreach ($logFile in $logFiles | Select-Object -First 3) {
        Write-Host "--- Log: $($logFile.Name) ($($logFile.LastWriteTime)) ---" -ForegroundColor Yellow
        Get-Content $logFile.FullName -Tail 30
    }
} else {
    Write-Host "No logs directory found." -ForegroundColor Yellow
}

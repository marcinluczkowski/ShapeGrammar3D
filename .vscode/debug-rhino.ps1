$ErrorActionPreference = 'Stop'

$rhinoExe = "C:\Program Files\Rhino 8\System\Rhino.exe"
$csproj   = Join-Path $PSScriptRoot "..\ShapeGrammar3D\ShapeGrammar3D.csproj"

# 1. Kill Rhino if running
$rhino = Get-Process Rhino -ErrorAction SilentlyContinue
if ($rhino) {
    Write-Host ">>> Closing Rhino..."
    Stop-Process -Name Rhino -Force
    Start-Sleep -Seconds 3
}

# 2. Debug build
Write-Host ">>> Building..."
dotnet build $csproj -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host ">>> Build FAILED"
    exit 1
}

# 3. Start Rhino
Write-Host ">>> Starting Rhino..."
$proc = Start-Process $rhinoExe -ArgumentList "/nosplash" -PassThru

# 4. Wait for Rhino window
$timeout = 90
$elapsed = 0
while ($elapsed -lt $timeout) {
    $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if ($running -and $running.MainWindowHandle -ne 0) {
        break
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host ">>> Waiting for Rhino... ($elapsed s)"
}

# 5. Copy PID to clipboard
$proc.Id | Set-Clipboard
Write-Host ""
Write-Host "============================================"
Write-Host "  Rhino PID: $($proc.Id)  (clipboard ni copy zumi)"
Write-Host "  Dialog de Ctrl+V shite Enter"
Write-Host "============================================"
Write-Host ""
exit 0

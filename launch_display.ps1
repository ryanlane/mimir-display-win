# Quick Launch Script for Mimir Display with Environment Variables
# This ensures the MQTT configuration is set before starting the application

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Mimir Display - Launch with MQTT Configuration" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

# Set environment variables
$env:MIMIR__PLATFORMURL = "http://mimir.local:5000"
$env:MIMIR__MQTTBROKERHOST = "mimir.local"
$env:MIMIR__MQTTBROKERPORT = "1883"
$env:MIMIR__MQTTUSERNAME = "mimir-display"
$env:MIMIR__MQTTPASSWORD = "tu4kZj37jBvSGrXcKsB57k0x"
$env:MIMIR__MQTTHEARTBEATINTERVAL = "30"

Write-Host "✅ Environment variables set:" -ForegroundColor Green
Write-Host "  PLATFORMURL: $env:MIMIR__PLATFORMURL" -ForegroundColor Gray
Write-Host "  MQTTBROKERHOST: $env:MIMIR__MQTTBROKERHOST" -ForegroundColor Gray
Write-Host "  MQTTBROKERPORT: $env:MIMIR__MQTTBROKERPORT" -ForegroundColor Gray
Write-Host "  MQTTUSERNAME: $env:MIMIR__MQTTUSERNAME" -ForegroundColor Gray
Write-Host ""

# Find the exe
$exePath = ".\MimirDisplay\bin\Debug\net8.0-windows\win-x64\MimirDisplay.exe"
if (-not (Test-Path $exePath)) {
	$exePath = ".\MimirDisplay\bin\Release\net8.0-windows\win-x64\MimirDisplay.exe"
}

if (-not (Test-Path $exePath)) {
	Write-Host "❌ MimirDisplay.exe not found. Please build the project first." -ForegroundColor Red
	exit 1
}

Write-Host "🚀 Launching: $exePath" -ForegroundColor Yellow
Write-Host ""
Write-Host "Watch the display window for the pair code..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C in this window to stop monitoring." -ForegroundColor Gray
Write-Host ""

# Start the process
Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath -Parent)

# Wait a bit for startup
Start-Sleep -Seconds 3

# Monitor logs
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Recent Log Output:" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan

$logFile = Get-ChildItem "$env:APPDATA\MimirDisplay\logs" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName

if ($logFile) {
	Write-Host "Log file: $logFile" -ForegroundColor Gray
	Write-Host ""
	Get-Content $logFile -Tail 20 -Wait
}

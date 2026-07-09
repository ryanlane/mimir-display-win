# Test MQTT Connection to Mimir Server
# This PowerShell script tests if the display can connect to MQTT

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Mimir Display MQTT Connection Test" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

# Check if mosquitto_sub is available
$mosquittoSub = Get-Command mosquitto_sub -ErrorAction SilentlyContinue
if (-not $mosquittoSub) {
	Write-Host "❌ mosquitto_sub not found. Trying basic TCP test..." -ForegroundColor Yellow
	Write-Host ""

	# Test TCP connection
	Write-Host "Testing TCP connection to mimir.local:1883..." -ForegroundColor Yellow
	$tcpClient = New-Object System.Net.Sockets.TcpClient
	try {
		$tcpClient.Connect('mimir.local', 1883)
		if ($tcpClient.Connected) {
			Write-Host "✅ Port 1883 is accessible" -ForegroundColor Green
			$tcpClient.Close()
		}
	} catch {
		Write-Host "❌ Cannot connect to port 1883: $($_.Exception.Message)" -ForegroundColor Red
		exit 1
	}

	Write-Host ""
	Write-Host "TCP connection works, but cannot test MQTT login without mosquitto client." -ForegroundColor Yellow
	Write-Host "Install Mosquitto to test MQTT authentication:" -ForegroundColor Yellow
	Write-Host "  - Download: https://mosquitto.org/download/" -ForegroundColor Gray
	Write-Host "  - Or: winget install EclipseFoundation.Mosquitto" -ForegroundColor Gray
	Write-Host ""
	exit 0
}

Write-Host "✅ Found mosquitto_sub at: $($mosquittoSub.Path)" -ForegroundColor Green
Write-Host ""

# Test MQTT connection with authentication
Write-Host "Testing MQTT connection with authentication..." -ForegroundColor Yellow
Write-Host "  Host: mimir.local" -ForegroundColor Gray
Write-Host "  Port: 1883" -ForegroundColor Gray
Write-Host "  User: mimir-display" -ForegroundColor Gray
Write-Host ""

$testTopic = "mimir/test/connection"
$timeout = 5

Write-Host "Subscribing to test topic for $timeout seconds..." -ForegroundColor Yellow

$process = Start-Process -FilePath "mosquitto_sub" `
	-ArgumentList "-h", "mimir.local", "-p", "1883", `
				  "-u", "mimir-display", "-P", "tu4kZj37jBvSGrXcKsB57k0x", `
				  "-t", "mimir/#", "-v", "-C", "1" `
	-NoNewWindow `
	-PassThru `
	-RedirectStandardOutput "mqtt-test-output.tmp" `
	-RedirectStandardError "mqtt-test-error.tmp"

$success = $process.WaitForExit($timeout * 1000)

if ($success -and $process.ExitCode -eq 0) {
	Write-Host "✅ MQTT connection successful!" -ForegroundColor Green
	Write-Host ""
} elseif (Test-Path "mqtt-test-error.tmp") {
	$error = Get-Content "mqtt-test-error.tmp" -Raw
	if ($error -match "Connection refused|Authentication failed") {
		Write-Host "❌ MQTT authentication failed" -ForegroundColor Red
		Write-Host "Error: $error" -ForegroundColor Red
	} else {
		Write-Host "⚠️  MQTT connection had issues: $error" -ForegroundColor Yellow
	}
} else {
	Write-Host "✅ MQTT connection appears successful (timed out waiting for message)" -ForegroundColor Green
	if (-not $success -and -not $process.HasExited) {
		$process.Kill()
	}
}

# Cleanup
if (Test-Path "mqtt-test-output.tmp") { Remove-Item "mqtt-test-output.tmp" -Force }
if (Test-Path "mqtt-test-error.tmp") { Remove-Item "mqtt-test-error.tmp" -Force }

Write-Host ""
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Restart the Mimir Display application" -ForegroundColor White
Write-Host "  2. Check logs at: `$env:APPDATA\MimirDisplay\logs\" -ForegroundColor White
Write-Host "  3. Look for 'MQTT connected and subscribed'" -ForegroundColor White
Write-Host "  4. Monitor server for discovery announcements" -ForegroundColor White
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

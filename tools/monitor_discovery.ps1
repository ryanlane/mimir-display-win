# Mimir Display Discovery Monitor (PowerShell)
# Requires: Mosquitto CLI tools (mosquitto_sub.exe in PATH)
# Usage: .\monitor_discovery.ps1 [-BrokerHost "localhost"]

param(
	[string]$BrokerHost = "localhost",
	[int]$BrokerPort = 1883
)

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Mimir Display Discovery Monitor" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Broker: $BrokerHost`:$BrokerPort"
Write-Host "Time:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

# Check if mosquitto_sub is available
$mosquittoSub = Get-Command mosquitto_sub -ErrorAction SilentlyContinue
if (-not $mosquittoSub) {
	Write-Host "❌ Error: mosquitto_sub not found in PATH" -ForegroundColor Red
	Write-Host ""
	Write-Host "Please install Mosquitto MQTT:" -ForegroundColor Yellow
	Write-Host "  1. Download from: https://mosquitto.org/download/" -ForegroundColor Yellow
	Write-Host "  2. Or via Chocolatey: choco install mosquitto" -ForegroundColor Yellow
	Write-Host "  3. Or via winget: winget install EclipseFoundation.Mosquitto" -ForegroundColor Yellow
	Write-Host ""
	exit 1
}

Write-Host "✅ Connected to MQTT broker" -ForegroundColor Green
Write-Host "🎯 Listening for Mimir display announcements..." -ForegroundColor Cyan
Write-Host ""
Write-Host "📡 Subscribed to:" -ForegroundColor Yellow
Write-Host "   - mimir/discovery/announce (periodic announcements)"
Write-Host "   - mimir/registry/register (initial registration)"
Write-Host "   - mimir/registry/pair (pair codes)"
Write-Host "   - mimir/+/status (status updates)"
Write-Host "   - mimir/+/heartbeat (keepalive)"
Write-Host ""
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

$discovered = @{}

# Start mosquitto_sub and process output
$process = Start-Process -FilePath "mosquitto_sub" `
	-ArgumentList "-h", $BrokerHost, "-p", $BrokerPort, "-t", "mimir/#", "-v" `
	-NoNewWindow `
	-PassThru `
	-RedirectStandardOutput "mimir-mqtt-output.tmp"

# Monitor the output file
try {
	$lastPosition = 0
	while ($true) {
		Start-Sleep -Milliseconds 100

		if (Test-Path "mimir-mqtt-output.tmp") {
			$content = Get-Content "mimir-mqtt-output.tmp" -Raw -ErrorAction SilentlyContinue
			if ($content -and $content.Length -gt $lastPosition) {
				$newContent = $content.Substring($lastPosition)
				$lastPosition = $content.Length

				# Process each line
				$lines = $newContent -split "`n"
				foreach ($line in $lines) {
					if ($line -match "^([\w/+]+)\s+(.+)$") {
						$topic = $matches[1]
						$payload = $matches[2]

						try {
							$data = $payload | ConvertFrom-Json
							$timestamp = Get-Date -Format "HH:mm:ss"

							if ($topic -eq "mimir/discovery/announce") {
								$deviceId = $data.device_id
								$discovered[$deviceId] = $data

								Write-Host "[$timestamp] 📺 DISCOVERY ANNOUNCEMENT" -ForegroundColor Green
								Write-Host "  Device ID:   $deviceId"
								Write-Host "  Pair Code:   $($data.pair_code)" -ForegroundColor Yellow
								Write-Host "  Status:      $($data.status)"
								Write-Host "  Hostname:    $($data.metadata.hostname)"
								Write-Host "  Name:        $($data.metadata.name)"
								Write-Host "  Location:    $($data.metadata.location)"
								Write-Host "  Backend:     $($data.capabilities.backend)"
								Write-Host "  Resolution:  $($data.capabilities.resolution -join 'x')"
								Write-Host "  Timestamp:   $($data.timestamp)"
								Write-Host ""
							}
							elseif ($topic -eq "mimir/registry/register") {
								$deviceId = $data.device_id
								Write-Host "[$timestamp] ✅ REGISTRATION" -ForegroundColor Green
								Write-Host "  Device ID:   $deviceId"
								Write-Host "  Version:     $($data.client_version)"
								Write-Host "  Protocol:    $($data.protocol_version)"
								Write-Host ""
							}
							elseif ($topic -eq "mimir/registry/pair") {
								$deviceId = $data.device_id
								Write-Host "[$timestamp] 🔗 PAIR REQUEST" -ForegroundColor Cyan
								Write-Host "  Device ID:   $deviceId"
								Write-Host "  Pair Code:   $($data.pair_code)" -ForegroundColor Yellow
								Write-Host "  Hostname:    $($data.metadata.hostname)"
								Write-Host ""
							}
							elseif ($topic -like "mimir/*/status") {
								$deviceId = $topic.Split('/')[1]
								Write-Host "[$timestamp] 📊 STATUS UPDATE - $deviceId" -ForegroundColor Gray
								Write-Host "  Status:      $($data.status)"
								Write-Host ""
							}
							elseif ($topic -like "mimir/*/heartbeat") {
								$deviceId = $topic.Split('/')[1]
								Write-Host "[$timestamp] 💓 HEARTBEAT - $deviceId" -ForegroundColor DarkGray
								Write-Host ""
							}
						}
						catch {
							# Ignore JSON parse errors for non-JSON messages
						}
					}
				}
			}
		}
	}
}
finally {
	# Cleanup
	if (-not $process.HasExited) {
		$process.Kill()
	}

	Write-Host ""
	Write-Host "=" * 70 -ForegroundColor Cyan
	Write-Host "👋 Shutting down..." -ForegroundColor Yellow

	if ($discovered.Count -gt 0) {
		Write-Host ""
		Write-Host "📋 Discovered $($discovered.Count) display(s):" -ForegroundColor Green
		foreach ($entry in $discovered.GetEnumerator()) {
			Write-Host "  • $($entry.Key)" -ForegroundColor Cyan
			Write-Host "    Pair code: $($entry.Value.pair_code)" -ForegroundColor Yellow
			Write-Host "    Hostname:  $($entry.Value.metadata.hostname)"
		}
	}

	Write-Host "=" * 70 -ForegroundColor Cyan

	# Cleanup temp file
	if (Test-Path "mimir-mqtt-output.tmp") {
		Remove-Item "mimir-mqtt-output.tmp" -Force
	}
}

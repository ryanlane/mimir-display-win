# Quick Pairing Reference

## Successful Pairing Logs

```
✅ Environment Check - MIMIR__MQTTBROKERHOST: mimir.local
✅ Environment Check - MIMIR__PLATFORMURL: http://mimir.local:5000
✅ Config loaded - MqttBrokerHost: mimir.local, PlatformUrl: http://mimir.local:5000
✅ Connecting to MQTT broker mimir.local:1883 as [device-id]
✅ MQTT connected
✅ Sending registration request for [device-id]
✅ Pair code published: [CODE]
✅ Received command type=finalize_registration [optional]
✅ Registration finalized: display_id=[id] stored [optional]
✅ Received command type=display_image [content assignment]
```

## Failed Pairing - Config Not Loaded

```
❌ Config loaded - MqttBrokerHost: , PlatformUrl:
❌ Searching for Mimir server…
❌ Scanning for Mimir server via mDNS (_mimir._tcp.local.)…
```

**Fix**: Check environment variables are set correctly

## Failed Pairing - MQTT Not Connected

```
❌ Status: Connecting to MQTT broker…
   [no "MQTT connected" message follows]
```

**Fix**: Check broker connectivity, credentials, firewall

## Quick Tests

### 1. Check Environment Variables

```powershell
$env:MIMIR__MQTTBROKERHOST
$env:MIMIR__PLATFORMURL
```

### 2. Test MQTT Connectivity

```powershell
.\tools\test_mqtt_connection.ps1
```

### 3. Monitor MQTT Traffic

```powershell
.\tools\monitor_discovery.ps1
```

### 4. View Latest Logs

```powershell
Get-Content "$env:APPDATA\MimirDisplay\logs\mimir-*.log" -Tail 50
```

### 5. Check State File

```powershell
Get-Content "$env:APPDATA\MimirDisplay\state.json"
```

## Common Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| "Code not found or expired" | Display not publishing to MQTT | Fix env vars, rebuild, relaunch |
| Repeated mDNS scans | Config not loaded | Use `.\launch_display.ps1` |
| MQTT connection timeout | Network/firewall issue | Check `test_mqtt_connection.ps1` |
| Pair code changes on restart | New random code each launch | Normal until finalized |
| Display shows old content | State file has stale assignment | Delete state.json to reset |

## Environment Variables

```bash
# Required
MIMIR__PLATFORMURL=http://mimir.local:5000
MIMIR__MQTTBROKERHOST=mimir.local
MIMIR__MQTTBROKERPORT=1883
MIMIR__MQTTUSERNAME=mimir-display
MIMIR__MQTTPASSWORD=your-password-here

# Optional
MIMIR__MQTTHEARTBEATINTERVAL=30
MIMIR__FULLSCREEN=false  # Start in window mode
```

## Launch Command

```powershell
.\launch_display.ps1
```

Or set environment variables and run directly:

```powershell
cd MimirDisplay\bin\Debug\net8.0-windows\win-x64
.\MimirDisplay.exe
```

## Key Files

- **Logs**: `%APPDATA%\MimirDisplay\logs\mimir-*.log`
- **State**: `%APPDATA%\MimirDisplay\state.json`
- **Cache**: `%APPDATA%\MimirDisplay\cache\`
- **Config**: `.env` in exe directory or environment variables

## MQTT Topics

### Published by Display
- `mimir/registry/register` - Registration with pair code
- `mimir/registry/pair` - Pair request
- `mimir/[device-id]/heartbeat` - Every 30s

### Subscribed by Display
- `mimir/[device-id]/cmd` - Commands from server
- `mimir/[device-id]/pair/ack` - Pair acknowledgment

## Pairing Steps

1. **Display**: Generate pair code → publish to MQTT
2. **User**: Enter code in web UI
3. **Server**: Validate → create DisplayClient → publish finalize_registration
4. **Display**: Store display_id → acknowledge → ready for content

## Support

See full documentation:
- **PAIRING-GUIDE.md** - Complete guide
- **PAIRING-FIX-SUMMARY.md** - What was fixed
- **TROUBLESHOOTING.md** - Detailed troubleshooting

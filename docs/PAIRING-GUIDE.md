# Mimir Display - Pairing Guide

## Overview

The Mimir display client uses MQTT-based pairing to register with the Mimir server. This document explains the pairing flow and troubleshooting steps.

## Pairing Flow

### 1. Display Startup

When the display starts:

1. Loads `.env` configuration or environment variables
2. Generates a random 6-character pair code (e.g., `RNKZ98`)
3. Connects to the MQTT broker using credentials from config or server API
4. Publishes registration request to `mimir/registry/register`
5. Displays pair code on splash screen

### 2. Server-Side Pairing

On the server side (via web UI or API):

1. User enters the pair code
2. Server validates the code (600s TTL, checks for collisions)
3. Server retrieves device metadata from registration request
4. Server creates `DisplayClient` record in database
5. Server publishes `finalize_registration` command to display

### 3. Finalize Registration

The display receives `finalize_registration` command with:

```json
{
  "type": "finalize_registration",
  "display_id": "server-assigned-id",
  "registration_key": "auth-token",
  "config": { /* optional display config */ }
}
```

The display then:

1. Stores `display_id` and `registration_key` in persistent state
2. Marks itself as registered
3. Updates status text on screen
4. Acknowledges the finalize command

## Configuration

### Environment Variables

The display expects these environment variables (prefix `MIMIR__`):

```bash
# Required: Platform and MQTT connection
MIMIR__PLATFORMURL=http://mimir.local:5000
MIMIR__MQTTBROKERHOST=mimir.local
MIMIR__MQTTBROKERPORT=1883
MIMIR__MQTTUSERNAME=mimir-display
MIMIR__MQTTPASSWORD=your-password-here

# Optional: Heartbeat interval (default: 30)
MIMIR__MQTTHEARTBEATINTERVAL=30

# Optional: Window mode (default: false, starts windowed)
MIMIR__FULLSCREEN=false
```

### Configuration Binding

**IMPORTANT**: The configuration system uses double-underscore notation:

- ✅ **Correct**: `MIMIR__MQTTBROKERHOST` maps to `Mimir:MqttBrokerHost`
- ❌ **Wrong**: Using prefix `MIMIR__` in `AddEnvironmentVariables(prefix: "MIMIR__")` strips the prefix and breaks binding

The `App.xaml.cs` configuration must use:

```csharp
cfg.AddEnvironmentVariables(); // No prefix! Let MIMIR__ stay intact
```

## Troubleshooting

### "Code not found or expired"

**Symptoms**: Server rejects the pair code

**Causes**:
1. Display hasn't published the pair request to MQTT
2. MQTT broker connection is down or misconfigured
3. Code has expired (600s TTL)
4. Network connectivity issue between display and broker

**Resolution**:

1. Check display logs for MQTT connection:
   ```powershell
   Get-Content "$env:APPDATA\MimirDisplay\logs\mimir-*.log" -Tail 50
   ```

2. Look for these log entries:
   - ✅ `MQTT connected`
   - ✅ `Sending registration request for [device-id]`
   - ✅ `Pair code published: [CODE]`

3. If missing, check environment variables are loaded:
   - ✅ `Environment Check - MIMIR__MQTTBROKERHOST: mimir.local`
   - ✅ `Config loaded - MqttBrokerHost: mimir.local`

4. Test MQTT connectivity:
   ```powershell
   .\tools\test_mqtt_connection.ps1
   ```

5. Monitor MQTT traffic:
   ```powershell
   .\tools\monitor_discovery.ps1
   ```

### Display scans mDNS instead of connecting to MQTT

**Symptoms**: Logs show repeated `Scanning for Mimir server via mDNS`

**Cause**: Environment variables not loaded into configuration

**Resolution**:

1. Verify `.env` exists in the output directory:
   ```powershell
   Test-Path .\MimirDisplay\bin\Debug\net8.0-windows\win-x64\.env
   ```

2. Check environment variables are set:
   ```powershell
   # Launch with explicit environment variables
   .\launch_display.ps1
   ```

3. Verify config binding in logs:
   - Should see: `Config loaded - MqttBrokerHost: mimir.local`
   - Not: `Config loaded - MqttBrokerHost: , PlatformUrl:`

### Display connects but pairing times out

**Symptoms**: Display publishes pair code but server never finalizes

**Causes**:
1. Server not subscribed to `mimir/registry/register`
2. Server MQTT credentials mismatch
3. Server can't reach display command topic

**Resolution**:

1. Check server logs for pair request receipt
2. Verify server publishes `finalize_registration` to `mimir/[device-id]/cmd`
3. Check display command subscription in logs:
   - ✅ `MQTT connected and subscribed`

## Launch Scripts

### Windows PowerShell

Use `launch_display.ps1` to start with environment variables:

```powershell
.\launch_display.ps1
```

This script:
- Sets all required environment variables
- Launches the display executable
- Monitors logs in real-time

### Manual Launch

For debugging, launch directly:

```powershell
cd MimirDisplay\bin\Debug\net8.0-windows\win-x64
$env:MIMIR__MQTTBROKERHOST = "mimir.local"
$env:MIMIR__PLATFORMURL = "http://mimir.local:5000"
# ... set other vars
.\MimirDisplay.exe
```

## Test Matrix (from server-side)

| Scenario | Expected Result |
|----------|----------------|
| Display publishes pair request | ✅ status: pending, 600s TTL, correct ack topic |
| GET /pair/{code}/status | ✅ pending + device_id |
| POST /pair claim | ✅ DisplayClient created with metadata |
| finalize_registration sent | ✅ Display receives and stores display_id |
| Code expired/reused | ✅ Server returns 404, display generates new code |
| Code collision | ✅ Second device told to regenerate |
| Re-pair of registered device | ✅ Updates existing record, no duplicate |

## MQTT Topics

### Display → Server

- `mimir/registry/register` - Initial registration with pair code
- `mimir/registry/pair` - Pair request (device_id + pair_code)
- `mimir/discovery/announce` - Periodic discovery announcements
- `mimir/[device-id]/status` - Status updates
- `mimir/[device-id]/heartbeat` - Heartbeat (every 30s)
- `mimir/[device-id]/ack` - Command acknowledgments

### Server → Display

- `mimir/[device-id]/cmd` - Commands (assign, display_image, finalize_registration)
- `mimir/[device-id]/pair/ack` - Pair acknowledgment

## State Persistence

Display state is stored in:

```
%APPDATA%\MimirDisplay\state.json
```

Contains:
- `server_assigned_display_id` - ID assigned by server during finalization
- `registration_key` - Auth token for authenticated endpoints
- `registered` - Boolean flag

## Next Steps

After successful pairing:

1. Display is ready to receive content assignments
2. Server can send `display_image` or `assign` commands
3. Display reports status and heartbeat every 30s
4. Pair code is no longer needed (until device is reset)

## Support

- Check logs: `%APPDATA%\MimirDisplay\logs\`
- Use monitoring scripts in `tools/`
- Review DISCOVERY.md for MQTT discovery architecture
- See TROUBLESHOOTING.md for common issues

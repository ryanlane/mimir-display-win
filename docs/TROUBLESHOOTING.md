# Mimir Display Connection Issue - Resolution

## Problem Summary
The display was not connecting to the Mimir server at `mimir.local` because:
1. **mDNS auto-discovery was failing** - Display couldn't find `_mimir._tcp.local.` service
2. **No MQTT broker configured** - `.env` file had empty `MQTTBROKERHOST`
3. **Missing MQTT credentials** - Server requires authentication

## Solution Applied

### 1. Updated `.env` Configuration

Added direct MQTT broker configuration with credentials:

```env
MIMIR__PLATFORMURL=http://mimir.local:5000
MIMIR__MQTTBROKERHOST=mimir.local
MIMIR__MQTTBROKERPORT=1883
MIMIR__MQTTUSERNAME=mimir-display
MIMIR__MQTTPASSWORD=tu4kZj37jBvSGrXcKsB57k0x
```

**Why this works:**
- Bypasses mDNS discovery (which was failing)
- Provides direct MQTT broker address
- Includes authentication credentials from server

## Testing Steps

### 1. Verify Connectivity (✅ PASSED)
```powershell
Test-NetConnection mimir.local -Port 1883
# Result: Connection successful, port accessible
```

### 2. Verify Server Health (✅ PASSED)
```powershell
curl http://mimir.local:5000/api/health
# Result: Server is healthy, MQTT broker is running
```

### 3. Restart Display Application

**Now you need to restart the Mimir Display application:**

1. **Close the current display** (press Escape or close window)
2. **Run the application again** from Visual Studio or exe
3. **Watch for these log messages:**
   ```
   ✅ "Connecting to MQTT broker mimir.local:1883"
   ✅ "MQTT connected and subscribed"
   ✅ "Sending registration request"
   ✅ "Pair code published: XXXXXX"
   ✅ "Discovery announcement published"
   ```

### 4. Check Logs
```powershell
# View latest log file
Get-ChildItem "$env:APPDATA\MimirDisplay\logs" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 20
```

**Look for:**
- ✅ "MQTT connected and subscribed"
- ✅ "Discovery announcement published"
- ❌ "MQTT connection failed" or "Authentication failed"

## What Should Happen Now

### On the Display Side:
1. **Connects to MQTT broker** at `mimir.local:1883`
2. **Authenticates** with username/password
3. **Publishes:**
   - Registration: `mimir/registry/register`
   - Pair code: `mimir/registry/pair`
   - Discovery: `mimir/discovery/announce`
   - Status: `mimir/{device-id}/status`
4. **Shows pair code** on splash screen

### On the Server Side:
The server should see the display if it's subscribing to:
- `mimir/discovery/announce`
- `mimir/registry/register`
- `mimir/registry/pair`
- `mimir/+/status`

## Server-Side Verification

If you have access to the server, verify it's listening:

```bash
# Subscribe to all mimir topics
mosquitto_sub -h localhost -p 1884 -t "mimir/#" -v

# Or specifically discovery announcements
mosquitto_sub -h localhost -p 1884 -t "mimir/discovery/announce" -v
```

**Note:** Server uses port **1884** internally (likely Docker), but exposes **1883** externally.

## Pairing Process

Once the display connects:

### Option 1: Auto-Discovery (Recommended)
If your server is properly listening to `mimir/discovery/announce`, it should automatically discover the display within 3 minutes.

### Option 2: Manual Pairing with Code
1. **Note the pair code** shown on the display splash screen (e.g., "ABC123")
2. **Enter it in your server's pairing UI**
3. **Server looks up device_id** from the pair code
4. **Server sends assignment** to `mimir/{device-id}/cmd`

### Option 3: Direct Command (If you know device_id)
```bash
# Get device ID from display logs or hostname
DEVICE_ID="your-hostname-slug"

# Send assignment command
mosquitto_pub -h mimir.local -p 1883 \
  -u mimir-display -P tu4kZj37jBvSGrXcKsB57k0x \
  -t "mimir/$DEVICE_ID/cmd" \
  -m '{"type":"assign","scene_id":"main","subchannel_id":"1"}'
```

## Troubleshooting

### If Display Still Doesn't Connect

1. **Check .env is being loaded:**
   ```powershell
   # Verify the file exists next to the exe
   Test-Path ".\MimirDisplay\.env"
   ```

2. **Check for typos in credentials:**
   ```powershell
   # View current .env
   Get-Content MimirDisplay\.env | Select-String "MQTT"
   ```

3. **Watch logs in real-time:**
   ```powershell
   Get-Content "$env:APPDATA\MimirDisplay\logs\mimir-$(Get-Date -Format 'yyyy-MM-dd').log" -Wait
   ```

4. **Test MQTT directly with mosquitto_pub:**
   ```powershell
   # Install mosquitto first: winget install EclipseFoundation.Mosquitto
   mosquitto_pub -h mimir.local -p 1883 `
	 -u mimir-display -P tu4kZj37jBvSGrXcKsB57k0x `
	 -t "mimir/test" -m "hello"
   ```

### If Pairing Code Fails

1. **Verify display is connected to MQTT** (check logs for "MQTT connected")
2. **Check if pair code was published** (look for "Pair code published" in logs)
3. **Verify server is subscribed** to `mimir/registry/pair`
4. **Check server logs** for pair code receipt
5. **Try manual assignment** using device_id directly

### Common Issues

| Symptom | Cause | Solution |
|---------|-------|----------|
| "Scanning for Mimir server via mDNS..." | .env not loaded or empty | Set `MIMIR__MQTTBROKERHOST` |
| "MQTT connection failed" | Wrong host/port | Verify mimir.local:1883 |
| "Authentication failed" | Wrong credentials | Check username/password in `.env` |
| Display connects but no pairing | Server not subscribed | Server must subscribe to `mimir/registry/pair` |
| Pair code doesn't work | Code expired or not found | Check server logs, try direct assignment |

## Expected Timeline

```
0:00  - Display starts
0:01  - Connects to MQTT broker
0:01  - Publishes registration + pair code + discovery
0:01  - Shows pair code on splash screen
0:30  - First heartbeat
3:00  - Second discovery announcement
...   - Continues heartbeat every 30s, discovery every ~3min
```

## Next Actions

1. **Restart the display application now**
2. **Watch for MQTT connection in logs**
3. **Note the pair code displayed**
4. **Wait 10-30 seconds** for discovery announcements
5. **Check server side** to see if display appears
6. **If server doesn't auto-detect**, use pair code manually
7. **If pairing fails**, check server logs and MQTT subscriptions

## Files You Can Check

- **Display logs:** `%APPDATA%\MimirDisplay\logs\mimir-*.log`
- **Config file:** `MimirDisplay\.env` (in your project folder)
- **Test script:** `tools\test_mqtt_connection.ps1`
- **Monitor script:** `tools\monitor_discovery.ps1`

## Documentation References

For more details, see:
- **[DISCOVERY.md](../DISCOVERY.md)** - Full discovery architecture
- **[SERVER-DISCOVERY-GUIDE.md](../SERVER-DISCOVERY-GUIDE.md)** - Server implementation
- **[UPDATE-NOTES.md](../UPDATE-NOTES.md)** - Recent changes

---

**Status: Configuration complete. Ready to test connection.**

Please restart the display and check logs for "MQTT connected and subscribed".

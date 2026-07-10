# Mimir Display Updates - Window Mode & Enhanced Discovery

## Summary of Changes

This update adds two major improvements to the Mimir Display application:

### 1. ✅ Window Mode with Fullscreen Toggle
- **Launches in windowed mode by default** (1280x720 resizable window)
- **F11 key** toggles fullscreen mode on/off
- **Environment variable** `MIMIR__FULLSCREEN=true` to start in fullscreen
- Better for development and testing

### 2. ✅ Enhanced MQTT-Based Discovery
- **New discovery announcement topic**: `mimir/discovery/announce`
- **Periodic announcements** every ~3 minutes (configurable)
- **Helps servers find displays** even if they missed initial registration
- **More reliable** than mDNS across network boundaries

## What Changed

### Window Behavior

**Before:**
- Always launched in fullscreen (borderless, maximized)
- Windowed mode only via debugger or `MIMIR__WINDOWED=true`
- No easy way to switch between modes

**After:**
- Launches in normal window by default
- **Press F11** to toggle fullscreen mode
- Set `MIMIR__FULLSCREEN=true` for kiosk mode
- Escape key still exits the application

### Discovery Mechanism

**Before:**
- Display registered once on MQTT connect
- If server missed registration, manual pairing needed
- Only mDNS for server discovery (display didn't advertise itself)

**After:**
- Display announces itself periodically via MQTT
- Server can discover displays by subscribing to `mimir/discovery/announce`
- Works across subnets/VLANs (unlike mDNS)
- More reliable for auto-discovery scenarios

## Usage

### For Users

#### Normal Operation (Windowed)
```bash
# Just run the application
MimirDisplay.exe

# Press F11 to toggle fullscreen
# Press Escape to exit
```

#### Kiosk Mode (Fullscreen at Startup)
```bash
# Set environment variable
$env:MIMIR__FULLSCREEN="true"
MimirDisplay.exe

# Or add to .env file:
# MIMIR__FULLSCREEN=true
```

### For Developers

#### Server-Side Discovery

**Subscribe to discovery announcements:**
```javascript
mqtt.subscribe('mimir/discovery/announce');

mqtt.on('message', (topic, payload) => {
  if (topic === 'mimir/discovery/announce') {
	const display = JSON.parse(payload);
	console.log(`Discovered: ${display.device_id}`);
	console.log(`Pair code: ${display.pair_code}`);
  }
});
```

**Python example:**
```python
import paho.mqtt.client as mqtt

def on_message(client, userdata, msg):
	if msg.topic == "mimir/discovery/announce":
		display = json.loads(msg.payload)
		print(f"Discovered: {display['device_id']}")
		print(f"Pair code: {display['pair_code']}")

client = mqtt.Client()
client.on_message = on_message
client.connect("localhost", 1883)
client.subscribe("mimir/discovery/announce")
client.loop_forever()
```

#### Monitor Discovery Activity

Use the provided monitoring tools:

**Python:**
```bash
cd tools
python monitor_discovery.py localhost
```

**PowerShell:**
```powershell
cd tools
.\monitor_discovery.ps1 -BrokerHost localhost
```

**Mosquitto CLI:**
```bash
mosquitto_sub -h localhost -t "mimir/discovery/announce" -v
```

## Configuration

### New Environment Variables

```bash
# Window mode control
MIMIR__FULLSCREEN=false          # true = start fullscreen, false = windowed

# MQTT heartbeat (affects announcement frequency)
MIMIR__MQTTHEARTBEATINTERVAL=30  # seconds between heartbeats
```

Discovery announcements happen every 6th heartbeat (default: every 3 minutes).

## MQTT Topics Reference

| Topic | Purpose | Frequency |
|-------|---------|-----------|
| `mimir/discovery/announce` | **Periodic discovery** | Every ~3 min |
| `mimir/registry/register` | Initial registration | Once on connect |
| `mimir/registry/pair` | Pair code broadcast | Once on connect |
| `mimir/{id}/status` | Status updates (retained) | On change |
| `mimir/{id}/heartbeat` | Keepalive | Every 30s |
| `mimir/{id}/cmd` | Inbound commands | As needed |
| `mimir/{id}/evt` | Outbound events | On event |

## Documentation

See the following files for detailed information:

- **[DISCOVERY.md](DISCOVERY.md)** - Complete discovery architecture and protocols
- **[SERVER-DISCOVERY-GUIDE.md](SERVER-DISCOVERY-GUIDE.md)** - Quick start for server implementation
- **[tools/monitor_discovery.py](tools/monitor_discovery.py)** - Python monitoring script
- **[tools/monitor_discovery.ps1](tools/monitor_discovery.ps1)** - PowerShell monitoring script

## Discovery Flow Diagram

```
Display Startup
	  │
	  ├─ Connect to MQTT broker (auto-discover via mDNS if needed)
	  │
	  ├─ Publish initial messages:
	  │  ├─ mimir/registry/register (full registration)
	  │  ├─ mimir/registry/pair (pair code)
	  │  ├─ mimir/discovery/announce (discovery)
	  │  └─ mimir/{id}/status (retained status)
	  │
	  ├─ Start heartbeat loop (every 30s):
	  │  ├─ mimir/{id}/heartbeat
	  │  └─ Every 6th heartbeat:
	  │     └─ mimir/discovery/announce (re-announce)
	  │
	  └─ Listen for commands:
		 └─ mimir/{id}/cmd
```

## Why MQTT Discovery Over mDNS?

| Feature | MQTT | mDNS |
|---------|------|------|
| Cross-subnet | ✅ Yes | ❌ No |
| Persistent connection | ✅ Yes | ❌ No |
| Bi-directional | ✅ Yes | ❌ No |
| QoS guarantees | ✅ Yes | ❌ No |
| Server-side complexity | ✅ Simple | ⚠️ Complex |
| Scalability | ✅ Excellent | ⚠️ Limited |

**Recommendation:** Use MQTT discovery as the primary mechanism. mDNS is still used for *finding* the MQTT broker, but display discovery happens via MQTT messages.

## Testing

### Test Window Modes
1. Run application normally - should start windowed
2. Press F11 - should enter fullscreen
3. Press F11 again - should return to windowed
4. Set `MIMIR__FULLSCREEN=true` and restart - should start fullscreen

### Test Discovery
1. Start MQTT broker (e.g., Mosquitto)
2. Run monitor script: `python tools/monitor_discovery.py`
3. Start display application
4. Should see:
   - Initial registration
   - Pair code announcement
   - Discovery announcement (immediately and every ~3 min)
   - Status updates
   - Heartbeats

### Test Server Integration
1. Subscribe to `mimir/discovery/announce` on your server
2. Start one or more display applications
3. Server should receive discovery announcements
4. Extract device_id and pair_code from announcements
5. Send test command to `mimir/{device_id}/cmd`

## Troubleshooting

### Display not detected by server?

1. **Check MQTT broker is running:**
   ```bash
   mosquitto -v
   ```

2. **Monitor MQTT traffic:**
   ```bash
   mosquitto_sub -h localhost -t "mimir/#" -v
   ```

3. **Check display logs:**
   - Location: `%APPDATA%\MimirDisplay\logs\`
   - Look for "Discovery announcement published"

4. **Verify server subscription:**
   - Server must subscribe to `mimir/discovery/announce`
   - Check server MQTT client connection

### Window mode not working?

1. **Check environment variable:**
   ```powershell
   $env:MIMIR__FULLSCREEN
   ```

2. **Check .env file:**
   - Location: Same folder as MimirDisplay.exe
   - Must be named exactly `.env`

3. **Check logs:**
   - Look for "Starting in windowed mode" or "Starting in fullscreen mode"

## Migration Notes

### Upgrading from Previous Version

1. **No breaking changes** - all existing functionality preserved
2. **Default behavior changed** - now starts windowed instead of fullscreen
3. **To keep old behavior** - set `MIMIR__FULLSCREEN=true`
4. **New MQTT topic** - server should subscribe to `mimir/discovery/announce`
5. **Heartbeat config** - now uses `MQTTHEARTBEATINTERVAL` (old config still works)

### Server-Side Updates Recommended

Add subscription to discovery announcements for better display detection:

```diff
  mqtt.subscribe("mimir/registry/register");
  mqtt.subscribe("mimir/registry/pair");
+ mqtt.subscribe("mimir/discovery/announce");
```

This allows your server to:
- Discover displays that connected before server startup
- Re-discover displays after network interruptions
- Find displays across subnets/VLANs

## Support

For issues or questions:
1. Check logs: `%APPDATA%\MimirDisplay\logs\`
2. Monitor MQTT: `mosquitto_sub -t "mimir/#" -v`
3. Review documentation: DISCOVERY.md and SERVER-DISCOVERY-GUIDE.md
4. Test with monitoring tools in `tools/` directory

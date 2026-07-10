# Mimir Display Discovery & Pairing

This document explains how the Mimir Display announces itself and pairs with the server.

## Discovery Architecture

The display uses **MQTT-based discovery** as the primary mechanism for server-side detection. This provides several advantages over mDNS:

- ✅ **Works across subnets/VLANs** (mDNS is limited to local broadcast domains)
- ✅ **Persistent connection** for real-time communication
- ✅ **Bi-directional** communication for commands and responses
- ✅ **Reliable delivery** with QoS guarantees
- ✅ **Centralized** - server subscribes to one topic for all displays

## MQTT Discovery Topics

### 1. Registration Topic: `mimir/registry/register`

**Published once on connect** with full device information.

```json
{
  "device_id": "display-hostname",
  "capabilities": {
	"backend": "windows",
	"resolution": [1920, 1080],
	"native_resolution": [1920, 1080],
	"orientation": "landscape",
	"rotation_deg": 0,
	"supported_formats": ["png", "jpeg", "jpg", "bmp", "gif", "webp"],
	"supports_animation": true,
	"simulation_mode": false
  },
  "metadata": {
	"name": "Mimir Windows Display",
	"location": "Unknown",
	"hostname": "COMPUTER-NAME",
	"client_version": "1.0.0",
	"protocol_version": "1",
	"tags": []
  },
  "client_version": "1.0.0",
  "protocol_version": "1",
  "reply_to": "mimir/display-hostname/reg/reply",
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

### 2. Pair Request Topic: `mimir/registry/pair`

**Published once on connect** with pairing code for manual pairing UI.

```json
{
  "device_id": "display-hostname",
  "pair_code": "ABC123",
  "metadata": { ... },
  "capabilities": { ... },
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

### 3. Discovery Announcement Topic: `mimir/discovery/announce` ⭐ NEW

**Published periodically** (every ~3 minutes by default) to help servers discover displays even if they missed the initial registration.

```json
{
  "device_id": "display-hostname",
  "pair_code": "ABC123",
  "status": "online",
  "capabilities": { ... },
  "metadata": { ... },
  "timestamp": "2024-01-15T10:30:00.000Z",
  "protocol_version": "1"
}
```

**Why periodic announcements?**
- Server might restart and miss initial registration
- Network interruptions can cause missed messages
- New server instances can discover existing displays
- Provides ongoing "liveness" signal beyond heartbeats

**Timing:**
- Published immediately on MQTT connect
- Re-published every 6th heartbeat cycle
- Default: every 3 minutes (with 30-second heartbeat interval)

### 4. Presence/Status Topic: `mimir/{device_id}/status`

**Retained message** updated on connect, heartbeat, and disconnect.

```json
{
  "device_id": "display-hostname",
  "status": "online",
  "timestamp": "2024-01-15T10:30:00.000Z",
  "capabilities": { ... },
  "metadata": { ... },
  "assigned_scene_id": null,
  "assigned_subchannel_id": null,
  "last_displayed": null,
  "pair_code": "ABC123"
}
```

### 5. Heartbeat Topic: `mimir/{device_id}/heartbeat`

**Published every 30 seconds** (configurable via `MqttHeartbeatInterval`).

```json
{
  "device_id": "display-hostname",
  "timestamp": "2024-01-15T10:30:00.000Z",
  "uptime_seconds": 3600,
  "assigned_scene_id": "scene-123",
  "assigned_subchannel_id": "subchannel-456"
}
```

## Server-Side Implementation

### Subscribing to Display Announcements

```javascript
// Subscribe to discovery announcements
mqtt.subscribe('mimir/discovery/announce');

mqtt.on('message', (topic, payload) => {
  if (topic === 'mimir/discovery/announce') {
	const display = JSON.parse(payload);
	console.log(`Discovered display: ${display.device_id}`);
	console.log(`Pair code: ${display.pair_code}`);
	console.log(`Hostname: ${display.metadata.hostname}`);

	// Add to known displays list
	registerDisplay(display);
  }
});
```

### Subscribing to Initial Registration

```javascript
// Subscribe to new device registrations
mqtt.subscribe('mimir/registry/register');
mqtt.subscribe('mimir/registry/pair');

mqtt.on('message', (topic, payload) => {
  const data = JSON.parse(payload);

  if (topic === 'mimir/registry/register') {
	console.log(`New display registered: ${data.device_id}`);
	// Send registration reply if needed
	mqtt.publish(`mimir/${data.device_id}/reg/reply`, JSON.stringify({
	  assigned_id: data.device_id,
	  config: {}
	}));
  }

  if (topic === 'mimir/registry/pair') {
	console.log(`Pair code ${data.pair_code} for ${data.device_id}`);
	// Show in pairing UI
	showPairCode(data.pair_code, data.device_id);
  }
});
```

### Monitoring All Displays

```javascript
// Subscribe to all display status topics using wildcards
mqtt.subscribe('mimir/+/status');
mqtt.subscribe('mimir/+/heartbeat');

mqtt.on('message', (topic, payload) => {
  const parts = topic.split('/');
  const deviceId = parts[1];
  const messageType = parts[2];

  if (messageType === 'status') {
	const status = JSON.parse(payload);
	updateDisplayStatus(deviceId, status);
  }

  if (messageType === 'heartbeat') {
	const heartbeat = JSON.parse(payload);
	updateDisplayHeartbeat(deviceId, heartbeat);
  }
});
```

## Configuration

### Display-Side (Environment Variables)

```bash
# MQTT broker connection (auto-discovered via mDNS if not set)
MIMIR__MQTTBROKERHOST=192.168.1.100
MIMIR__MQTTBROKERPORT=1883

# Optional: MQTT authentication
MIMIR__MQTTUSERNAME=display
MIMIR__MQTTPASSWORD=secret

# Heartbeat interval (seconds) - affects announcement frequency
MIMIR__MQTTHEARTBEATINTERVAL=30

# Device identity
MIMIR__DISPLAYID=display-living-room
MIMIR__DISPLAYNAME=Living Room Display
MIMIR__DISPLAYLOCATION=Living Room
MIMIR__DISPLAYTAGS=main-floor,public

# Start in fullscreen mode
MIMIR__FULLSCREEN=true
```

### Discovery Flow

```
┌─────────────────┐                    ┌─────────────────┐
│  Mimir Display  │                    │  Mimir Server   │
└────────┬────────┘                    └────────┬────────┘
		 │                                      │
		 │  1. Discover MQTT broker via mDNS   │
		 │     (if MQTTBROKERHOST not set)     │
		 │◄─────────────────────────────────────┤
		 │                                      │
		 │  2. Connect to MQTT broker           │
		 ├──────────────────────────────────────►
		 │                                      │
		 │  3. Publish to:                      │
		 │     - mimir/registry/register        │
		 │     - mimir/registry/pair            │
		 │     - mimir/discovery/announce       │
		 │     - mimir/{id}/status (retained)   │
		 ├──────────────────────────────────────►
		 │                                      │
		 │  4. Server subscribes to:            │
		 │     - mimir/discovery/announce       │
		 │     - mimir/registry/+               │
		 │     - mimir/+/status                 │
		 │                                      │◄─┐
		 │                                      │  │ Listen
		 │  5. Periodic heartbeat & announce    │  │
		 ├──────────────────────────────────────►  │
		 │     every 30s (heartbeat)            │  │
		 │     every ~3min (announce)           │  │
		 │                                      │◄─┘
		 │                                      │
		 │  6. Server sends commands            │
		 │◄─────────────────────────────────────┤
		 │     mimir/{id}/cmd                   │
		 │                                      │
		 │  7. Display publishes events         │
		 ├──────────────────────────────────────►
		 │     mimir/{id}/evt                   │
		 │                                      │
```

## Pairing Methods

### 1. Automatic Discovery (Recommended)

Server subscribes to `mimir/discovery/announce` and automatically discovers all online displays. No user intervention needed.

### 2. Manual Pair Code

1. Display shows pair code on splash screen (e.g., "ABC123")
2. User enters pair code in server UI
3. Server looks up device_id from pair request
4. Server sends assignment command to `mimir/{device_id}/cmd`

### 3. Direct Assignment

If you know the device ID (from hostname or config), you can send commands directly:

```bash
mosquitto_pub -t "mimir/display-hostname/cmd" -m '{
  "type": "assign",
  "scene_id": "main-scene",
  "subchannel_id": "channel-1"
}'
```

## Troubleshooting

### Display not detected by server?

1. **Check MQTT broker connection**
   - Verify `MIMIR__MQTTBROKERHOST` is set or mDNS discovery works
   - Check firewall rules (MQTT port 1883)
   - Test with `mosquitto_sub -h <broker> -t "mimir/#" -v`

2. **Check server is subscribing to discovery topics**
   - Server should subscribe to `mimir/discovery/announce`
   - Check server logs for MQTT subscription confirmation

3. **Monitor MQTT traffic**
   ```bash
   # Watch all Mimir MQTT traffic
   mosquitto_sub -h <broker> -t "mimir/#" -v

   # Watch only discovery announcements
   mosquitto_sub -h <broker> -t "mimir/discovery/announce" -v

   # Watch specific display
   mosquitto_sub -h <broker> -t "mimir/display-hostname/#" -v
   ```

4. **Check display logs**
   - Location: `%APPDATA%\MimirDisplay\logs\`
   - Look for "Discovery announcement published"
   - Verify MQTT connection succeeded

### Display connects but doesn't receive commands?

1. Check topic subscription: Display subscribes to `mimir/{device_id}/cmd`
2. Verify device_id matches between server and display
3. Check QoS levels (commands use QoS 1 for reliability)

## Comparison: MQTT vs mDNS Discovery

| Feature | MQTT Discovery | mDNS Discovery |
|---------|----------------|----------------|
| **Cross-subnet** | ✅ Yes | ❌ No (local only) |
| **Persistent connection** | ✅ Yes | ❌ No |
| **Bi-directional** | ✅ Yes | ❌ No |
| **Reliability** | ✅ QoS guarantees | ⚠️ Best effort UDP |
| **Scalability** | ✅ Excellent | ⚠️ Limited |
| **Latency** | ✅ Real-time | ⚠️ Must poll |
| **Server implementation** | ✅ Simple (subscribe) | ⚠️ Complex (scanning) |
| **Infrastructure** | Requires MQTT broker | No infrastructure |

## Conclusion

**MQTT-based discovery is the recommended approach** because:

1. It's already part of the core communication protocol
2. Works reliably across network boundaries
3. Provides real-time bi-directional communication
4. Easier to implement server-side (simple subscription)
5. More scalable for multiple displays

The addition of the **`mimir/discovery/announce`** topic provides a robust discovery mechanism that works even when servers restart or displays connect asynchronously.

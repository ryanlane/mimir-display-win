# Server-Side Display Discovery - Quick Start

## Minimal Server Implementation

Here's the simplest way to discover and communicate with Mimir displays from your server:

### Python Example (using paho-mqtt)

```python
import paho.mqtt.client as mqtt
import json
from datetime import datetime

# Store discovered displays
displays = {}

def on_connect(client, userdata, flags, rc):
	print(f"Connected to MQTT broker with code {rc}")

	# Subscribe to discovery announcements (recommended)
	client.subscribe("mimir/discovery/announce")

	# Also subscribe to initial registration if you want
	client.subscribe("mimir/registry/register")
	client.subscribe("mimir/registry/pair")

	# Subscribe to all display status updates
	client.subscribe("mimir/+/status")
	client.subscribe("mimir/+/heartbeat")

def on_message(client, userdata, msg):
	try:
		payload = json.loads(msg.payload.decode())
		topic = msg.topic

		# Discovery announcement (most important for finding displays)
		if topic == "mimir/discovery/announce":
			device_id = payload["device_id"]
			displays[device_id] = {
				"device_id": device_id,
				"pair_code": payload["pair_code"],
				"status": payload["status"],
				"hostname": payload["metadata"]["hostname"],
				"name": payload["metadata"]["name"],
				"location": payload["metadata"]["location"],
				"capabilities": payload["capabilities"],
				"last_seen": datetime.utcnow()
			}
			print(f"📺 Display discovered: {device_id}")
			print(f"   Pair code: {payload['pair_code']}")
			print(f"   Hostname: {payload['metadata']['hostname']}")
			print(f"   Location: {payload['metadata']['location']}")
			print()

		# Initial registration
		elif topic == "mimir/registry/register":
			device_id = payload["device_id"]
			print(f"✅ New display registered: {device_id}")

			# Send registration reply (optional)
			client.publish(
				f"mimir/{device_id}/reg/reply",
				json.dumps({"assigned_id": device_id, "config": {}}),
				qos=1
			)

		# Pair request
		elif topic == "mimir/registry/pair":
			device_id = payload["device_id"]
			pair_code = payload["pair_code"]
			print(f"🔗 Pair code received: {pair_code} for {device_id}")

		# Status update
		elif "/status" in topic:
			device_id = topic.split("/")[1]
			if device_id in displays:
				displays[device_id]["status"] = payload["status"]
				displays[device_id]["last_seen"] = datetime.utcnow()

		# Heartbeat
		elif "/heartbeat" in topic:
			device_id = topic.split("/")[1]
			if device_id in displays:
				displays[device_id]["last_seen"] = datetime.utcnow()

	except Exception as e:
		print(f"Error processing message: {e}")

# Create MQTT client
client = mqtt.Client(client_id="mimir-server")
client.on_connect = on_connect
client.on_message = on_message

# Connect to broker
client.connect("localhost", 1883, 60)

# Start listening
print("🎯 Listening for Mimir displays...")
client.loop_forever()
```

### Node.js Example (using mqtt.js)

```javascript
const mqtt = require('mqtt');

// Store discovered displays
const displays = new Map();

// Connect to MQTT broker
const client = mqtt.connect('mqtt://localhost:1883', {
  clientId: 'mimir-server'
});

client.on('connect', () => {
  console.log('Connected to MQTT broker');

  // Subscribe to discovery topics
  client.subscribe('mimir/discovery/announce');
  client.subscribe('mimir/registry/register');
  client.subscribe('mimir/registry/pair');
  client.subscribe('mimir/+/status');
  client.subscribe('mimir/+/heartbeat');
});

client.on('message', (topic, payload) => {
  try {
	const data = JSON.parse(payload.toString());

	// Discovery announcement - most important!
	if (topic === 'mimir/discovery/announce') {
	  const deviceId = data.device_id;
	  displays.set(deviceId, {
		deviceId,
		pairCode: data.pair_code,
		status: data.status,
		hostname: data.metadata.hostname,
		name: data.metadata.name,
		location: data.metadata.location,
		capabilities: data.capabilities,
		lastSeen: new Date()
	  });

	  console.log(`📺 Display discovered: ${deviceId}`);
	  console.log(`   Pair code: ${data.pair_code}`);
	  console.log(`   Hostname: ${data.metadata.hostname}`);
	  console.log();
	}

	// Initial registration
	else if (topic === 'mimir/registry/register') {
	  console.log(`✅ New display registered: ${data.device_id}`);

	  // Send registration reply (optional)
	  client.publish(
		`mimir/${data.device_id}/reg/reply`,
		JSON.stringify({ assigned_id: data.device_id, config: {} }),
		{ qos: 1 }
	  );
	}

	// Pair request
	else if (topic === 'mimir/registry/pair') {
	  console.log(`🔗 Pair code: ${data.pair_code} for ${data.device_id}`);
	}

	// Status/heartbeat updates
	else if (topic.includes('/status') || topic.includes('/heartbeat')) {
	  const deviceId = topic.split('/')[1];
	  const display = displays.get(deviceId);
	  if (display) {
		display.lastSeen = new Date();
		if (data.status) display.status = data.status;
	  }
	}

  } catch (error) {
	console.error('Error processing message:', error);
  }
});

console.log('🎯 Listening for Mimir displays...');

// Example: Send command to a display
function assignDisplay(deviceId, sceneId, subchannelId) {
  const command = {
	type: 'assign',
	scene_id: sceneId,
	subchannel_id: subchannelId,
	timestamp: new Date().toISOString()
  };

  client.publish(`mimir/${deviceId}/cmd`, JSON.stringify(command), { qos: 1 });
  console.log(`📤 Sent assignment to ${deviceId}`);
}

// Example: Show an image on a display
function showImage(deviceId, imageUrl) {
  const command = {
	type: 'display_image',
	image_url: imageUrl,
	timestamp: new Date().toISOString()
  };

  client.publish(`mimir/${deviceId}/cmd`, JSON.stringify(command), { qos: 1 });
  console.log(`📤 Sent image command to ${deviceId}: ${imageUrl}`);
}

// Example: List all discovered displays
function listDisplays() {
  console.log('\n📋 Discovered displays:');
  displays.forEach((display, deviceId) => {
	const ageSeconds = Math.floor((Date.now() - display.lastSeen) / 1000);
	console.log(`  ${deviceId}`);
	console.log(`    Name: ${display.name}`);
	console.log(`    Location: ${display.location}`);
	console.log(`    Pair code: ${display.pairCode}`);
	console.log(`    Status: ${display.status}`);
	console.log(`    Last seen: ${ageSeconds}s ago`);
  });
}

// List displays every 30 seconds
setInterval(listDisplays, 30000);
```

## Testing with Mosquitto CLI

### Subscribe to all discovery announcements

```bash
mosquitto_sub -h localhost -t "mimir/discovery/announce" -v
```

### Subscribe to everything

```bash
mosquitto_sub -h localhost -t "mimir/#" -v
```

### Send a command to a display

```bash
# Assign to a scene
mosquitto_pub -h localhost -t "mimir/display-hostname/cmd" -m '{
  "type": "assign",
  "scene_id": "main-scene",
  "subchannel_id": "channel-1"
}'

# Display an image
mosquitto_pub -h localhost -t "mimir/display-hostname/cmd" -m '{
  "type": "display_image",
  "image_url": "http://server/image.png"
}'
```

## Key MQTT Topics Reference

| Topic | Direction | Purpose | Frequency |
|-------|-----------|---------|-----------|
| `mimir/discovery/announce` | Display → Server | **Periodic discovery** | Every ~3 min |
| `mimir/registry/register` | Display → Server | Initial registration | Once on connect |
| `mimir/registry/pair` | Display → Server | Pair code broadcast | Once on connect |
| `mimir/{id}/status` | Display → Server | Status updates (retained) | On change + heartbeat |
| `mimir/{id}/heartbeat` | Display → Server | Keepalive | Every 30s |
| `mimir/{id}/cmd` | Server → Display | Commands | As needed |
| `mimir/{id}/evt` | Display → Server | Events (ack, error) | On event |

## Command Types

### assign
```json
{
  "type": "assign",
  "scene_id": "main-scene",
  "subchannel_id": "channel-1"
}
```

### display_image
```json
{
  "type": "display_image",
  "image_url": "http://server/content/image.png",
  "display": {
	"duration_seconds": 30
  }
}
```

### set_scene
```json
{
  "type": "set_scene",
  "scene_id": "scene-123",
  "scene_name": "Main Display"
}
```

### refresh
```json
{
  "type": "refresh"
}
```

## Display Discovery States

```
┌─────────────┐
│   Offline   │
└──────┬──────┘
	   │ Connect to MQTT
	   v
┌─────────────┐
│ Registering │ ← Publishes to mimir/discovery/announce
└──────┬──────┘   mimir/registry/register
	   │           mimir/registry/pair
	   │
	   v
┌─────────────┐
│  Unpaired   │ ← Shows pair code
└──────┬──────┘   Periodic announcements continue
	   │
	   │ Server sends assignment
	   v
┌─────────────┐
│   Paired    │ ← Assigned to scene/subchannel
└──────┬──────┘   Receives content commands
	   │
	   │ Periodic heartbeat + announcements
	   v
┌─────────────┐
│   Active    │ ← Displaying content
└─────────────┘
```

## Best Practices

1. **Always subscribe to `mimir/discovery/announce`** - This is the most reliable way to discover displays
2. **Handle late joins** - Displays send periodic announcements, so your server will discover them even if it starts later
3. **Monitor heartbeats** - If you don't receive heartbeat for >90 seconds, mark display as offline
4. **Use QoS 1 for commands** - Ensures command delivery
5. **Keep device_id mapping** - Store pair code → device_id mapping for user-friendly pairing
6. **Set up retained status** - Display status is retained, so you can check it anytime

## Deployment Checklist

- [ ] MQTT broker is running and accessible
- [ ] Server subscribes to `mimir/discovery/announce`
- [ ] Server can send commands to `mimir/{device_id}/cmd`
- [ ] Firewall allows MQTT traffic (port 1883)
- [ ] Displays have correct `MIMIR__MQTTBROKERHOST` or mDNS is working
- [ ] Test discovery: restart a display and verify server sees announcement
- [ ] Test commands: send test image to discovered display

## Troubleshooting

### Not seeing discovery announcements?

```bash
# 1. Check if displays are publishing
mosquitto_sub -h <broker> -t "mimir/discovery/announce" -v

# 2. Check if broker is receiving messages
mosquitto_sub -h <broker> -t "mimir/#" -v

# 3. Check display logs
# Windows: %APPDATA%\MimirDisplay\logs\mimir-*.log

# 4. Verify MQTT broker is reachable
telnet <broker> 1883
```

### Display connects but doesn't respond to commands?

1. Check device_id matches exactly (case-sensitive)
2. Verify command JSON is valid
3. Check QoS level (use QoS 1 for reliability)
4. Monitor display events: `mosquitto_sub -t "mimir/+/evt" -v`

### Need to reset a display?

```bash
# Clear retained status
mosquitto_pub -h <broker> -t "mimir/<device-id>/status" -n -r

# Send register command
mosquitto_pub -h <broker> -t "mimir/<device-id>/cmd" -m '{"type":"register"}'
```

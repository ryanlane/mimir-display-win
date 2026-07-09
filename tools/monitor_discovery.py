# Mimir Display Discovery Monitor
# This script listens for discovery announcements from Mimir displays
# Usage: python monitor_discovery.py [mqtt_broker_host]

import sys
import json
import paho.mqtt.client as mqtt
from datetime import datetime

discovered_displays = {}

def on_connect(client, userdata, flags, rc):
    if rc == 0:
        print("✅ Connected to MQTT broker")
        print("🎯 Listening for Mimir display announcements...\n")

        # Subscribe to discovery topics
        client.subscribe("mimir/discovery/announce")
        client.subscribe("mimir/registry/register")
        client.subscribe("mimir/registry/pair")
        client.subscribe("mimir/+/status")
        client.subscribe("mimir/+/heartbeat")

        print("📡 Subscribed to:")
        print("   - mimir/discovery/announce (periodic announcements)")
        print("   - mimir/registry/register (initial registration)")
        print("   - mimir/registry/pair (pair codes)")
        print("   - mimir/+/status (status updates)")
        print("   - mimir/+/heartbeat (keepalive)")
        print("\n" + "="*70 + "\n")
    else:
        print(f"❌ Failed to connect to MQTT broker: {rc}")
        sys.exit(1)

def on_message(client, userdata, msg):
    try:
        payload = json.loads(msg.payload.decode())
        timestamp = datetime.now().strftime("%H:%M:%S")

        if msg.topic == "mimir/discovery/announce":
            device_id = payload["device_id"]
            discovered_displays[device_id] = payload

            print(f"[{timestamp}] 📺 DISCOVERY ANNOUNCEMENT")
            print(f"  Device ID:   {device_id}")
            print(f"  Pair Code:   {payload['pair_code']}")
            print(f"  Status:      {payload['status']}")
            print(f"  Hostname:    {payload['metadata']['hostname']}")
            print(f"  Name:        {payload['metadata']['name']}")
            print(f"  Location:    {payload['metadata']['location']}")
            print(f"  Backend:     {payload['capabilities']['backend']}")
            print(f"  Resolution:  {payload['capabilities']['resolution']}")
            print(f"  Formats:     {', '.join(payload['capabilities']['supported_formats'])}")
            print(f"  Timestamp:   {payload['timestamp']}")
            print("")

        elif msg.topic == "mimir/registry/register":
            device_id = payload["device_id"]
            print(f"[{timestamp}] ✅ REGISTRATION")
            print(f"  Device ID:   {device_id}")
            print(f"  Version:     {payload['client_version']}")
            print(f"  Protocol:    {payload['protocol_version']}")
            print(f"  Reply To:    {payload['reply_to']}")
            print("")

        elif msg.topic == "mimir/registry/pair":
            device_id = payload["device_id"]
            print(f"[{timestamp}] 🔗 PAIR REQUEST")
            print(f"  Device ID:   {device_id}")
            print(f"  Pair Code:   {payload['pair_code']}")
            print(f"  Hostname:    {payload['metadata']['hostname']}")
            print("")

        elif "/status" in msg.topic:
            device_id = msg.topic.split("/")[1]
            print(f"[{timestamp}] 📊 STATUS UPDATE")
            print(f"  Device ID:   {device_id}")
            print(f"  Status:      {payload['status']}")
            if payload.get("pair_code"):
                print(f"  Pair Code:   {payload['pair_code']}")
            print("")

        elif "/heartbeat" in msg.topic:
            device_id = msg.topic.split("/")[1]
            print(f"[{timestamp}] 💓 HEARTBEAT - {device_id}")
            if payload.get("uptime_seconds"):
                uptime_min = payload["uptime_seconds"] // 60
                print(f"  Uptime:      {uptime_min} minutes")
            print("")

    except json.JSONDecodeError:
        print(f"⚠️  Non-JSON message on {msg.topic}")
    except Exception as e:
        print(f"❌ Error processing message: {e}")
        print(f"  Topic: {msg.topic}")
        print(f"  Payload: {msg.payload.decode()}")

def on_disconnect(client, userdata, rc):
    if rc != 0:
        print(f"\n⚠️  Unexpected disconnection. Reconnecting...")

def main():
    broker_host = sys.argv[1] if len(sys.argv) > 1 else "localhost"
    broker_port = 1883

    print("=" * 70)
    print("Mimir Display Discovery Monitor")
    print("=" * 70)
    print(f"Broker: {broker_host}:{broker_port}")
    print(f"Time:   {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 70)
    print()

    client = mqtt.Client(client_id="mimir-discovery-monitor")
    client.on_connect = on_connect
    client.on_message = on_message
    client.on_disconnect = on_disconnect

    try:
        client.connect(broker_host, broker_port, 60)
        client.loop_forever()
    except KeyboardInterrupt:
        print("\n\n" + "=" * 70)
        print("👋 Shutting down...")
        if discovered_displays:
            print(f"\n📋 Discovered {len(discovered_displays)} display(s):")
            for device_id, info in discovered_displays.items():
                print(f"  • {device_id}")
                print(f"    Pair code: {info['pair_code']}")
                print(f"    Hostname:  {info['metadata']['hostname']}")
        print("=" * 70)
        client.disconnect()
    except Exception as e:
        print(f"❌ Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()

# Animated Content Support

## Overview

The Windows display client now **explicitly reports** its animation capabilities to the Mimir server, ensuring the server knows it can receive and play animated WebP and GIF content.

## What Was Changed

### MqttService.cs - BuildCapabilities()

**Added explicit capability flags:**

```csharp
SupportedFormats = ["png", "jpeg", "jpg", "bmp", "gif", "webp"],
SupportsAnimation = true,  // This Windows display supports animated WebP and GIF
```

These fields are now sent in:
- **Registration requests** (`mimir/registry`)
- **Discovery announcements** (`mimir/discovery/announce`)
- **Presence/status updates** (`mimir/displays/{device_id}/status`)

## How It Works

### 1. Client-Side (Windows Display)

#### Capabilities Reporting
The display publishes its capabilities via MQTT:

```json
{
  "device_id": "display-xyz",
  "capabilities": {
	"backend": "windows",
	"resolution": [1920, 1080],
	"native_resolution": [1920, 1080],
	"orientation": "landscape",
	"rotation_deg": 0,
	"supported_formats": ["png", "jpeg", "jpg", "bmp", "gif", "webp"],
	"supports_animation": true,
	"simulation_mode": false
  }
}
```

#### Content Rendering
When the display receives a `display_image` MQTT command with a WebP URL:

1. **ContentService** downloads the `.webp` file to cache
2. **DisplayWindow** detects the `.webp` extension
3. **Magick.NET** decodes all frames from the WebP
4. **DispatcherTimer** animates the frames at the correct speed

### 2. Server-Side (Mimir Platform)

The server stores `supports_animation` in the `display_clients` table and uses it to:

- **Select appropriate content variants** (animated vs. static)
- **Optimize bandwidth** (don't send animated files to displays that can't render them)
- **Channel rendering decisions** (channels can check `display.supports_animation`)

## Verification

### Check MQTT Messages

Use the **MQTT Monitor** (Menu → MQTT Monitor) to verify capabilities are being sent:

1. Launch the display in windowed mode
2. Open **MQTT Monitor** from the menu
3. Filter for messages containing `"supports_animation"`
4. You should see it set to `true` in:
   - Registration messages
   - Discovery announcements
   - Status/presence updates

### Check Server Database

On the Mimir server, verify the display's capabilities are stored:

```sql
SELECT display_id, supports_animation, capabilities 
FROM display_clients 
WHERE display_id = 'your-display-id';
```

### Test Animated Content

1. Pair the display with the server
2. Assign animated WebP content from a channel
3. The display should play the animation smoothly
4. Check logs for: `"Animated WebP: {frameCount} frames"` message

## Supported Formats

The Windows display client supports:

| Format | Static | Animated | Notes |
|--------|--------|----------|-------|
| PNG    | ✅     | ❌       | Single frame only |
| JPEG   | ✅     | ❌       | Single frame only |
| BMP    | ✅     | ❌       | Single frame only |
| GIF    | ✅     | ✅       | Via XamlAnimatedGif library |
| WebP   | ✅     | ✅       | Via Magick.NET library |

## Related Files

- **MimirDisplay/Services/MqttService.cs** - Capability reporting
- **MimirDisplay/Models/MqttSchemas.cs** - `DisplayCapabilities` model
- **MimirDisplay/Services/ContentService.cs** - Content download/caching
- **MimirDisplay/Windows/DisplayWindow.xaml.cs** - Animated rendering
- **MimirDisplay/MimirDisplay.csproj** - Package references (Magick.NET, XamlAnimatedGif)

## Troubleshooting

### Animated content not showing

1. **Check capability reporting:**
   - Use MQTT Monitor to verify `supports_animation: true` is being sent

2. **Check server-side selection:**
   - Verify the channel is checking `display.supports_animation`
   - Verify the URL points to the animated `.webp` file (not a static derivative)

3. **Check client rendering:**
   - View logs for "Animated WebP" or "Displaying animated GIF" messages
   - Ensure Magick.NET is installed (check NuGet packages)

4. **Check content format:**
   - Verify the `.webp` file actually contains multiple frames
   - Test with a known-good animated WebP file

### Server not recognizing animation support

1. **Force re-registration:**
   - Use "Reset Pairing State" from the display menu
   - Re-pair the display with a new pair code

2. **Check server migration:**
   - Verify the `supports_animation` column exists in `display_clients` table
   - Run pending database migrations if needed

3. **Check MQTT connection:**
   - Verify presence messages are being received by the server
   - Check server logs for capability updates

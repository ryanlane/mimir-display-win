# Info Overlay Feature

## Overview

The Mimir Display client now includes an **Info Overlay** feature that displays detailed metadata about the currently displayed content.

## How to Use

### Enable/Disable the Overlay

1. **Via Menu (Windowed Mode Only):**
   - Open the application in windowed mode
   - Go to **View** → **Show Info Overlay**
   - The menu item is checkable, so you can toggle it on/off

2. **Visual Location:**
   - The overlay appears in the **top-right corner** of the display
   - It has a semi-transparent dark background with a green border
   - Stays on top of the content being displayed

### Information Displayed

The overlay shows:

#### Always Visible:
- **File Name** - The name of the currently displayed file
- **File Size** - Formatted as B, KB, MB, or GB
- **Format** - Image format (PNG, JPEG, GIF, WEBP, etc.)
- **Resolution** - Image dimensions in pixels (e.g., 1920x1080)
- **Last Updated** - Timestamp when the overlay was last refreshed (HH:mm:ss)

#### Conditional (Animated Content Only):
- **Animation** - Number of frames (e.g., "45 frames")
- **FPS** - Average frames per second calculated from frame delays

## Features

### Automatic Updates
- The overlay updates automatically whenever new content is displayed
- No need to manually refresh

### Smart Visibility
- Only shows when:
  - The overlay is enabled via the menu
  - Content is actively being displayed (not on splash screen)
- Hidden in fullscreen mode menu
- Toggles along with the content

### Format Support
- **Static Images:** PNG, JPEG, BMP
- **Animated GIF:** Shows frame count and average FPS
- **Animated WebP:** Shows frame count and average FPS
- **Static WebP:** Treated as a static image

## Technical Details

### FPS Calculation
- For animated content, FPS is calculated as: `1000ms / average_frame_delay_ms`
- Frame delays are extracted from the image metadata using ImageMagick
- Displays as a decimal with one decimal place (e.g., "12.5 FPS")

### File Size Formatting
- Automatically converts to the most readable unit:
  - Less than 1 KB: Shows in bytes (B)
  - Less than 1 MB: Shows in kilobytes (KB)
  - Less than 1 GB: Shows in megabytes (MB)
  - 1 GB or more: Shows in gigabytes (GB)

### Resolution Detection
- Uses ImageMagick for WebP and GIF files
- Uses WPF BitmapImage for other formats
- Shows actual image dimensions, not display dimensions

## Example Display

```
FILE INFO
File: animated_cat.webp
Size: 2.34 MB
Resolution: 800x600
Format: WEBP
Animation: 45 frames
FPS: 12.5
Updated: 14:23:45
```

## Notes

- The overlay is lightweight and does not impact playback performance
- Information is read once when the image is loaded, not continuously
- The overlay preference is not persisted between sessions (always starts disabled)
- In fullscreen mode, the menu is hidden, but the overlay can remain visible if it was already enabled

## Use Cases

- **Content Verification:** Confirm the correct file is being displayed
- **Debugging:** Check image format, size, and animation properties
- **Performance Monitoring:** View FPS for animated content
- **QA/Testing:** Validate content meets specifications

## Future Enhancements (Potential)

- Persist overlay state across sessions
- Add window resolution (current display size)
- Add network stats (if streaming)
- Add keyboard shortcut for quick toggle
- Add more animation details (loop count, total duration)

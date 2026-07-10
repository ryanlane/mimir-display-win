# Mimir Windows Display Client

A Windows-native fullscreen display client for the [Mimir](https://github.com/ryanlane/mimir) platform.
Built with **.NET 8 + WPF**, following the same MQTT-based protocol as the Python `mimir-display` client.

---

## Features

- **Fullscreen WPF window** — zero chrome, cursor hidden, black letterbox background
- **mDNS auto-discovery** — finds the Mimir server on the LAN via `_mimir._tcp.local.` (no manual config needed)
- **MQTT client** — connects, registers, receives `assign` / `display_image` / `set_scene` commands
- **Image rendering** — PNG, JPEG, BMP, WebP (requires Windows WebP codec), animated GIF
- **Splash screen** — shows pair code and status while waiting for content
- **Disk image cache** — SHA-256 keyed, conditional GET (ETag), automatic pruning
- **Persistent state** — restores last assignment, MQTT override, and scene across restarts
- **Health endpoint** — `GET http://localhost:8081/health` returns JSON status
- **Structured logging** — Serilog to console + rolling file (`%APPDATA%\MimirDisplay\logs\`)
- **DI + Generic Host** — `Microsoft.Extensions.Hosting`, clean service lifetimes
- **.env support** — drop a `.env` file next to the exe (see `.env.example`)

---

## Quick Start

### Build & Run

```powershell
# Prerequisites: .NET 8 SDK, Visual Studio 2022+
cd mimir-display-win
dotnet run --project MimirDisplay
```

### Publish (self-contained single file)

```powershell
dotnet publish MimirDisplay -c Release -r win-x64 --self-contained
# Output: MimirDisplay/bin/Release/net8.0-windows/win-x64/publish/MimirDisplay.exe
```

---

## Configuration

Configuration is loaded from three sources in priority order (highest first):

1. **Environment variables** — prefix `MIMIR__` (double underscore), e.g. `MIMIR__MQTTBROKERHOST`
2. **`.env` file** — placed next to the exe (same syntax as environment variables)
3. **`appsettings.json`** — defaults shipped with the app

### Key Settings

| Key | Default | Description |
|-----|---------|-------------|
| `MIMIR__PLATFORMURL` | _(empty)_ | Mimir server URL. Leave blank for mDNS auto-discovery. |
| `MIMIR__MQTTBROKERHOST` | _(empty)_ | MQTT broker hostname. Leave blank to auto-discover. |
| `MIMIR__MQTTBROKERPORT` | `1883` | MQTT broker port. |
| `MIMIR__MQTTUSERNAME` | _(empty)_ | MQTT username (if required). |
| `MIMIR__MQTTPASSWORD` | _(empty)_ | MQTT password (if required). |
| `MIMIR__DISPLAYID` | _(hostname)_ | Override display ID. Defaults to the machine hostname slug. |
| `MIMIR__DISPLAYNAME` | `Mimir Windows Display` | Friendly display name shown in the server UI. |
| `MIMIR__DISPLAYLOCATION` | `Unknown` | Physical location label. |
| `MIMIR__DISPLAYORIENTATION` | `landscape` | `landscape` / `portrait_left` / `portrait_right` |
| `MIMIR__HDMISCALEMODE` | `fit` | `fit` (letterbox) or `fill` (stretch/crop) |
| `MIMIR__STARTUPLOGOPATH` | _(built-in)_ | Path to a custom startup logo (PNG/JPEG). |
| `MIMIR__WEBHOOKPORT` | `8081` | Port for the `/health` HTTP endpoint. |
| `MIMIR__LOGLEVEL` | `Information` | `Debug` / `Information` / `Warning` / `Error` |
| `MIMIR__CACHEDIRECTORY` | `%APPDATA%\MimirDisplay\cache` | Override image cache directory. |
| `MIMIR__STATEDIRECTORY` | `%APPDATA%\MimirDisplay\state` | Override persistent state directory. |

Copy `.env.example` → `.env` and fill in your values.

---

## MQTT Protocol

This client implements the same Mimir MQTT protocol as `mimir-display`:

### Topics

| Topic | Direction | Description |
|-------|-----------|-------------|
| `mimir/{id}/status` | Publish (retained) | Presence payload (capabilities, scene, pair code) |
| `mimir/{id}/heartbeat` | Publish | Periodic heartbeat |
| `mimir/{id}/evt` | Publish | Events: `ack`, `rendered`, `error` |
| `mimir/{id}/cmd` | Subscribe | Commands from the server |
| `mimir/{id}/pair/ack` | Subscribe | Pair confirmation |
| `mimir/registry/register` | Publish | Registration request |
| `mimir/registry/pair` | Publish | Pair code announcement |

### Commands handled

| Command | Action |
|---------|--------|
| `assign` | Download content URL, render image, publish `ack` + `rendered` |
| `display_image` | Direct image URL, same pipeline as `assign` |
| `set_scene` | Store `scene_id` / `subchannel_id`, publish `ack` |
| `clear_scene` | Clear scene state, publish `ack` |
| `refresh` | Acknowledge; waits for server to push new `display_image` |

---

## Architecture

```
App.xaml.cs          — IHost setup, DI, WPF window lifecycle
├── DisplayOrchestrator  (BackgroundService)
│   ├── DiscoveryService — mDNS _mimir._tcp.local. scanner
│   ├── MqttService      — MQTT connect/register/commands/heartbeat
│   ├── ContentService   — HTTP download + disk cache
│   ├── StateService     — JSON state on disk
│   └── HealthService    — HTTP /health endpoint
└── DisplayWindow (WPF) — fullscreen image renderer + splash overlay
```

---

## Adding the App Icon / Logo

Place your assets in `MimirDisplay/Resources/`:

- `mimir.ico` — application icon (set in `.csproj`)
- `mimir_logo.png` — splash screen logo (512×512 recommended)

Mark them as **Resource** in the project (Build Action: `Resource`).

---

## Logging

Logs are written to:
- **Console** — always enabled
- **Rolling file** — `%APPDATA%\MimirDisplay\logs\mimir-YYYYMMDD.log` (7-day retention)

---

## License

MIT

---

## Documentation

Additional docs are in the [`docs/`](docs/) folder:

| File | Description |
|------|-------------|
| [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common issues and fixes |
| [PAIRING-GUIDE.md](docs/PAIRING-GUIDE.md) | How display pairing works |
| [DISCOVERY.md](docs/DISCOVERY.md) | mDNS / server auto-discovery |
| [SERVER-DISCOVERY-GUIDE.md](docs/SERVER-DISCOVERY-GUIDE.md) | Server-side discovery details |
| [MQTT-SPEC-COMPLIANCE.md](docs/MQTT-SPEC-COMPLIANCE.md) | MQTT protocol notes |
| [ANIMATION-SUPPORT.md](docs/ANIMATION-SUPPORT.md) | Animated GIF / WebP support |
| [INFO-OVERLAY-FEATURE.md](docs/INFO-OVERLAY-FEATURE.md) | In-window info overlay |
| [SINGLE-FILE-DEPLOYMENT.md](docs/SINGLE-FILE-DEPLOYMENT.md) | Single-file publish guide |
| [ICON-AND-INSTALLER-GUIDE.md](docs/ICON-AND-INSTALLER-GUIDE.md) | App icon and Inno Setup installer |

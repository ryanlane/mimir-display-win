namespace MimirDisplay.Mqtt;

/// <summary>
/// Generates all MQTT topic strings for a given device ID.
/// Mirrors the Python MqttTopicManager.
/// </summary>
public sealed class TopicManager
{
    public string DeviceId { get; private set; }

    private string _base;

    public TopicManager(string deviceId)
    {
        DeviceId = deviceId;
        _base = $"mimir/{deviceId}";
    }

    public void UpdateDeviceId(string newId)
    {
        DeviceId = newId;
        _base = $"mimir/{newId}";
    }

    // ── Outbound ──────────────────────────────────────────────────────────────

    /// <summary>Retained presence / status topic.</summary>
    public string Status => $"{_base}/status";

    /// <summary>Heartbeat pings.</summary>
    public string Heartbeat => $"{_base}/heartbeat";

    /// <summary>Outbound events (ack, rendered, error).</summary>
    public string Events => $"{_base}/evt";

    // ── Inbound ───────────────────────────────────────────────────────────────

    /// <summary>Inbound command topic (assign, display_image, etc.).</summary>
    public string Commands => $"{_base}/cmd";

    /// <summary>Pair ACK from server.</summary>
    public string PairAck => $"{_base}/pair/ack";

    /// <summary>Registration reply from server.</summary>
    public string RegistrationReply => $"{_base}/reg/reply";

    // ── Fleet / shared ────────────────────────────────────────────────────────

    /// <summary>Fleet-wide registration inbox.</summary>
    public static string Registry => "mimir/registry/register";

    /// <summary>Fleet-wide pair request inbox.</summary>
    public static string PairRequest => "mimir/registry/pair";

    /// <summary>Fleet-wide discovery/announcement topic for displays to broadcast their presence.</summary>
    public static string DiscoveryAnnounce => "mimir/discovery/announce";

    /// <summary>Fleet-wide OTA desired version topic.</summary>
    public static string FleetDesiredVersion => "mimir/fleet/desired_version";

    // ── QoS constants ─────────────────────────────────────────────────────────

    public const int StatusQos = 1;
    public const int EventsQos = 0;
    public const int CommandsQos = 1;
}

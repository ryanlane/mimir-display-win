using System.Text.Json.Serialization;

namespace MimirDisplay.Models;

// ── MQTT command schemas ──────────────────────────────────────────────────────

public enum CommandType
{
    Assign,
    DisplayImage,
    Refresh,
    Register,
    Ready,
    RegistrationComplete,
    SetScene,
    ClearScene,
    Unknown
}

public static class CommandTypeNames
{
    public const string Assign = "assign";
    public const string DisplayImage = "display_image";
    public const string Refresh = "refresh";
    public const string Register = "register";
    public const string Ready = "ready";
    public const string RegistrationComplete = "registration_complete";
    public const string SetScene = "set_scene";
    public const string ClearScene = "clear_scene";

    public static CommandType Parse(string? type) => type switch
    {
        Assign => CommandType.Assign,
        DisplayImage => CommandType.DisplayImage,
        Refresh => CommandType.Refresh,
        Register => CommandType.Register,
        Ready => CommandType.Ready,
        RegistrationComplete => CommandType.RegistrationComplete,
        SetScene => CommandType.SetScene,
        ClearScene => CommandType.ClearScene,
        _ => CommandType.Unknown
    };
}

public sealed class MqttCommand
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("assignment_id")]
    public string? AssignmentId { get; set; }

    [JsonPropertyName("sequence")]
    public int? Sequence { get; set; }

    [JsonPropertyName("scene_id")]
    public string? SceneId { get; set; }

    [JsonPropertyName("scene_name")]
    public string? SceneName { get; set; }

    [JsonPropertyName("subchannel_id")]
    public string? SubchannelId { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("display")]
    public Dictionary<string, object>? Display { get; set; }

    [JsonPropertyName("content")]
    public ContentSpec? Content { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("update_type")]
    public string? UpdateType { get; set; }

    [JsonPropertyName("refresh_interval_s")]
    public int? RefreshIntervalSeconds { get; set; }

    public string? GetImageUrl() => ImageUrl ?? Url;

    public string? GetDeliveryUrl() =>
        Content?.Delivery?.Url
        ?? ImageUrl
        ?? Url;
}

public sealed class ContentSpec
{
    [JsonPropertyName("delivery")]
    public DeliverySpec? Delivery { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

public sealed class DeliverySpec
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("ttl_seconds")]
    public int? TtlSeconds { get; set; }
}

// ── MQTT event (outbound) schemas ─────────────────────────────────────────────

public sealed class AckEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ack";

    [JsonPropertyName("assignment_id")]
    public string? AssignmentId { get; set; }

    [JsonPropertyName("sequence")]
    public int? Sequence { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("scene_id")]
    public string? SceneId { get; set; }

    [JsonPropertyName("subchannel_id")]
    public string? SubchannelId { get; set; }
}

public sealed class RenderedEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "rendered";

    [JsonPropertyName("assignment_id")]
    public string? AssignmentId { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
}

public sealed class ErrorEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "error";

    [JsonPropertyName("assignment_id")]
    public string? AssignmentId { get; set; }

    [JsonPropertyName("error_type")]
    public string? ErrorType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
}

// ── Registration schemas ──────────────────────────────────────────────────────

public sealed class RegistrationRequest
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public DisplayCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("metadata")]
    public DisplayMetadata Metadata { get; set; } = new();

    [JsonPropertyName("client_version")]
    public string ClientVersion { get; set; } = "1.0.0";

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "1";

    [JsonPropertyName("reply_to")]
    public string ReplyTo { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
}

public sealed class RegistrationResponse
{
    [JsonPropertyName("assigned_id")]
    public string? AssignedId { get; set; }

    [JsonPropertyName("config")]
    public Dictionary<string, object>? Config { get; set; }
}

public sealed class DisplayCapabilities
{
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "windows";

    [JsonPropertyName("resolution")]
    public int[] Resolution { get; set; } = [1920, 1080];

    [JsonPropertyName("native_resolution")]
    public int[] NativeResolution { get; set; } = [1920, 1080];

    [JsonPropertyName("orientation")]
    public string Orientation { get; set; } = "landscape";

    [JsonPropertyName("rotation_deg")]
    public int RotationDeg { get; set; } = 0;

    [JsonPropertyName("supported_formats")]
    public string[] SupportedFormats { get; set; } = ["png", "jpeg", "jpg", "bmp", "gif", "webp"];

    [JsonPropertyName("supports_animation")]
    public bool SupportsAnimation { get; set; } = true;

    [JsonPropertyName("simulation_mode")]
    public bool SimulationMode { get; set; } = false;
}

public sealed class DisplayMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Mimir Windows Display";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "Unknown";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = System.Net.Dns.GetHostName();

    [JsonPropertyName("client_version")]
    public string ClientVersion { get; set; } = "1.0.0";

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "1";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

// ── Presence / status payload ─────────────────────────────────────────────────

public sealed class PresencePayload
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "online";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [JsonPropertyName("capabilities")]
    public DisplayCapabilities? Capabilities { get; set; }

    [JsonPropertyName("metadata")]
    public DisplayMetadata? Metadata { get; set; }

    [JsonPropertyName("assigned_scene_id")]
    public string? AssignedSceneId { get; set; }

    [JsonPropertyName("assigned_subchannel_id")]
    public string? AssignedSubchannelId { get; set; }

    [JsonPropertyName("last_displayed")]
    public string? LastDisplayed { get; set; }

    [JsonPropertyName("pair_code")]
    public string? PairCode { get; set; }
}

// ── Pair request ──────────────────────────────────────────────────────────────

public sealed class PairRequest
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("pair_code")]
    public string PairCode { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public DisplayMetadata? Metadata { get; set; }

    [JsonPropertyName("capabilities")]
    public DisplayCapabilities? Capabilities { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
}

// ── Discovery announcement ──────────────────────────────────────────────────────────

/// <summary>
/// Periodic announcement broadcast for server-side discovery.
/// Published to mimir/discovery/announce so servers can find displays.
/// </summary>
public sealed class DiscoveryAnnouncement
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("pair_code")]
    public string PairCode { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "online";

    [JsonPropertyName("capabilities")]
    public DisplayCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("metadata")]
    public DisplayMetadata Metadata { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "1";
}

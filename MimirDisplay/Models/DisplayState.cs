using System.Text.Json;
using System.Text.Json.Serialization;

namespace MimirDisplay.Models;

/// <summary>
/// Persistent display state written to disk as JSON.
/// Survives process restarts so the display can resume the last assignment.
/// </summary>
public sealed class DisplayState
{
    [JsonPropertyName("assigned_scene_id")]
    public string? AssignedSceneId { get; set; }

    [JsonPropertyName("assigned_subchannel_id")]
    public string? AssignedSubchannelId { get; set; }

    [JsonPropertyName("last_assignment_id")]
    public string? LastAssignmentId { get; set; }

    [JsonPropertyName("last_displayed")]
    public string? LastDisplayed { get; set; }

    [JsonPropertyName("last_content_path")]
    public string? LastContentPath { get; set; }

    [JsonPropertyName("platform_url_override")]
    public string? PlatformUrlOverride { get; set; }

    [JsonPropertyName("mqtt_override")]
    public MqttOverride? MqttOverride { get; set; }

    [JsonPropertyName("registered")]
    public bool Registered { get; set; }

    [JsonPropertyName("assigned_id")]
    public string? AssignedId { get; set; }

    [JsonPropertyName("server_assigned_display_id")]
    public string? ServerAssignedDisplayId { get; set; }

    [JsonPropertyName("registration_key")]
    public string? RegistrationKey { get; set; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class MqttOverride
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("port")]
    public int? Port { get; set; }
}

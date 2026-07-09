using System;

namespace MimirDisplay.Models;

/// <summary>
/// Event data for MQTT message monitoring.
/// Published when any MQTT message is sent or received.
/// </summary>
public sealed class MqttMessageEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public MqttMessageDirection Direction { get; init; }
    public string Topic { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public int PayloadSize { get; init; }
    public string? MessageType { get; init; } // For commands: type field
}

public enum MqttMessageDirection
{
    Sent,
    Received
}

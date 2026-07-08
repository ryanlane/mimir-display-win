namespace MimirDisplay.Configuration;

/// <summary>
/// Strongly-typed configuration for the Mimir display client.
/// Bound from the "Mimir" section of appsettings.json and environment variables
/// (prefix MIMIR__).
/// </summary>
public sealed class DisplayConfig
{
    public const string SectionName = "Mimir";

    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Override display ID. When empty, the machine hostname slug is used.
    /// </summary>
    public string DisplayId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Mimir Windows Display";
    public string DisplayLocation { get; set; } = "Unknown";

    /// <summary>Comma-separated tags, e.g. "canary,office".</summary>
    public string DisplayTags { get; set; } = string.Empty;

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Base URL of the Mimir server, e.g. "http://mimir.local:5000".
    /// Leave empty to enable mDNS auto-discovery.
    /// </summary>
    public string PlatformUrl { get; set; } = string.Empty;

    public string MqttBrokerHost { get; set; } = string.Empty;
    public int MqttBrokerPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = string.Empty;
    public string MqttPassword { get; set; } = string.Empty;

    /// <summary>Heartbeat publish interval in seconds.</summary>
    public int MqttHeartbeatInterval { get; set; } = 30;

    // ── Display ───────────────────────────────────────────────────────────────

    /// <summary>landscape | portrait_left | portrait_right</summary>
    public string DisplayOrientation { get; set; } = "landscape";

    /// <summary>fit | fill</summary>
    public string HdmiScaleMode { get; set; } = "fit";

    /// <summary>Letterbox background colour as #RRGGBB.</summary>
    public string HdmiBackgroundColor { get; set; } = "#000000";

    /// <summary>Path to startup logo image. Empty = built-in default.</summary>
    public string StartupLogoPath { get; set; } = string.Empty;

    public string DefaultContentPath { get; set; } = string.Empty;

    // ── Storage ───────────────────────────────────────────────────────────────

    /// <summary>Override cache directory. Defaults to %APPDATA%\MimirDisplay\cache.</summary>
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>Override state directory. Defaults to %APPDATA%\MimirDisplay\state.</summary>
    public string StateDirectory { get; set; } = string.Empty;

    // ── Operational ───────────────────────────────────────────────────────────

    public bool WebhookEnabled { get; set; } = true;
    public int WebhookPort { get; set; } = 8081;
    public string LogLevel { get; set; } = "Information";

    // ── Derived helpers ───────────────────────────────────────────────────────

    public List<string> GetTags() =>
        DisplayTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .ToList();

    /// <summary>
    /// Returns the effective display ID: configured value or the hostname slug.
    /// </summary>
    public string GetEffectiveDisplayId()
    {
        if (!string.IsNullOrWhiteSpace(DisplayId))
            return Slugify(DisplayId);

        return Slugify(System.Net.Dns.GetHostName());
    }

    public static string Slugify(string input)
    {
        var s = input.Trim().ToLowerInvariant()
                     .Replace(' ', '-')
                     .Replace('_', '-');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\-]", string.Empty);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"-+", "-");
        return string.IsNullOrEmpty(s) ? "display" : s;
    }

    public string GetCacheDirectory() =>
        string.IsNullOrWhiteSpace(CacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MimirDisplay", "cache")
            : CacheDirectory;

    public string GetStateDirectory() =>
        string.IsNullOrWhiteSpace(StateDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MimirDisplay", "state")
            : StateDirectory;
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;

namespace MimirDisplay.Services;

/// <summary>
/// Main hosted service that drives the full display lifecycle:
/// 1. Show splash screen
/// 2. Discover Mimir server (mDNS if PlatformUrl is blank)
/// 3. Fetch MQTT broker config from the server API
/// 4. Connect to MQTT and run the receive loop indefinitely
/// </summary>
public sealed class DisplayOrchestrator : BackgroundService
{
    private readonly DisplayConfig _config;
    private readonly StateService _state;
    private readonly MqttService _mqtt;
    private readonly DiscoveryService _discovery;
    private readonly HealthService _health;
    private readonly ILogger<DisplayOrchestrator> _logger;

    // Set by WPF App after the window is created
    private Func<string, Task>? _onShowImage;
    private Action<string>? _onStatusText;
    private Action<string>? _onPairCode;

    public DisplayOrchestrator(
        IOptions<DisplayConfig> config,
        StateService state,
        MqttService mqtt,
        DiscoveryService discovery,
        HealthService health,
        ILogger<DisplayOrchestrator> logger)
    {
        _config = config.Value;
        _state = state;
        _mqtt = mqtt;
        _discovery = discovery;
        _health = health;
        _logger = logger;
    }

    public void SetCallbacks(
        Func<string, Task> onShowImage,
        Action<string> onStatusText,
        Action<string> onPairCode)
    {
        _onShowImage = onShowImage;
        _onStatusText = onStatusText;
        _onPairCode = onPairCode;

        // Wire display callback into MQTT service
        _mqtt.SetDisplayCallback(async (path, cfg) =>
        {
            if (_onShowImage != null)
                await _onShowImage(path);
        });

        _mqtt.SetStatusTextCallback(text => _onStatusText?.Invoke(text));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DisplayOrchestrator starting");

        // Start health endpoint
        try { _health.Start(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Health service failed to start"); }

        _health.SetStatus("starting");
        UpdateStatus("Mimir Display starting…");
        NotifyPairCode(_mqtt.PairCode);

        // Restore any persisted MQTT override from state
        var savedState = _state.Current;
        var mqttHost = _config.MqttBrokerHost;
        var mqttPort = _config.MqttBrokerPort;
        var platformUrl = _config.PlatformUrl;

        if (savedState.MqttOverride?.Host != null)
        {
            mqttHost = savedState.MqttOverride.Host;
            mqttPort = savedState.MqttOverride.Port ?? mqttPort;
            _logger.LogInformation("Using persisted MQTT override {Host}:{Port}", mqttHost, mqttPort);
        }
        if (!string.IsNullOrWhiteSpace(savedState.PlatformUrlOverride))
        {
            platformUrl = savedState.PlatformUrlOverride;
        }

        // If no broker configured, run mDNS discovery first
        if (string.IsNullOrWhiteSpace(mqttHost))
        {
            if (!string.IsNullOrWhiteSpace(platformUrl))
            {
                // Try fetching broker config from a known platform URL
                var resolved = await FetchBrokerConfigAsync(platformUrl, stoppingToken);
                if (resolved.HasValue)
                {
                    (mqttHost, mqttPort) = resolved.Value;
                }
            }
            else
            {
                // Full mDNS discovery path
                UpdateStatus("Searching for Mimir server…");

                await _discovery.RunDiscoveryLoopAsync(async (url) =>
                {
                    _logger.LogInformation("Discovered server: {Url}", url);
                    UpdateStatus($"Found Mimir server at {url}");

                    var resolved = await FetchBrokerConfigAsync(url, stoppingToken);
                    if (resolved.HasValue)
                    {
                        (mqttHost, mqttPort) = resolved.Value;
                        _state.Update(s => s.PlatformUrlOverride = url);
                    }
                    else
                    {
                        // Best-effort: assume default broker port on same host
                        var uri = new Uri(url);
                        mqttHost = uri.Host;
                    }
                }, stoppingToken);
            }
        }

        if (string.IsNullOrWhiteSpace(mqttHost))
        {
            _logger.LogError("No MQTT broker could be discovered or configured. Retrying on next restart.");
            UpdateStatus("Could not find Mimir server. Check network or set MIMIR__MQTTBROKERHOST.");
            _health.SetStatus("error", error: "no_broker");
            // Don't crash — let the host keep running so the window stays open
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            return;
        }

        _health.SetStatus("connecting");

        // Run MQTT (reconnects internally on failures)
        _logger.LogInformation("Starting MQTT loop against {Host}:{Port}", mqttHost, mqttPort);
        try
        {
            await _mqtt.RunAsync(mqttHost, mqttPort, stoppingToken);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT service terminated with error");
            _health.SetStatus("error", error: ex.Message);
        }
    }

    /// <summary>
    /// GETs /api/displays/mqtt/config from the platform and returns (host, port).
    /// </summary>
    private async Task<(string host, int port)?> FetchBrokerConfigAsync(
        string platformUrl,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var endpoint = platformUrl.TrimEnd('/') + "/api/displays/mqtt/config";
            var resp = await http.GetAsync(endpoint, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? host = null;
            int port = 1883;

            if (root.TryGetProperty("host", out var h)) host = h.GetString();
            if (root.TryGetProperty("mqtt_host", out var mh)) host = mh.GetString();
            if (root.TryGetProperty("port", out var p)) port = p.GetInt32();
            if (root.TryGetProperty("mqtt_port", out var mp)) port = mp.GetInt32();

            if (!string.IsNullOrWhiteSpace(host))
            {
                _logger.LogInformation("Fetched MQTT broker config: {Host}:{Port}", host, port);
                return (host!, port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch broker config from {Url}", platformUrl);
        }
        return null;
    }

    private void UpdateStatus(string text)
    {
        _logger.LogInformation(text);
        _onStatusText?.Invoke(text);
        _health.SetStatus(text.Contains("error", StringComparison.OrdinalIgnoreCase) ? "error" : "running");
    }

    private void NotifyPairCode(string code) => _onPairCode?.Invoke(code);
}

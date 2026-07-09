using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;
using MimirDisplay.Models;
using MimirDisplay.Mqtt;

namespace MimirDisplay.Services;

/// <summary>
/// Core MQTT service: connects to the broker, handles registration, subscribes to
/// command topics, dispatches commands, and publishes presence/heartbeat/events.
/// </summary>
public sealed class MqttService : IAsyncDisposable
{
    private readonly DisplayConfig _config;
    private readonly StateService _state;
    private readonly ContentService _content;
    private readonly ILogger<MqttService> _logger;

    private IMqttClient? _client;
    private TopicManager? _topics;
    private CancellationTokenSource? _heartbeatCts;

    // Injected by the orchestrator after construction
    private Func<string, Dictionary<string, object>, Task>? _onDisplayImage;
    private Action<string>? _onStatusText;

    // Current window resolution (updated dynamically)
    private int _currentWidth = 1920;
    private int _currentHeight = 1080;

    public string? DeviceId => _topics?.DeviceId;
    public bool IsConnected => _client?.IsConnected ?? false;

    public string? CurrentSceneId { get; private set; }
    public string? CurrentSubchannelId { get; private set; }

    // Pair code shown on the splash screen
    public string PairCode { get; } = GeneratePairCode();

    // MQTT message monitoring event
    public event EventHandler<MqttMessageEvent>? MessageReceived;

    public MqttService(
        IOptions<DisplayConfig> config,
        StateService state,
        ContentService content,
        ILogger<MqttService> logger)
    {
        _config = config.Value;
        _state = state;
        _content = content;
        _logger = logger;
    }

    public void SetDisplayCallback(Func<string, Dictionary<string, object>, Task> callback)
        => _onDisplayImage = callback;

    public void SetStatusTextCallback(Action<string> callback)
        => _onStatusText = callback;

    private CancellationTokenSource? _resolutionPublishCts;

    public void UpdateResolution(int width, int height)
    {
        if (width == _currentWidth && height == _currentHeight) return;
        _currentWidth = width;
        _currentHeight = height;
        _logger.LogDebug("Resolution updated to {Width}x{Height}", width, height);

        // Republish the retained status (which carries capabilities.resolution)
        // once the size settles — the server syncs it to the display record and
        // re-renders content at the new size. Debounced: SizeChanged fires
        // continuously during a window drag.
        _resolutionPublishCts?.Cancel();
        var cts = _resolutionPublishCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, cts.Token);
                await PublishPresenceAsync(ct: cts.Token);
                _logger.LogInformation("Published resolution change: {Width}x{Height}", width, height);
            }
            catch (OperationCanceledException) { /* superseded by a newer resize */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish resolution change");
            }
        });
    }

    /// <summary>
    /// Connects to the MQTT broker and starts the receive loop.
    /// Returns when the connection is terminated or <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task RunAsync(string brokerHost, int brokerPort, CancellationToken ct)
    {
        var deviceId = _config.GetEffectiveDisplayId();
        _topics = new TopicManager(deviceId);

        _logger.LogInformation("Connecting to MQTT broker {Host}:{Port} as {DeviceId}",
            brokerHost, brokerPort, deviceId);

        UpdateStatus("Connecting to MQTT broker…");

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId($"mimir-win-{deviceId}-{Guid.NewGuid():N}")
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

        if (!string.IsNullOrWhiteSpace(_config.MqttUsername))
            optionsBuilder = optionsBuilder.WithCredentials(_config.MqttUsername, _config.MqttPassword);

        // Will message: mark offline on unexpected disconnect
        optionsBuilder = optionsBuilder.WithWillTopic(_topics.Status)
            .WithWillPayload(BuildPresenceJson("offline"))
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        var options = optionsBuilder.Build();

        // Reconnect loop
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _client.ConnectAsync(options, ct);

                // Subscribe to command and pair/ack topics
                await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(_topics.Commands, MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopicFilter(_topics.PairAck, MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopicFilter(_topics.RegistrationReply, MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(), ct);

                _logger.LogInformation("MQTT connected and subscribed");

                await RegisterAsync(ct);
                await PublishPresenceAsync(ct: ct);
                StartHeartbeat(ct);

                // Block until disconnected
                await WaitForDisconnectAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT connection failed; retrying in 5s");
                UpdateStatus("MQTT disconnected — reconnecting…");
                try { await Task.Delay(5_000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        StopHeartbeat();
        await PublishPresenceAsync(status: "offline", ct: CancellationToken.None);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    private async Task RegisterAsync(CancellationToken ct)
    {
        if (_topics == null || _client == null) return;

        var caps = BuildCapabilities();
        var meta = BuildMetadata();

        var request = new RegistrationRequest
        {
            DeviceId = _topics.DeviceId,
            Capabilities = caps,
            Metadata = meta,
            ReplyTo = _topics.RegistrationReply,
        };

        _logger.LogInformation("Sending registration request for {DeviceId}", _topics.DeviceId);
        await PublishJsonAsync(TopicManager.Registry, request, MqttQualityOfServiceLevel.AtLeastOnce, ct: ct);

        // Also send pair code so the server UI can show it immediately
        await PublishPairCodeAsync(ct);

        // Publish initial discovery announcement
        await PublishDiscoveryAnnouncementAsync(ct);
    }

    private async Task PublishPairCodeAsync(CancellationToken ct)
    {
        if (_topics == null || _client == null) return;

        var payload = new PairRequest
        {
            DeviceId = _topics.DeviceId,
            PairCode = PairCode,
            ReplyTo = _topics.PairAck,
            Metadata = BuildMetadata(),
            Capabilities = BuildCapabilities(),
        };

        await PublishJsonAsync(TopicManager.PairRequest, payload, MqttQualityOfServiceLevel.AtLeastOnce, ct: ct);
        _logger.LogInformation("Pair code published: {PairCode}", PairCode);
    }

    /// <summary>
    /// Publishes a discovery announcement to help servers find this display.
    /// Published to a fleet-wide topic that servers can subscribe to.
    /// </summary>
    private async Task PublishDiscoveryAnnouncementAsync(CancellationToken ct)
    {
        if (_topics == null || _client == null) return;

        var announcement = new DiscoveryAnnouncement
        {
            DeviceId = _topics.DeviceId,
            PairCode = PairCode,
            Status = "online",
            Capabilities = BuildCapabilities(),
            Metadata = BuildMetadata(),
        };

        await PublishJsonAsync(TopicManager.DiscoveryAnnounce, announcement, 
            MqttQualityOfServiceLevel.AtLeastOnce, retain: false, ct: ct);
        _logger.LogDebug("Discovery announcement published for {DeviceId}", _topics.DeviceId);
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    private void StartHeartbeat(CancellationToken parentCt)
    {
        StopHeartbeat();
        _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        var ct = _heartbeatCts.Token;
        var interval = TimeSpan.FromSeconds(_config.MqttHeartbeatInterval);

        _ = Task.Run(async () =>
        {
            int announcementCounter = 0;
            const int announcementEvery = 6; // Announce every 6th heartbeat (e.g., every 3 minutes if heartbeat is 30s)

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct);
                    if (_client?.IsConnected == true)
                    {
                        await PublishPresenceAsync(ct: ct);
                        await PublishHeartbeatAsync(ct);

                        // Periodic discovery announcements to help servers find displays
                        announcementCounter++;
                        if (announcementCounter >= announcementEvery)
                        {
                            await PublishDiscoveryAnnouncementAsync(ct);
                            announcementCounter = 0;
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Heartbeat error");
                }
            }
        }, ct);
    }

    private void StopHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.ConvertPayloadToString();

        _logger.LogDebug("MQTT message received on {Topic}", topic);

        // Raise monitoring event
        RaiseMessageEvent(MqttMessageDirection.Received, topic, payload);

        if (_topics == null) return;

        if (topic == _topics.RegistrationReply)
        {
            await HandleRegistrationReplyAsync(payload);
            return;
        }

        if (topic == _topics.PairAck)
        {
            _logger.LogInformation("Pair ACK received");
            UpdateStatus("Paired — waiting for content…");
            return;
        }

        if (topic == _topics.Commands)
        {
            // Try to extract command type for monitoring
            string? cmdType = null;
            try
            {
                var cmd = JsonSerializer.Deserialize<MqttCommand>(payload);
                cmdType = cmd?.Type;
            }
            catch { /* ignore */ }

            RaiseMessageEvent(MqttMessageDirection.Received, topic, payload, cmdType);
            await HandleCommandAsync(payload);
        }
    }

    private Task HandleRegistrationReplyAsync(string payload)
    {
        try
        {
            var response = JsonSerializer.Deserialize<RegistrationResponse>(payload);
            if (response?.AssignedId != null)
            {
                _logger.LogInformation("Registration complete: assigned_id={Id}", response.AssignedId);
                _topics!.UpdateDeviceId(response.AssignedId);
                _state.Update(s =>
                {
                    s.Registered = true;
                    s.AssignedId = response.AssignedId;
                });
                UpdateStatus("Registered — waiting for assignment…");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse registration reply");
        }
        return Task.CompletedTask;
    }

    private async Task HandleCommandAsync(string payload)
    {
        MqttCommand? cmd;
        try
        {
            cmd = JsonSerializer.Deserialize<MqttCommand>(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialise command");
            return;
        }

        if (cmd == null) return;

        var type = CommandTypeNames.Parse(cmd.Type);
        _logger.LogInformation("Received command type={Type} assignment_id={Id}", cmd.Type, cmd.AssignmentId);

        switch (type)
        {
            case CommandType.Assign:
                await HandleAssignAsync(cmd);
                break;
            case CommandType.DisplayImage:
                await HandleDisplayImageAsync(cmd);
                break;
            case CommandType.SetScene:
                await HandleSetSceneAsync(cmd);
                break;
            case CommandType.ClearScene:
                await HandleClearSceneAsync(cmd);
                break;
            case CommandType.Refresh:
                await HandleRefreshAsync(cmd);
                break;
            case CommandType.FinalizeRegistration:
                await HandleFinalizeRegistrationAsync(cmd);
                break;
            case CommandType.Ready:
            case CommandType.Register:
            case CommandType.RegistrationComplete:
                _logger.LogDebug("Ignoring lifecycle command {Type}", cmd.Type);
                break;
            default:
                _logger.LogDebug("Unknown command type: {Type}", cmd.Type);
                break;
        }
    }

    // ── Command handlers ──────────────────────────────────────────────────────

    private async Task HandleAssignAsync(MqttCommand cmd)
    {
        await PublishAckAsync(cmd.AssignmentId, cmd.Sequence, ok: true, message: "Processing…");

        var url = cmd.GetDeliveryUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            await PublishErrorAsync(cmd.AssignmentId, "missing_url", "No delivery URL in assign command");
            return;
        }

        await DownloadAndDisplayAsync(url, cmd.AssignmentId, cmd.SceneId, cmd.SubchannelId);
    }

    private async Task HandleDisplayImageAsync(MqttCommand cmd)
    {
        var url = cmd.GetImageUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            await PublishErrorAsync(cmd.AssignmentId, "missing_url", "No image URL in display_image command");
            return;
        }

        await PublishAckAsync(cmd.AssignmentId, null, ok: true, message: $"Displaying: {TruncateUrl(url)}");
        await DownloadAndDisplayAsync(url, cmd.AssignmentId, null, null);
    }

    private Task HandleSetSceneAsync(MqttCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.SceneId))
        {
            return PublishErrorAsync(cmd.AssignmentId, "missing_scene_id", "set_scene requires scene_id");
        }
        CurrentSceneId = cmd.SceneId;
        CurrentSubchannelId = cmd.SubchannelId;
        _logger.LogInformation("Scene set: {SceneId} / {SubchannelId}", CurrentSceneId, CurrentSubchannelId);
        return PublishAckAsync(cmd.AssignmentId, null, ok: true,
            message: $"scene_id={cmd.SceneId}", sceneId: cmd.SceneId, subchannelId: cmd.SubchannelId);
    }

    private Task HandleClearSceneAsync(MqttCommand cmd)
    {
        CurrentSceneId = null;
        CurrentSubchannelId = null;
        return PublishAckAsync(cmd.AssignmentId, null, ok: true, message: "scene cleared");
    }

    private Task HandleRefreshAsync(MqttCommand cmd)
    {
        _logger.LogInformation("Refresh request acknowledged (scene={Scene})", CurrentSceneId ?? "none");
        return PublishAckAsync(cmd.AssignmentId, null, ok: true, message: "Refresh acknowledged");
    }

    private async Task HandleFinalizeRegistrationAsync(MqttCommand cmd)
    {
        _logger.LogInformation("Received finalize_registration: display_id={DisplayId}, has_key={HasKey}, has_config={HasConfig}",
            cmd.DisplayId ?? "(none)", 
            !string.IsNullOrWhiteSpace(cmd.RegistrationKey),
            cmd.Config != null);

        if (string.IsNullOrWhiteSpace(cmd.DisplayId))
        {
            _logger.LogWarning("finalize_registration missing display_id");
            return;
        }

        // Store the server-assigned display_id and registration_key
        _state.Update(state =>
        {
            state.ServerAssignedDisplayId = cmd.DisplayId;
            state.RegistrationKey = cmd.RegistrationKey;
            state.Registered = true;
        });

        _logger.LogInformation("Registration finalized: display_id={DisplayId} stored", cmd.DisplayId);

        // Update status
        _onStatusText?.Invoke($"Registered as {cmd.DisplayId}");

        // Acknowledge
        await PublishAckAsync(cmd.AssignmentId, null, ok: true, message: "Registration finalized");
    }

    // ── Download + display pipeline ───────────────────────────────────────────

    private async Task DownloadAndDisplayAsync(
        string url,
        string? assignmentId,
        string? sceneId,
        string? subchannelId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var localPath = await _content.FetchAsync(url);

            var displayConfig = new Dictionary<string, object>();
            if (sceneId != null) displayConfig["scene_id"] = sceneId;
            if (subchannelId != null) displayConfig["subchannel_id"] = subchannelId;
            if (assignmentId != null) displayConfig["assignment_id"] = assignmentId;

            if (_onDisplayImage != null)
                await _onDisplayImage(localPath, displayConfig);

            // Persist assignment state
            if (sceneId != null) CurrentSceneId = sceneId;
            if (subchannelId != null) CurrentSubchannelId = subchannelId;

            _state.Update(s =>
            {
                if (sceneId != null) s.AssignedSceneId = sceneId;
                if (subchannelId != null) s.AssignedSubchannelId = subchannelId;
                if (assignmentId != null) s.LastAssignmentId = assignmentId;
                s.LastContentPath = localPath;
                s.LastDisplayed = DateTimeOffset.UtcNow.ToString("o");
            });

            sw.Stop();
            await PublishRenderedAsync(assignmentId, (int)sw.ElapsedMilliseconds);
            await PublishPresenceAsync(ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed to display content from {Url}", url);
            await PublishErrorAsync(assignmentId, "display_error", ex.Message);
        }
    }

    // ── Publish helpers ───────────────────────────────────────────────────────

    public async Task PublishPresenceAsync(string status = "online", CancellationToken ct = default)
    {
        if (_client?.IsConnected != true || _topics == null) return;
        var json = BuildPresenceJson(status);
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_topics.Status)
            .WithPayload(json)
            .WithRetainFlag(true)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        RaiseMessageEvent(MqttMessageDirection.Sent, _topics.Status, json, "presence");
    }

    private async Task PublishHeartbeatAsync(CancellationToken ct)
    {
        if (_client?.IsConnected != true || _topics == null) return;
        var payload = JsonSerializer.Serialize(new
        {
            device_id = _topics.DeviceId,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
        });
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_topics.Heartbeat)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build(), ct);

        RaiseMessageEvent(MqttMessageDirection.Sent, _topics.Heartbeat, payload, "heartbeat");
    }

    private async Task PublishAckAsync(
        string? assignmentId,
        int? sequence,
        bool ok,
        string? message = null,
        string? sceneId = null,
        string? subchannelId = null)
    {
        var evt = new AckEvent
        {
            AssignmentId = assignmentId,
            Sequence = sequence,
            Ok = ok,
            Message = message,
            SceneId = sceneId,
            SubchannelId = subchannelId,
        };
        await PublishEventAsync(evt);
    }

    private async Task PublishRenderedAsync(string? assignmentId, int durationMs)
    {
        var evt = new RenderedEvent
        {
            AssignmentId = assignmentId,
            DurationMs = durationMs,
        };
        await PublishEventAsync(evt);
    }

    private async Task PublishErrorAsync(string? assignmentId, string errorType, string message)
    {
        var evt = new ErrorEvent
        {
            AssignmentId = assignmentId,
            ErrorType = errorType,
            Message = message,
        };
        await PublishEventAsync(evt);
    }

    private async Task PublishEventAsync<T>(T payload)
    {
        if (_client?.IsConnected != true || _topics == null) return;
        await PublishJsonAsync(_topics.Events, payload,
            MqttQualityOfServiceLevel.AtMostOnce);
    }

    private async Task PublishJsonAsync<T>(
        string topic,
        T payload,
        MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce,
        bool retain = false,
        CancellationToken ct = default)
    {
        if (_client == null) return;
        var json = JsonSerializer.Serialize(payload);
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(retain)
            .Build(), ct);
    }

    // ── Connected / disconnected callbacks ────────────────────────────────────

    private Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("MQTT connected");
        UpdateStatus("Connected to MQTT — registering…");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("MQTT disconnected: {Reason}", e.ReasonString ?? e.Exception?.Message);
        StopHeartbeat();
        return Task.CompletedTask;
    }

    // ── Presence JSON ─────────────────────────────────────────────────────────

    private string BuildPresenceJson(string status = "online")
    {
        var savedState = _state.Current;
        var presence = new PresencePayload
        {
            DeviceId = _topics?.DeviceId ?? _config.GetEffectiveDisplayId(),
            Status = status,
            Capabilities = BuildCapabilities(),
            Metadata = BuildMetadata(),
            AssignedSceneId = savedState.AssignedSceneId,
            AssignedSubchannelId = savedState.AssignedSubchannelId,
            LastDisplayed = savedState.LastDisplayed,
            PairCode = PairCode,
        };
        return JsonSerializer.Serialize(presence);
    }

    private DisplayCapabilities BuildCapabilities()
    {
        // Use current window resolution if available, otherwise fall back to screen resolution
        var (w, h) = _currentWidth > 0 && _currentHeight > 0 
            ? (_currentWidth, _currentHeight) 
            : GetScreenResolution();

        return new DisplayCapabilities
        {
            Backend = "windows",
            Resolution = [w, h],
            NativeResolution = [w, h],
            Orientation = _config.DisplayOrientation,
            RotationDeg = _config.DisplayOrientation switch
            {
                "portrait_left" => 90,
                "portrait_right" => 270,
                _ => 0,
            },
            SupportedFormats = ["png", "jpeg", "jpg", "bmp", "gif", "webp"],
            SupportsAnimation = true,  // This Windows display supports animated WebP and GIF
        };
    }

    private DisplayMetadata BuildMetadata() => new()
    {
        Name = _config.DisplayName,
        Location = _config.DisplayLocation,
        Hostname = System.Net.Dns.GetHostName(),
        Tags = _config.GetTags(),
    };

    // ── Utilities ─────────────────────────────────────────────────────────────

    private void UpdateStatus(string text)
    {
        _logger.LogInformation("Status: {Text}", text);
        _onStatusText?.Invoke(text);
    }

    private static (int w, int h) GetScreenResolution()
    {
        // Must be called on STA thread in production; returns safe fallback here.
        try
        {
            var screen = System.Windows.SystemParameters.PrimaryScreenWidth;
            var h = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
            return ((int)screen, h);
        }
        catch
        {
            return (1920, 1080);
        }
    }

    private static string TruncateUrl(string url, int max = 64)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var name = Path.GetFileName(path);
            var s = string.IsNullOrEmpty(name) ? url : name;
            return s.Length > max ? s[..(max - 1)] + "…" : s;
        }
        catch { return url.Length > max ? url[..(max - 1)] + "…" : url; }
    }

    private static string GeneratePairCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[6];
        rng.GetBytes(bytes);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private void RaiseMessageEvent(MqttMessageDirection direction, string topic, string payload, string? messageType = null)
    {
        try
        {
            MessageReceived?.Invoke(this, new MqttMessageEvent
            {
                Direction = direction,
                Topic = topic,
                Payload = payload,
                PayloadSize = Encoding.UTF8.GetByteCount(payload),
                MessageType = messageType
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to raise MQTT message event");
        }
    }

    // ── Extension helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Blocks until the MQTT client disconnects or <paramref name="ct"/> is cancelled.
    /// Uses a TaskCompletionSource wired to the DisconnectedAsync event.
    /// </summary>
    private async Task WaitForDisconnectAsync(CancellationToken ct)
    {
        if (_client == null) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task Handler(MqttClientDisconnectedEventArgs _)
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        }

        _client.DisconnectedAsync += Handler;
        try
        {
            await tcs.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        finally
        {
            _client.DisconnectedAsync -= Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopHeartbeat();
        if (_client != null)
        {
            try { await _client.DisconnectAsync(); } catch { /* ignore */ }
            _client.Dispose();
        }
    }
}

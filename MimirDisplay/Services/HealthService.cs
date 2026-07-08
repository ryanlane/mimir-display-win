using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;

namespace MimirDisplay.Services;

/// <summary>
/// Lightweight HTTP health endpoint that serves JSON on GET /health.
/// Runs on its own background thread so MQTT work is never blocked.
/// </summary>
public sealed class HealthService : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ILogger<HealthService> _logger;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _task;

    // Mutable state updated by the orchestrator
    private string _status = "starting";
    private string? _backend = "windows";
    private int[]? _resolution;
    private string? _lastError;

    public HealthService(IOptions<DisplayConfig> config, ILogger<HealthService> logger)
    {
        _port = config.Value.WebhookPort;
        _logger = logger;
    }

    public void SetStatus(string status, int[]? resolution = null, string? error = null)
    {
        _status = status;
        _resolution = resolution;
        _lastError = error;
    }

    public void Start()
    {
        try
        {
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();
        }
        catch
        {
            // Fall back to localhost if the wildcard binding requires elevation
            _listener.Close();
            var l2 = new HttpListener();
            l2.Prefixes.Add($"http://localhost:{_port}/");
            l2.Start();
            // Replace — we can't reassign the field so restart from l2
            _logger.LogInformation("Health server listening on http://localhost:{Port}/health", _port);
            _cts = new CancellationTokenSource();
            _task = Task.Run(() => ServeLoop(l2, _cts.Token));
            return;
        }

        _logger.LogInformation("Health server listening on http://+:{Port}/health", _port);
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => ServeLoop(_listener, _cts.Token));
    }

    private async Task ServeLoop(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health listener error");
                break;
            }

            _ = Task.Run(() => HandleRequest(ctx), CancellationToken.None);
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path.TrimEnd('/') == "/health" && ctx.Request.HttpMethod == "GET")
            {
                var payload = JsonSerializer.Serialize(new
                {
                    status = _status,
                    backend = _backend,
                    resolution = _resolution,
                    error = _lastError,
                    timestamp = DateTimeOffset.UtcNow.ToString("o"),
                });
                var body = Encoding.UTF8.GetBytes(payload);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health request handling error");
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        _cts?.Dispose();
    }
}

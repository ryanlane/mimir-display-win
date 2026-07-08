using Microsoft.Extensions.Logging;
using Zeroconf;

namespace MimirDisplay.Services;

/// <summary>
/// Discovers the Mimir server on the local network via mDNS (_mimir._tcp.local.)
/// and advertises this display as a _mimir-display._tcp.local. service.
/// </summary>
public sealed class DiscoveryService
{
    private const string ServerServiceType = "_mimir._tcp.local.";
    private const string DisplayServiceType = "_mimir-display._tcp.local.";

    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(ILogger<DiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans for a Mimir server via mDNS.
    /// Returns the server base URL (e.g. "http://192.168.1.50:5000") or null if none found.
    /// </summary>
    public async Task<string?> DiscoverServerAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        _logger.LogInformation("Scanning for Mimir server via mDNS ({ServiceType})…", ServerServiceType);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var responses = await ZeroconfResolver.ResolveAsync(
                ServerServiceType,
                scanTime: timeout,
                cancellationToken: timeoutCts.Token);

            foreach (var host in responses)
            {
                foreach (var svc in host.Services.Values)
                {
                    var ip = host.IPAddress;
                    var port = svc.Port;
                    var url = $"http://{ip}:{port}";
                    _logger.LogInformation("Found Mimir server at {Url} (host={Host})", url, host.DisplayName);
                    return url;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("mDNS scan timed out — no server found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mDNS discovery failed");
        }

        return null;
    }

    /// <summary>
    /// Continuously polls for a Mimir server, calling <paramref name="onFound"/> when discovered.
    /// Retries with increasing delay until cancelled.
    /// </summary>
    public async Task RunDiscoveryLoopAsync(
        Func<string, Task> onFound,
        CancellationToken ct)
    {
        var delays = new[] { 3, 5, 10, 15, 30, 60 };
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var url = await DiscoverServerAsync(TimeSpan.FromSeconds(5), ct);
            if (url != null)
            {
                await onFound(url);
                return;
            }

            var delay = delays[Math.Min(attempt, delays.Length - 1)];
            _logger.LogDebug("No server found; retrying in {Delay}s (attempt {Attempt})", delay, attempt + 1);
            attempt++;

            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}

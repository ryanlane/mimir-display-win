using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;

namespace MimirDisplay.Services;

/// <summary>
/// Downloads remote images to a local disk cache and resolves .local hostnames.
/// Uses a content-hash as the cache key (ETag or URL-based) so stale entries
/// are evicted only when content changes.
/// </summary>
public sealed class ContentService
{
    private readonly string _cacheDir;
    private readonly HttpClient _http;
    private readonly ILogger<ContentService> _logger;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public ContentService(
        IHttpClientFactory httpFactory,
        IOptions<DisplayConfig> config,
        ILogger<ContentService> logger)
    {
        _logger = logger;
        _cacheDir = config.Value.GetCacheDirectory();
        Directory.CreateDirectory(_cacheDir);
        _http = httpFactory.CreateClient(nameof(ContentService));
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to the cache directory and returns the local path.
    /// If the file is already cached and the ETag matches, returns the cached copy.
    /// </summary>
    public async Task<string> FetchAsync(string url, string? etag = null, CancellationToken ct = default)
    {
        var cacheKey = ComputeCacheKey(url);
        var metaPath = Path.Combine(_cacheDir, cacheKey + ".meta");
        var dataPath = GetCachedDataPath(cacheKey, metaPath);

        // Conditional GET — if we have a local copy and an ETag, try 304
        if (dataPath != null && etag != null)
        {
            _logger.LogDebug("Cache hit (etag match) for {Url}", url);
            return dataPath;
        }

        _logger.LogDebug("Downloading {Url}", url);

        int attempts = 3;
        double backoff = 0.75;
        Exception? last = null;

        for (int i = 1; i <= attempts; i++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (dataPath != null && File.Exists(metaPath))
                {
                    var savedEtag = await File.ReadAllTextAsync(metaPath, ct);
                    if (!string.IsNullOrEmpty(savedEtag))
                        req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{savedEtag}\"", true));
                }

                using var resp = await _http.SendAsync(req, ct);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotModified && dataPath != null)
                {
                    _logger.LogDebug("304 Not Modified for {Url}", url);
                    return dataPath;
                }

                resp.EnsureSuccessStatusCode();

                // Content-Type is preferred, but fall back to the URL's own
                // extension — servers on Python < 3.13 served .webp as
                // text/plain, which routed animated WebP to the static
                // renderer (frame 0 only). DisplayWindow picks its decoder
                // from this file extension.
                var ext = GetExtensionFromContentType(resp.Content.Headers.ContentType?.MediaType)
                          ?? GetExtensionFromUrl(url)
                          ?? ".img";
                var finalPath = Path.Combine(_cacheDir, cacheKey + ext);

                await using var fs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs, ct);

                // Persist ETag for next request
                var newEtag = resp.Headers.ETag?.Tag?.Trim('"');
                await File.WriteAllTextAsync(metaPath, newEtag ?? string.Empty, ct);

                // Remove any stale copies with a different extension
                foreach (var old in Directory.GetFiles(_cacheDir, cacheKey + ".*")
                                             .Where(f => !f.EndsWith(".meta") && f != finalPath))
                    File.Delete(old);

                _logger.LogInformation("Downloaded {Url} -> {Path}", url, finalPath);
                return finalPath;
            }
            catch (Exception ex) when (i < attempts)
            {
                last = ex;
                _logger.LogWarning("Download attempt {Attempt}/{Total} failed for {Url}: {Error}", i, attempts, url, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(backoff * (1 + Random.Shared.NextDouble() * 0.3)), ct);
                backoff *= 1.6;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Download failed with no exception captured");
    }

    /// <summary>Cleans up cache files older than <paramref name="maxAge"/>.</summary>
    public void PruneCache(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.GetFiles(_cacheDir))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Cache prune skipped {File}: {Error}", file, ex.Message);
            }
        }
    }

    private string? GetCachedDataPath(string cacheKey, string metaPath)
    {
        foreach (var ext in ImageExtensions)
        {
            var path = Path.Combine(_cacheDir, cacheKey + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string ComputeCacheKey(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string? GetExtensionFromContentType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => null
    };

    /// <summary>Extension from the URL path, if it's a known image type.</summary>
    private static string? GetExtensionFromUrl(string url)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            return ImageExtensions.Contains(ext) ? ext : null;
        }
        catch
        {
            return null;
        }
    }
}

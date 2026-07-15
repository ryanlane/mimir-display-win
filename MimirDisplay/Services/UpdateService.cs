using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MimirDisplay.Services;

/// <summary>
/// Information about an available update retrieved from GitHub Releases.
/// </summary>
public sealed class UpdateInfo
{
    public Version LatestVersion { get; init; } = new();
    public string TagName { get; init; } = string.Empty;
    public string ReleasePageUrl { get; init; } = string.Empty;
    /// <summary>Direct download URL for the .exe asset, or null if not present.</summary>
    public string? AssetDownloadUrl { get; init; }
    public string? AssetName { get; init; }
}

/// <summary>
/// Checks GitHub Releases for new versions and can perform an in-place update
/// by downloading the new executable and handing off via a PowerShell script.
/// </summary>
public sealed class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/ryanlane/mimir-display-win/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/ryanlane/mimir-display-win/releases";

    private readonly HttpClient _http;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(IHttpClientFactory httpFactory, ILogger<UpdateService> logger)
    {
        _http = httpFactory.CreateClient(nameof(UpdateService));
        _logger = logger;
    }

    /// <summary>
    /// Queries GitHub for the latest release. Returns null when already up to date
    /// or when the check cannot be completed (network error, etc.).
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var current = GetCurrentVersion();
            _logger.LogInformation("Checking for updates. Current version: {Version}", current);

            var release = await _http.GetFromJsonAsync<GitHubRelease>(ApiUrl);
            if (release is null)
                return null;

            var tagName = release.TagName?.TrimStart('v') ?? string.Empty;
            if (!Version.TryParse(tagName, out var latest))
            {
                _logger.LogWarning("Could not parse release tag '{Tag}' as a version", release.TagName);
                return null;
            }

            _logger.LogInformation("Latest release: {Tag} ({Version})", release.TagName, latest);

            if (latest <= current)
            {
                _logger.LogInformation("Already up to date");
                return null;
            }

            // Find the first .exe asset
            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

            return new UpdateInfo
            {
                LatestVersion = latest,
                TagName = release.TagName ?? tagName,
                ReleasePageUrl = release.HtmlUrl ?? ReleasesPageUrl,
                AssetDownloadUrl = asset?.BrowserDownloadUrl,
                AssetName = asset?.Name,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Downloads the new executable and launches a PowerShell handoff script that
    /// waits for the current process to exit, replaces the exe, and re-launches.
    /// Returns false if the download or launch fails.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        if (string.IsNullOrEmpty(update.AssetDownloadUrl))
            return false;

        try
        {
            var updateDir = Path.Combine(Path.GetTempPath(), "MimirDisplay-update");
            Directory.CreateDirectory(updateDir);

            var assetName = update.AssetName ?? "MimirDisplay.exe";
            var downloadPath = Path.Combine(updateDir, assetName);

            _logger.LogInformation("Downloading {Asset} from {Url}", assetName, update.AssetDownloadUrl);

            // Stream download with progress reporting
            using var response = await _http.GetAsync(update.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var src = await response.Content.ReadAsStreamAsync();
            using var dst = File.Create(downloadPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report((int)(downloaded * 100 / total));
            }

            _logger.LogInformation("Download complete: {Path}", downloadPath);

            var currentExe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule!.FileName;
            var currentPid = Environment.ProcessId;

            // Write a small PowerShell script to the temp dir
            var scriptPath = Path.Combine(updateDir, "update.ps1");
            var oldEsc = currentExe.Replace("'", "''");
            var newEsc = downloadPath.Replace("'", "''");
            var script =
                "param()\n" +
                $"$pid_to_wait = {currentPid}\n" +
                $"$old = '{oldEsc}'\n" +
                $"$new = '{newEsc}'\n" +
                "while (Get-Process -Id $pid_to_wait -ErrorAction SilentlyContinue) {\n" +
                "    Start-Sleep -Milliseconds 500\n" +
                "}\n" +
                "Start-Sleep -Seconds 1\n" +
                "try {\n" +
                "    Copy-Item -Force $new $old\n" +
                "    Start-Process $old\n" +
                "} catch {\n" +
                "    Start-Process $new\n" +
                "}\n";

            await File.WriteAllTextAsync(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download or install failed");
            return false;
        }
    }

    /// <summary>Opens the GitHub releases page in the default browser.</summary>
    public static void OpenReleasePage(UpdateInfo update)
    {
        Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
    }

    public static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    // ── JSON models ───────────────────────────────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

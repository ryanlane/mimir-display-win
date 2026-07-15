using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;
using MimirDisplay.Services;
using XamlAnimatedGif;
using ImageMagick;

namespace MimirDisplay.Windows;

/// <summary>
/// Fullscreen display window.
/// Handles static images and animated GIFs. All UI updates are dispatched to
/// the WPF Dispatcher so this class can be called from any thread.
/// </summary>
public partial class DisplayWindow : Window
{
    // Win32: returns the inner client-area rectangle (no title bar, no frame)
    // in physical pixels. This is the same geometry Electron's getContentSize() reports.
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private readonly DisplayConfig _config;
    private readonly ILogger<DisplayWindow> _logger;
    private bool _isFullscreen = false;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;

    // Service references (set by orchestrator)
    private MqttService? _mqttService;
    private StateService? _stateService;
    private MqttMonitorWindow? _mqttMonitorWindow;

    // Resolution tracking
    private Action<int, int>? _onResolutionChanged;
    private int _lastReportedWidth = 0;
    private int _lastReportedHeight = 0;

    // Animated WebP playback
    private DispatcherTimer? _webpAnimationTimer;
    private List<BitmapSource>? _webpFrames;
    private List<int>? _webpDelays;
    private int _currentFrameIndex = 0;

    // Info overlay state (technical/file details — filename, size, resolution, format)
    private bool _isInfoOverlayEnabled = false;
    private string? _currentFilePath;
    private long _currentFileSize;
    private int _currentImageWidth;
    private int _currentImageHeight;
    private int _currentFrameCount;

    // Artwork overlay state (content details — title/artist/etc, when a channel
    // supplies them). On by default: unlike the debug File Info Overlay, this
    // is part of the content itself, not a diagnostic tool.
    private bool _isArtworkOverlayEnabled = true;
    private Dictionary<string, string>? _currentArtworkMetadata;
    private double _currentFps;

    private readonly UpdateService _updateService;

    public DisplayWindow(IOptions<DisplayConfig> config, ILogger<DisplayWindow> logger, UpdateService updateService)
    {
        _config = config.Value;
        _logger = logger;
        _updateService = updateService;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Allow Escape to close in non-kiosk/debug scenarios
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Application.Current.Shutdown();
            else if (e.Key == System.Windows.Input.Key.F11)
                ToggleFullscreen();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DeviceIdText.Text = _config.GetEffectiveDisplayId();
        LoadLogo();
        ApplyWindowMode();
        _logger.LogInformation("Window loaded. Press F11 to toggle fullscreen.");

        // Background update check — fires 10 s after startup so it doesn't
        // slow down initial connection or interrupt the splash screen.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var update = await _updateService.CheckForUpdateAsync();
            if (update is not null)
            {
                Dispatcher.Invoke(() => ShowUpdateNotification(update, silent: true));
            }
        });
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            // Exit fullscreen
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
            ResizeMode = _previousResizeMode;
            Topmost = false;
            Cursor = System.Windows.Input.Cursors.Arrow;
            AdminMenu.Visibility = Visibility.Visible;
            _isFullscreen = false;
            _logger.LogInformation("Exited fullscreen mode");
        }
        else
        {
            // Enter fullscreen
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Cursor = System.Windows.Input.Cursors.None;
            AdminMenu.Visibility = Visibility.Collapsed;
            _isFullscreen = true;
            _logger.LogInformation("Entered fullscreen mode");
        }
    }

    private void ApplyWindowMode()
    {
        // Start in fullscreen if explicitly requested, otherwise windowed
        bool startFullscreen = Environment.GetEnvironmentVariable("MIMIR__FULLSCREEN") == "true";

        if (startFullscreen)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            Cursor = System.Windows.Input.Cursors.None;
            AdminMenu.Visibility = Visibility.Collapsed;
            _isFullscreen = true;
            _logger.LogInformation("Starting in fullscreen mode (MIMIR__FULLSCREEN=true)");
        }
        else
        {
            // Already configured in XAML, just log
            AdminMenu.Visibility = Visibility.Visible;
            _logger.LogInformation("Starting in windowed mode. Press F11 to toggle fullscreen.");
        }
    }

    // ── Public API (called from orchestrator on any thread) ───────────────────

    public void SetPairCode(string code)
    {
        Dispatcher.Invoke(() =>
        {
            PairCodeText.Text = code;
        });
    }

    public void SetStatusText(string text)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;
        });
    }

    /// <summary>
    /// Shows an image file fullscreen.
    /// Handles PNG, JPEG, BMP, GIF (animated), and WebP (animated and static).
    /// Returns when the image has been loaded and displayed.
    /// </summary>
    /// <param name="config">Optional per-assignment config (scene_id, subchannel_id,
    /// assignment_id, metadata) forwarded from the MQTT command. The "metadata"
    /// entry, when present, is a Dictionary&lt;string, string&gt; of content
    /// details (title/artist/etc — see MqttCommand.MetadataStrings()) drawn as
    /// an on-screen overlay.</param>
    public Task ShowImageAsync(string filePath, Dictionary<string, object>? config = null)
    {
        var tcs = new TaskCompletionSource();

        Dispatcher.Invoke(() =>
        {
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".gif")
                {
                    ShowAnimatedGif(filePath);
                }
                else if (ext == ".webp")
                {
                    ShowWebP(filePath);
                }
                else
                {
                    ShowStaticImage(filePath);
                }

                HideSplash();

                // Update info overlay with new file details
                UpdateInfoOverlay(filePath);

                // Update artwork overlay (title/artist/etc, when the channel supplied it)
                Dictionary<string, string>? metadata = null;
                if (config != null && config.TryGetValue("metadata", out var metaObj) && metaObj is Dictionary<string, string> metaDict)
                {
                    metadata = metaDict;
                }
                UpdateArtworkOverlay(metadata);

                tcs.SetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to display image: {Path}", filePath);
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public void ShowSplash()
    {
        Dispatcher.Invoke(() =>
        {
            SplashOverlay.Visibility = Visibility.Visible;
            ContentImage.Visibility = Visibility.Collapsed;
            AnimatedImage.Visibility = Visibility.Collapsed;
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ShowStaticImage(string filePath)
    {
        // Stop any running animation
        AnimationBehavior.SetSourceUri(AnimatedImage, null!);
        AnimatedImage.Visibility = Visibility.Collapsed;
        StopWebPAnimation();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        // Apply orientation rotation
        var rotation = _config.DisplayOrientation switch
        {
            "portrait_left" => Rotation.Rotate90,
            "portrait_right" => Rotation.Rotate270,
            _ => Rotation.Rotate0,
        };
        bitmap.Rotation = rotation;
        bitmap.EndInit();
        bitmap.Freeze();

        ContentImage.Source = bitmap;
        ContentImage.Stretch = _config.HdmiScaleMode == "fill"
            ? System.Windows.Media.Stretch.UniformToFill
            : System.Windows.Media.Stretch.Uniform;

        ContentImage.Visibility = Visibility.Visible;
    }

    private void ShowAnimatedGif(string filePath)
    {
        ContentImage.Visibility = Visibility.Collapsed;
        StopWebPAnimation(); // Stop any WebP animation

        AnimationBehavior.SetSourceUri(AnimatedImage, new Uri(filePath, UriKind.Absolute));
        AnimationBehavior.SetRepeatBehavior(AnimatedImage, System.Windows.Media.Animation.RepeatBehavior.Forever);
        AnimatedImage.Stretch = _config.HdmiScaleMode == "fill"
            ? System.Windows.Media.Stretch.UniformToFill
            : System.Windows.Media.Stretch.Uniform;
        AnimatedImage.Visibility = Visibility.Visible;
    }

    private void ShowWebP(string filePath)
    {
        // Stop any running animation
        AnimationBehavior.SetSourceUri(AnimatedImage, null!);
        AnimatedImage.Visibility = Visibility.Collapsed;
        StopWebPAnimation();

        try
        {
            using var magickImageCollection = new MagickImageCollection(filePath);

            if (magickImageCollection.Count > 1)
            {
                // Animated WebP frames are stored as deltas (only the changed
                // region, composited over the previous frame). Coalesce()
                // flattens every frame to a full canvas — without it, playback
                // shows only the changing parts.
                magickImageCollection.Coalesce();

                _logger.LogInformation("Loading animated WebP with {FrameCount} frames", magickImageCollection.Count);
                ShowAnimatedWebP(magickImageCollection);
            }
            else
            {
                // Static WebP - use regular image loading
                _logger.LogDebug("Loading static WebP");
                ShowStaticImage(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load WebP, trying fallback");
            // Fallback to static image loader (might work with Windows WebP codec)
            ShowStaticImage(filePath);
        }
    }

    private void ShowAnimatedWebP(MagickImageCollection frames)
    {
        _webpFrames = new List<BitmapSource>();
        _webpDelays = new List<int>();

        foreach (var frame in frames)
        {
            // Convert MagickImage to WPF BitmapSource
            using var memoryStream = new MemoryStream();
            frame.Write(memoryStream, MagickFormat.Png);
            memoryStream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze(); // Make it thread-safe

            _webpFrames.Add(bitmap);

            // Get frame delay in milliseconds (WebP uses centiseconds)
            var delay = (int)(frame.AnimationDelay * 10); // Convert from centiseconds to milliseconds
            _webpDelays.Add(delay > 0 ? delay : 100); // Minimum 100ms per frame
        }

        _currentFrameIndex = 0;
        ContentImage.Source = _webpFrames[0];
        ContentImage.Stretch = _config.HdmiScaleMode == "fill"
            ? System.Windows.Media.Stretch.UniformToFill
            : System.Windows.Media.Stretch.Uniform;
        ContentImage.Visibility = Visibility.Visible;

        // Start animation timer
        _webpAnimationTimer = new DispatcherTimer(DispatcherPriority.Render);
        _webpAnimationTimer.Interval = TimeSpan.FromMilliseconds(_webpDelays[0]);
        _webpAnimationTimer.Tick += WebPAnimationTimer_Tick;
        _webpAnimationTimer.Start();

        _logger.LogInformation("Started animated WebP playback with {FrameCount} frames", _webpFrames.Count);
    }

    private void WebPAnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_webpFrames == null || _webpDelays == null || _webpFrames.Count == 0)
        {
            StopWebPAnimation();
            return;
        }

        _currentFrameIndex = (_currentFrameIndex + 1) % _webpFrames.Count;
        ContentImage.Source = _webpFrames[_currentFrameIndex];

        // Update timer interval for next frame
        if (_webpAnimationTimer != null)
        {
            _webpAnimationTimer.Interval = TimeSpan.FromMilliseconds(_webpDelays[_currentFrameIndex]);
        }
    }

    private void StopWebPAnimation()
    {
        if (_webpAnimationTimer != null)
        {
            _webpAnimationTimer.Stop();
            _webpAnimationTimer.Tick -= WebPAnimationTimer_Tick;
            _webpAnimationTimer = null;
        }

        _webpFrames?.Clear();
        _webpFrames = null;
        _webpDelays?.Clear();
        _webpDelays = null;
        _currentFrameIndex = 0;
    }

    private void HideSplash()
    {
        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    private void LoadLogo()
    {
        try
        {
            Uri? logoUri = null;

            if (!string.IsNullOrWhiteSpace(_config.StartupLogoPath) && File.Exists(_config.StartupLogoPath))
            {
                logoUri = new Uri(_config.StartupLogoPath, UriKind.Absolute);
            }
            else
            {
                // Embedded default resource
                var resourceUri = new Uri("pack://application:,,,/Resources/mimir_logo.png", UriKind.Absolute);
                logoUri = resourceUri;
            }

            if (logoUri != null)
            {
                var bmp = new BitmapImage(logoUri);
                LogoImage.Source = bmp;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load logo");
        }
    }

    // ── Menu handlers ─────────────────────────────────────────────────────────

    public void SetServices(MqttService mqttService, StateService stateService)
    {
        _mqttService = mqttService;
        _stateService = stateService;
        UpdateMenuStatus();
    }

    public void SetResolutionCallback(Action<int, int> callback)
    {
        _onResolutionChanged = callback;
        // Report current resolution immediately
        ReportCurrentResolution();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportCurrentResolution();
    }

    private void ReportCurrentResolution()
    {
        if (_onResolutionChanged == null)
            return;

        // Use Win32 GetClientRect to get the inner content area in physical pixels.
        // This matches Electron's win.getContentSize() semantics: no title bar, no
        // OS frame, and already in physical (not DIP) coordinates.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect))
            return;

        var width  = rect.Right  - rect.Left;
        var height = rect.Bottom - rect.Top;

        // Only report if the value actually changed (avoid spam during resize drag)
        if (width != _lastReportedWidth || height != _lastReportedHeight)
        {
            _lastReportedWidth  = width;
            _lastReportedHeight = height;
            _logger.LogDebug("Content area resolution: {Width}x{Height} px (Win32 GetClientRect)",
                width, height);
            _onResolutionChanged(width, height);
        }
    }

    public (int Width, int Height) GetCurrentResolution()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out var rect))
            return (rect.Right - rect.Left, rect.Bottom - rect.Top);
        return ((int)ActualWidth, (int)ActualHeight);
    }

    private void UpdateMenuStatus()
    {
        if (_mqttService != null)
        {
            MenuDeviceIdItem.Header = _mqttService.DeviceId ?? "(none)";
            MenuConnectionStatusItem.Header = _mqttService.IsConnected ? "Connected" : "Disconnected";
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void MenuMqttMonitor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_mqttService == null)
            {
                MessageBox.Show("MQTT service not available", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.LogWarning("MQTT monitor clicked but service is null");
                return;
            }

            if (_mqttMonitorWindow != null && _mqttMonitorWindow.IsLoaded)
            {
                _mqttMonitorWindow.Activate();
                return;
            }

            _logger.LogInformation("Opening MQTT monitor window");
            _mqttMonitorWindow = new MqttMonitorWindow(_mqttService)
            {
                Owner = this
            };
            _mqttMonitorWindow.Show();
            _logger.LogInformation("MQTT monitor window opened successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MQTT monitor window");
            MessageBox.Show($"Failed to open MQTT monitor: {ex.Message}\n\nCheck logs for details.", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MimirDisplay", "cache");

            if (Directory.Exists(cacheDir))
            {
                var files = Directory.GetFiles(cacheDir);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }

                MessageBox.Show($"Cleared {files.Length} cached files", "Cache Cleared",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Cache directory not found", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear cache: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuViewLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MimirDisplay", "logs");

            if (Directory.Exists(logDir))
            {
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            else
            {
                MessageBox.Show("Logs directory not found", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open logs: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void MenuResetPairing_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset pairing state? This will clear the stored device ID and registration key.\n\nThe display will need to be paired again on next restart.",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes && _stateService != null)
        {
            _stateService.Update(state =>
            {
                state.ServerAssignedDisplayId = null;
                state.RegistrationKey = null;
                state.Registered = false;
            });

            MessageBox.Show("Pairing state reset. Restart the application to pair again.",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void MenuToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var version = UpdateService.GetCurrentVersion();

        var deviceId = _mqttService?.DeviceId ?? "(none)";
        var pairCode = _mqttService?.PairCode ?? "(none)";
        var connected = _mqttService?.IsConnected == true ? "Yes" : "No";

        var message = $"Mimir Display Client\n" +
                      $"Version: {version}\n\n" +
                      $"Device ID: {deviceId}\n" +
                      $"Pair Code: {pairCode}\n" +
                      $"Connected: {connected}\n\n" +
                      $"\u00A9 2026 Mimir Project";

        MessageBox.Show(message, "About Mimir Display",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void MenuCheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var update = await _updateService.CheckForUpdateAsync();
        if (update is null)
        {
            MessageBox.Show(
                $"You are running the latest version ({UpdateService.GetCurrentVersion()}).",
                "No Updates Available", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowUpdateNotification(update, silent: false);
    }

    /// <summary>
    /// Shows the update-available dialog. When <paramref name="silent"/> is true the
    /// check was automatic; the dialog still asks the user what to do.
    /// </summary>
    private void ShowUpdateNotification(UpdateInfo update, bool silent)
    {
        var current = UpdateService.GetCurrentVersion();
        var hasAsset = !string.IsNullOrEmpty(update.AssetDownloadUrl);

        var body = $"A new version of Mimir Display is available!\n\n" +
                   $"Current version:  {current}\n" +
                   $"Latest version:   {update.LatestVersion}  ({update.TagName})\n\n" +
                   (hasAsset
                       ? "Would you like to download and install it now?\n\nChoose No to open the release page instead."
                       : "Would you like to open the release page to download it?");

        var buttons = hasAsset ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo;
        var result = MessageBox.Show(body, "Update Available", buttons, MessageBoxImage.Information);

        if (hasAsset)
        {
            // Yes = download & install, No = open browser, Cancel = dismiss
            if (result == MessageBoxResult.Yes)
                _ = BeginInstallAsync(update);
            else if (result == MessageBoxResult.No)
                UpdateService.OpenReleasePage(update);
        }
        else
        {
            if (result == MessageBoxResult.Yes)
                UpdateService.OpenReleasePage(update);
        }
    }

    private async Task BeginInstallAsync(UpdateInfo update)
    {
        var progressWindow = new System.Windows.Window
        {
            Title = "Downloading Update",
            Width = 340,
            Height = 110,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
        };
        var bar = new System.Windows.Controls.ProgressBar
        {
            Minimum = 0, Maximum = 100, IsIndeterminate = true,
            Margin = new Thickness(20),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0xE8, 0x6A)),
        };
        progressWindow.Content = bar;
        progressWindow.Show();

        var progress = new Progress<int>(v =>
        {
            bar.IsIndeterminate = false;
            bar.Value = v;
        });

        var success = await _updateService.DownloadAndInstallAsync(update, progress);
        progressWindow.Close();

        if (success)
        {
            MessageBox.Show(
                "Update downloaded successfully.\n\nThe application will now close and restart with the new version.",
                "Restarting", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        else
        {
            var retry = MessageBox.Show(
                "The automatic update failed. Would you like to open the release page to download it manually?",
                "Update Failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (retry == MessageBoxResult.Yes)
                UpdateService.OpenReleasePage(update);
        }
    }

    private void MenuInfoOverlay_Click(object sender, RoutedEventArgs e)
    {
        _isInfoOverlayEnabled = MenuInfoOverlay.IsChecked;
        UpdateInfoOverlayVisibility();
        _logger.LogInformation("File info overlay {State}", _isInfoOverlayEnabled ? "enabled" : "disabled");
    }

    private void MenuArtworkOverlay_Click(object sender, RoutedEventArgs e)
    {
        _isArtworkOverlayEnabled = MenuArtworkOverlay.IsChecked;
        UpdateArtworkOverlay(_currentArtworkMetadata);
        _logger.LogInformation("Artwork overlay {State}", _isArtworkOverlayEnabled ? "enabled" : "disabled");
    }

    private void UpdateInfoOverlayVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            InfoOverlay.Visibility = _isInfoOverlayEnabled && !string.IsNullOrEmpty(_currentFilePath)
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    // Same field order/labels as the server-baked overlay (mimir-source-metart's
    // _apply_overlay) and the Electron client, so a display looks the same
    // whether the panel was baked into the image or rendered here client-side.
    private static readonly (string Key, string Label)[] ArtworkMetaRows =
    {
        ("date", "Date"),
        ("medium", "Medium"),
        ("department", "Gallery"),
        ("culture", "Culture"),
    };

    /// <summary>
    /// Renders (or hides) the artwork details overlay. Shown by default
    /// whenever metadata is present — it's part of the content itself, not
    /// a diagnostic tool like the File Info Overlay — but toggleable via
    /// View > Show Artwork Overlay for anyone who doesn't want it.
    /// </summary>
    private void UpdateArtworkOverlay(Dictionary<string, string>? metadata)
    {
        _currentArtworkMetadata = metadata;

        Dispatcher.Invoke(() =>
        {
            ArtworkOverlayPanel.Children.Clear();

            if (!_isArtworkOverlayEnabled || metadata == null || metadata.Count == 0)
            {
                ArtworkOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            bool anyRowAdded = false;

            var fontScale = _config.ArtworkOverlayFontScale > 0 ? _config.ArtworkOverlayFontScale : 1.0;
            var wrapWidth = _config.ArtworkOverlayWrapWidth;
            var wrapping = wrapWidth > 0 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            var trimming = wrapWidth > 0 ? TextTrimming.None : TextTrimming.CharacterEllipsis;

            if (metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            {
                var tb = new TextBlock
                {
                    Text = title,
                    FontSize = 34 * fontScale,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = wrapping,
                    TextTrimming = trimming,
                };
                if (wrapWidth > 0) tb.MaxWidth = wrapWidth;
                ArtworkOverlayPanel.Children.Add(tb);
                anyRowAdded = true;
            }

            if (metadata.TryGetValue("artist", out var artist) && !string.IsNullOrWhiteSpace(artist))
            {
                var tb = new TextBlock
                {
                    Text = artist,
                    FontSize = 21 * fontScale,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xE6, 0xAA)),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = wrapping,
                    TextTrimming = trimming,
                };
                if (wrapWidth > 0) tb.MaxWidth = wrapWidth;
                ArtworkOverlayPanel.Children.Add(tb);
                anyRowAdded = true;
            }

            foreach (var (key, label) in ArtworkMetaRows)
            {
                if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    var tb = new TextBlock
                    {
                        Text = $"{label.ToUpperInvariant()}   {value}",
                        FontSize = 16 * fontScale,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xBE, 0xBE, 0xBE)),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = wrapping,
                        TextTrimming = trimming,
                    };
                    if (wrapWidth > 0) tb.MaxWidth = wrapWidth;
                    ArtworkOverlayPanel.Children.Add(tb);
                    anyRowAdded = true;
                }
            }

            if (!anyRowAdded)
            {
                ArtworkOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            metadata.TryGetValue("overlay_position", out var position);
            var effectivePosition = !string.IsNullOrWhiteSpace(_config.ArtworkOverlayPositionOverride)
                ? _config.ArtworkOverlayPositionOverride
                : (position ?? "bottom_left");
            (ArtworkOverlay.HorizontalAlignment, ArtworkOverlay.VerticalAlignment) = effectivePosition switch
            {
                "top_left" => (HorizontalAlignment.Left, VerticalAlignment.Top),
                "top_right" => (HorizontalAlignment.Right, VerticalAlignment.Top),
                "top_center" => (HorizontalAlignment.Center, VerticalAlignment.Top),
                "bottom_right" => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
                "bottom_center" => (HorizontalAlignment.Center, VerticalAlignment.Bottom),
                _ => (HorizontalAlignment.Left, VerticalAlignment.Bottom), // bottom_left default
            };

            ArtworkOverlay.Visibility = Visibility.Visible;
        });
    }

    private void UpdateInfoOverlay(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            _currentFilePath = filePath;
            var fileInfo = new FileInfo(filePath);
            _currentFileSize = fileInfo.Length;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            Dispatcher.Invoke(() =>
            {
                // File name
                InfoFileName.Text = $"File: {Path.GetFileName(filePath)}";

                // File size
                InfoFileSize.Text = $"Size: {FormatFileSize(_currentFileSize)}";

                // Format
                var formatName = ext.TrimStart('.').ToUpperInvariant();
                InfoFormat.Text = $"Format: {formatName}";

                // Get image dimensions
                try
                {
                    if (ext == ".webp")
                    {
                        // Use Magick.NET to get WebP info
                        using var magickImageCollection = new MagickImageCollection(filePath);
                        if (magickImageCollection.Count > 0)
                        {
                            var firstFrame = magickImageCollection[0];
                            _currentImageWidth = (int)firstFrame.Width;
                            _currentImageHeight = (int)firstFrame.Height;
                            _currentFrameCount = magickImageCollection.Count;

                            InfoResolution.Text = $"Resolution: {_currentImageWidth}x{_currentImageHeight}";

                            if (_currentFrameCount > 1)
                            {
                                InfoAnimation.Text = $"Animation: {_currentFrameCount} frames";
                                InfoAnimation.Visibility = Visibility.Visible;

                                // Calculate average FPS
                                if (_webpDelays != null && _webpDelays.Count > 0)
                                {
                                    var avgDelay = _webpDelays.Average();
                                    _currentFps = avgDelay > 0 ? 1000.0 / avgDelay : 0;
                                    InfoFPS.Text = $"FPS: {_currentFps:F1}";
                                    InfoFPS.Visibility = Visibility.Visible;
                                }
                            }
                            else
                            {
                                InfoAnimation.Visibility = Visibility.Collapsed;
                                InfoFPS.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                    else if (ext == ".gif")
                    {
                        // Use Magick.NET for GIF info
                        using var magickImageCollection = new MagickImageCollection(filePath);
                        if (magickImageCollection.Count > 0)
                        {
                            var firstFrame = magickImageCollection[0];
                            _currentImageWidth = (int)firstFrame.Width;
                            _currentImageHeight = (int)firstFrame.Height;
                            _currentFrameCount = magickImageCollection.Count;

                            InfoResolution.Text = $"Resolution: {_currentImageWidth}x{_currentImageHeight}";

                            if (_currentFrameCount > 1)
                            {
                                InfoAnimation.Text = $"Animation: {_currentFrameCount} frames";
                                InfoAnimation.Visibility = Visibility.Visible;

                                // Calculate average FPS from frame delays
                                var delays = magickImageCollection.Select(f => (int)(f.AnimationDelay * 10)).ToList();
                                if (delays.Count > 0)
                                {
                                    var avgDelay = delays.Average();
                                    _currentFps = avgDelay > 0 ? 1000.0 / avgDelay : 0;
                                    InfoFPS.Text = $"FPS: {_currentFps:F1}";
                                    InfoFPS.Visibility = Visibility.Visible;
                                }
                            }
                            else
                            {
                                InfoAnimation.Visibility = Visibility.Collapsed;
                                InfoFPS.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                    else
                    {
                        // Static image - use BitmapImage
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        _currentImageWidth = bitmap.PixelWidth;
                        _currentImageHeight = bitmap.PixelHeight;
                        _currentFrameCount = 1;

                        InfoResolution.Text = $"Resolution: {_currentImageWidth}x{_currentImageHeight}";
                        InfoAnimation.Visibility = Visibility.Collapsed;
                        InfoFPS.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read image dimensions for info overlay");
                    InfoResolution.Text = "Resolution: Unknown";
                    InfoAnimation.Visibility = Visibility.Collapsed;
                    InfoFPS.Visibility = Visibility.Collapsed;
                }

                // Last updated
                InfoLastUpdated.Text = $"Updated: {DateTime.Now:HH:mm:ss}";

                // Show overlay if enabled
                UpdateInfoOverlayVisibility();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update info overlay");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}


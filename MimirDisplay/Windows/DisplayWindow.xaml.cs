using System.IO;
using System.Windows;
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

    public DisplayWindow(IOptions<DisplayConfig> config, ILogger<DisplayWindow> logger)
    {
        _config = config.Value;
        _logger = logger;
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
    public Task ShowImageAsync(string filePath)
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
                // Animated WebP
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

        // Use ActualWidth/Height for the content area
        var width = (int)ActualWidth;
        var height = (int)ActualHeight;

        // Only report if resolution actually changed (avoid spam during resize drag)
        if (width != _lastReportedWidth || height != _lastReportedHeight)
        {
            _lastReportedWidth = width;
            _lastReportedHeight = height;
            _logger.LogDebug("Window resolution changed to {Width}x{Height}", width, height);
            _onResolutionChanged(width, height);
        }
    }

    public (int Width, int Height) GetCurrentResolution()
    {
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
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "Unknown";

        var deviceId = _mqttService?.DeviceId ?? "(none)";
        var pairCode = _mqttService?.PairCode ?? "(none)";
        var connected = _mqttService?.IsConnected == true ? "Yes" : "No";

        var message = $"Mimir Display Client\n" +
                      $"Version: {version}\n\n" +
                      $"Device ID: {deviceId}\n" +
                      $"Pair Code: {pairCode}\n" +
                      $"Connected: {connected}\n\n" +
                      $"© 2026 Mimir Project";

        MessageBox.Show(message, "About Mimir Display",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}


using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;
using MimirDisplay.Services;
using XamlAnimatedGif;

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

    public DisplayWindow(IOptions<DisplayConfig> config, ILogger<DisplayWindow> logger)
    {
        _config = config.Value;
        _logger = logger;
        InitializeComponent();
        Loaded += OnLoaded;

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
            _isFullscreen = true;
            _logger.LogInformation("Starting in fullscreen mode (MIMIR__FULLSCREEN=true)");
        }
        else
        {
            // Already configured in XAML, just log
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
    /// Handles PNG, JPEG, BMP, GIF (animated), and WebP (requires Windows codec).
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

        AnimationBehavior.SetSourceUri(AnimatedImage, new Uri(filePath, UriKind.Absolute));
        AnimationBehavior.SetRepeatBehavior(AnimatedImage, System.Windows.Media.Animation.RepeatBehavior.Forever);
        AnimatedImage.Stretch = _config.HdmiScaleMode == "fill"
            ? System.Windows.Media.Stretch.UniformToFill
            : System.Windows.Media.Stretch.Uniform;
        AnimatedImage.Visibility = Visibility.Visible;
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
}

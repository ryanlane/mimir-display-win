using System.Windows;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimirDisplay.Configuration;
using MimirDisplay.Services;
using MimirDisplay.Windows;
using Serilog;

namespace MimirDisplay;

/// <summary>
/// Application entry point and DI root.
/// Builds an IHost with all services, then shows the WPF display window.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private DisplayWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load .env file FIRST (next to the exe or in CWD)
        LoadEnvFile();

        // Configure Serilog early so startup messages are captured
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MimirDisplay", "logs", "mimir-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateBootstrapLogger();

        // Debug: Log what we think is in the environment
        Log.Information("Environment Check - MIMIR__MQTTBROKERHOST: {MqttHost}", 
            Environment.GetEnvironmentVariable("MIMIR__MQTTBROKERHOST") ?? "(not set)");
        Log.Information("Environment Check - MIMIR__PLATFORMURL: {PlatformUrl}", 
            Environment.GetEnvironmentVariable("MIMIR__PLATFORMURL") ?? "(not set)");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, services, cfg) =>
            {
                var level = ctx.Configuration["Mimir:LogLevel"] ?? "Information";
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MimirDisplay", "logs");
                cfg.WriteTo.Console()
                   .WriteTo.File(
                       Path.Combine(logDir, "mimir-.log"),
                       rollingInterval: RollingInterval.Day,
                       retainedFileCountLimit: 7)
                   .MinimumLevel.Is(ParseLogLevel(level));
            })
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                // Environment variables without a prefix, expecting keys like Mimir__MqttBrokerHost
                cfg.AddEnvironmentVariables();
                cfg.AddCommandLine(e.Args.ToArray());
            })
            .ConfigureServices((ctx, services) =>
            {
                // Config
                services.Configure<DisplayConfig>(ctx.Configuration.GetSection(DisplayConfig.SectionName));

                // Core services
                services.AddSingleton<StateService>();
                services.AddSingleton<ContentService>();
                services.AddSingleton<DiscoveryService>();
                services.AddSingleton<MqttService>();
                services.AddSingleton<HealthService>();
                services.AddSingleton<DisplayOrchestrator>();

                // WPF window registered as singleton so DI can resolve it
                services.AddSingleton<DisplayWindow>();

                // HTTP client with polly-style retry via the factory
                services.AddHttpClient(nameof(ContentService), client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("User-Agent", "MimirDisplay/1.0");
                });

                // Background hosted service
                services.AddHostedService(sp => sp.GetRequiredService<DisplayOrchestrator>());
            })
            .Build();

        // Create and show the display window before starting the host
        _window = _host.Services.GetRequiredService<DisplayWindow>();
        MainWindow = _window;
        _window.Show();

        // Wire display callbacks
        var orchestrator = _host.Services.GetRequiredService<DisplayOrchestrator>();
        orchestrator.SetCallbacks(
            onShowImage: path => _window.ShowImageAsync(path),
            onStatusText: text => _window.SetStatusText(text),
            onPairCode: code => _window.SetPairCode(code)
        );

        // Show pair code immediately from MQTT service before host starts
        var mqtt = _host.Services.GetRequiredService<MqttService>();
        _window.SetPairCode(mqtt.PairCode);

        // Wire services for admin panel
        var state = _host.Services.GetRequiredService<StateService>();
        _window.SetServices(mqtt, state);

        // Wire resolution tracking: update MQTT when window is resized
        _window.SetResolutionCallback((width, height) => mqtt.UpdateResolution(width, height));

        // Start the host (runs background services)
        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void LoadEnvFile()
    {
        var exeDir = AppContext.BaseDirectory;
        Log.Information("Attempting to load .env file from: {ExeDir}", exeDir);

        foreach (var candidate in new[] { ".env", Path.Combine(exeDir, ".env") })
        {
            Log.Information("Checking for .env at: {Path}, Exists: {Exists}", 
                candidate, File.Exists(candidate));

            if (File.Exists(candidate))
            {
                Log.Information("Loading .env file from: {Path}", candidate);

                // Load without TraversePath - just load from exact file
                Env.Load(candidate);

                // Debug: Log some loaded values
                var mqttHost = Environment.GetEnvironmentVariable("MIMIR__MQTTBROKERHOST");
                var platformUrl = Environment.GetEnvironmentVariable("MIMIR__PLATFORMURL");
                Log.Information("After loading .env - MIMIR__MQTTBROKERHOST: {MqttHost}, MIMIR__PLATFORMURL: {PlatformUrl}", 
                    mqttHost ?? "(null)", platformUrl ?? "(null)");
                return;
            }
        }
        Log.Warning("No .env file found in any candidate locations");
    }

    private static Serilog.Events.LogEventLevel ParseLogLevel(string level) =>
        level.ToLowerInvariant() switch
        {
            "debug" or "verbose" => Serilog.Events.LogEventLevel.Debug,
            "warning" or "warn" => Serilog.Events.LogEventLevel.Warning,
            "error" => Serilog.Events.LogEventLevel.Error,
            _ => Serilog.Events.LogEventLevel.Information,
        };
}

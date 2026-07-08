using System.Text.Json;
using MimirDisplay.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirDisplay.Configuration;

namespace MimirDisplay.Services;

/// <summary>
/// Manages persistent display state in a JSON file on disk.
/// Thread-safe via a lock on all read/write operations.
/// </summary>
public sealed class StateService
{
    private readonly string _statePath;
    private readonly ILogger<StateService> _logger;
    private readonly object _lock = new();
    private DisplayState _state = new();

    public StateService(IOptions<DisplayConfig> config, ILogger<StateService> logger)
    {
        _logger = logger;
        var stateDir = config.Value.GetStateDirectory();
        Directory.CreateDirectory(stateDir);
        _statePath = Path.Combine(stateDir, "display_state.json");
        Load();
    }

    public DisplayState Current
    {
        get { lock (_lock) { return _state; } }
    }

    public void Update(Action<DisplayState> mutate)
    {
        lock (_lock)
        {
            mutate(_state);
            _state.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                _state = JsonSerializer.Deserialize<DisplayState>(json, DisplayState.SerializerOptions)
                         ?? new DisplayState();
                _logger.LogDebug("Loaded state from {Path}", _statePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load state; starting fresh");
            _state = new DisplayState();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, DisplayState.SerializerOptions);
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to {Path}", _statePath);
        }
    }
}

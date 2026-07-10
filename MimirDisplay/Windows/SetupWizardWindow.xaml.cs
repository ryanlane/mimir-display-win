using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MimirDisplay.Windows;

/// <summary>
/// First-run setup wizard. Shown when .env is missing or MQTT is unconfigured.
/// Walks the user through connection, identity, and display settings, then
/// writes a .env file and sets <see cref="ConfigurationSaved"/> = true.
/// </summary>
public partial class SetupWizardWindow : Window
{
    // True when the user completed the wizard and saved. App.xaml.cs checks this.
    public bool ConfigurationSaved { get; private set; }

    private int  _currentPage = 1;
    private const int TotalPages = 5;

    private readonly string _envPath;

    // Page titles / subtitles
    private static readonly (string Title, string Subtitle)[] PageMeta =
    [
        ("Welcome to Mimir Display",  "Let's get your display connected."),
        ("Server Connection",         "Enter your Mimir server and MQTT broker details."),
        ("Display Identity",          "Name and locate this display."),
        ("Display Settings",          "Choose how content is presented."),
        ("Ready to Launch",           "Your configuration has been saved."),
    ];

    public SetupWizardWindow()
    {
        InitializeComponent();

        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        _envPath = System.IO.Path.Combine(exeDir, ".env");

        // Pre-fill fields with any values already in the .env (partial config case)
        PreFillFromEnv();
        UpdateUI();
    }

    // ── Pre-fill from existing .env if present ───────────────────────────────

    private void PreFillFromEnv()
    {
        var vals = ReadEnvFile();
        PlatformUrlBox.Text      = vals.GetValueOrDefault("MIMIR__PLATFORMURL", "");
        MqttHostBox.Text         = vals.GetValueOrDefault("MIMIR__MQTTBROKERHOST", "");
        MqttPortBox.Text         = vals.GetValueOrDefault("MIMIR__MQTTBROKERPORT", "1883");
        MqttUserBox.Text         = vals.GetValueOrDefault("MIMIR__MQTTUSERNAME", "");
        MqttPasswordBox.Password = vals.GetValueOrDefault("MIMIR__MQTTPASSWORD", "");

        DisplayNameBox.Text     = vals.GetValueOrDefault("MIMIR__DISPLAYNAME", "");
        DisplayLocationBox.Text = vals.GetValueOrDefault("MIMIR__DISPLAYLOCATION", "");
        DisplayIdBox.Text       = vals.GetValueOrDefault("MIMIR__DISPLAYID", "");

        var orientation = vals.GetValueOrDefault("MIMIR__DISPLAYORIENTATION", "landscape");
        foreach (System.Windows.Controls.ComboBoxItem item in OrientationBox.Items)
            if (item.Content?.ToString() == orientation) { item.IsSelected = true; break; }

        var scale = vals.GetValueOrDefault("MIMIR__HDMISCALEMODE", "fit");
        foreach (System.Windows.Controls.ComboBoxItem item in ScaleModeBox.Items)
            if (item.Content?.ToString() == scale) { item.IsSelected = true; break; }

        FullscreenBox.IsChecked = vals.GetValueOrDefault("MIMIR__FULLSCREEN", "false")
                                      .Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> ReadEnvFile()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_envPath)) return result;
        foreach (var raw in File.ReadAllLines(_envPath))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx < 1) continue;
            result[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return result;
    }

    // ── Public helper: should the wizard be shown? ───────────────────────────

    /// <summary>
    /// Returns true when the MQTT broker host is missing (wizard required).
    /// Also triggers if .env doesn't exist.
    /// </summary>
    public static bool NeedsSetup()
    {
        var exeDir  = AppDomain.CurrentDomain.BaseDirectory;
        var envPath = System.IO.Path.Combine(exeDir, ".env");

        if (!File.Exists(envPath)) return true;

        // Parse existing .env
        foreach (var raw in File.ReadAllLines(envPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || !line.Contains('=')) continue;
            var idx = line.IndexOf('=');
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Equals("MIMIR__MQTTBROKERHOST", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(val);
        }

        // Key not found at all → needs setup
        return true;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        // On the finish page the button becomes "Launch"
        if (_currentPage == TotalPages)
        {
            if (ConfigurationSaved)
            {
                DialogResult = true;
                Close();
            }
            return;
        }

        if (!ValidateCurrentPage()) return;

        // Build summary just before showing the finish page
        if (_currentPage == TotalPages - 1)
        {
            SaveConfiguration();
            BuildSummary();
        }

        _currentPage++;
        UpdateUI();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            NextButton.Content = "Next →";
            UpdateUI();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfigurationSaved)
        {
            var result = MessageBox.Show(
                "Setup is not complete. Mimir Display cannot run without a configuration.\n\n" +
                "Exit the application?",
                "Exit Setup?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private bool ValidateCurrentPage()
    {
        if (_currentPage == 2)
        {
            var ok = true;

            if (string.IsNullOrWhiteSpace(MqttHostBox.Text))
            {
                MqttHostError.Visibility = Visibility.Visible;
                ok = false;
            }
            else MqttHostError.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(MqttUserBox.Text))
            {
                MqttUserError.Visibility = Visibility.Visible;
                ok = false;
            }
            else MqttUserError.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(MqttPasswordBox.Password))
            {
                MqttPassError.Visibility = Visibility.Visible;
                ok = false;
            }
            else MqttPassError.Visibility = Visibility.Collapsed;

            return ok;
        }

        return true;
    }

    // ── UI state ─────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        // Show/hide pages
        Page1.Visibility = _currentPage == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = _currentPage == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = _currentPage == 3 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = _currentPage == 4 ? Visibility.Visible : Visibility.Collapsed;
        Page5.Visibility = _currentPage == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Header text
        var meta = PageMeta[_currentPage - 1];
        PageTitleText.Text    = meta.Title;
        PageSubtitleText.Text = meta.Subtitle;

        // Step label
        StepLabel.Text = $"Step {_currentPage} of {TotalPages}";

        // Back button
        BackButton.IsEnabled = _currentPage > 1 && _currentPage < TotalPages;

        // Next button label
        NextButton.Content = _currentPage == TotalPages ? "Launch" : "Next →";

        // Step dots
        Ellipse[] dots = [Dot1, Dot2, Dot3, Dot4, Dot5];
        for (int i = 0; i < dots.Length; i++)
            dots[i].Fill = new SolidColorBrush(
                i < _currentPage ? Color.FromRgb(0x4E, 0xC9, 0xB0)
                                 : Color.FromRgb(0x44, 0x44, 0x44));
    }

    // ── Summary page ─────────────────────────────────────────────────────────

    private void BuildSummary()
    {
        var orientation = (OrientationBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                              ?.Content?.ToString() ?? "landscape";
        var scaleMode   = (ScaleModeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                              ?.Content?.ToString() ?? "fit";

        var sb = new StringBuilder();
        sb.AppendLine($"Platform URL  : {OrDefault(PlatformUrlBox.Text, "(auto-discover)")}");
        sb.AppendLine($"MQTT Host     : {MqttHostBox.Text}:{OrDefault(MqttPortBox.Text, "1883")}");
        sb.AppendLine($"MQTT Username : {MqttUserBox.Text}");
        sb.AppendLine($"Display Name  : {OrDefault(DisplayNameBox.Text, "(hostname)")}");
        sb.AppendLine($"Location      : {OrDefault(DisplayLocationBox.Text, "(none)")}");
        sb.AppendLine($"Orientation   : {orientation}");
        sb.AppendLine($"Scale Mode    : {scaleMode}");
        sb.AppendLine($"Fullscreen    : {(FullscreenBox.IsChecked == true ? "yes" : "no")}");

        SummaryText.Text = sb.ToString();
    }

    private static string OrDefault(string? val, string fallback)
        => string.IsNullOrWhiteSpace(val) ? fallback : val;

    // ── Save .env ─────────────────────────────────────────────────────────────

    private void SaveConfiguration()
    {
        var orientation = (OrientationBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                              ?.Content?.ToString() ?? "landscape";
        var scaleMode   = (ScaleModeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                              ?.Content?.ToString() ?? "fit";

        var port = int.TryParse(MqttPortBox.Text, out var p) ? p : 1883;

        var lines = new StringBuilder();
        lines.AppendLine("# Mimir Display — generated by setup wizard");
        lines.AppendLine();
        lines.AppendLine("# Connection");
        lines.AppendLine($"MIMIR__PLATFORMURL={PlatformUrlBox.Text.Trim()}");
        lines.AppendLine($"MIMIR__MQTTBROKERHOST={MqttHostBox.Text.Trim()}");
        lines.AppendLine($"MIMIR__MQTTBROKERPORT={port}");
        lines.AppendLine($"MIMIR__MQTTUSERNAME={MqttUserBox.Text.Trim()}");
        lines.AppendLine($"MIMIR__MQTTPASSWORD={MqttPasswordBox.Password}");
        lines.AppendLine("MIMIR__MQTTHEARTBEATINTERVAL=30");
        lines.AppendLine();
        lines.AppendLine("# Identity");
        lines.AppendLine($"MIMIR__DISPLAYID={DisplayIdBox.Text.Trim()}");
        lines.AppendLine($"MIMIR__DISPLAYNAME={OrDefault(DisplayNameBox.Text.Trim(), "Mimir Windows Display")}");
        lines.AppendLine($"MIMIR__DISPLAYLOCATION={OrDefault(DisplayLocationBox.Text.Trim(), "Unknown")}");
        lines.AppendLine();
        lines.AppendLine("# Display");
        lines.AppendLine($"MIMIR__DISPLAYORIENTATION={orientation}");
        lines.AppendLine($"MIMIR__HDMISCALEMODE={scaleMode}");
        lines.AppendLine("MIMIR__HDMIBACKGROUNDCOLOR=#000000");
        lines.AppendLine($"MIMIR__FULLSCREEN={(FullscreenBox.IsChecked == true ? "true" : "false")}");
        lines.AppendLine();
        lines.AppendLine("# Operational");
        lines.AppendLine("MIMIR__WEBHOOKENABLED=true");
        lines.AppendLine("MIMIR__WEBHOOKPORT=8081");
        lines.AppendLine("MIMIR__LOGLEVEL=Information");
        lines.AppendLine("MIMIR__CACHEDIRECTORY=");
        lines.AppendLine("MIMIR__STATEDIRECTORY=");

        try
        {
            File.WriteAllText(_envPath, lines.ToString(), Encoding.UTF8);

            // Reload env vars in-process so the host picks them up without a restart
            DotNetEnv.Env.Load(_envPath);

            ConfigurationSaved = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save configuration:\n\n{ex.Message}",
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

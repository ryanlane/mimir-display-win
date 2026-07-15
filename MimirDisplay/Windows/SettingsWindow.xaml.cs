using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MimirDisplay.Windows;

public partial class SettingsWindow : Window
{
    private readonly string _envPath;
    private Dictionary<string, string> _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();

        // Find .env file next to the executable
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        _envPath = Path.Combine(exeDir, ".env");

        LoadSettings();
        PopulateUI();
    }

    private void LoadSettings()
    {
        _settings.Clear();

        if (!File.Exists(_envPath))
        {
            LoadDefaults();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_envPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    _settings[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load settings: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            LoadDefaults();
        }
    }

    private void LoadDefaults()
    {
        _settings = new Dictionary<string, string>
        {
            ["MIMIR__PLATFORMURL"] = "",
            ["MIMIR__MQTTBROKERHOST"] = "",
            ["MIMIR__MQTTBROKERPORT"] = "1883",
            ["MIMIR__MQTTUSERNAME"] = "",
            ["MIMIR__MQTTPASSWORD"] = "",
            ["MIMIR__MQTTHEARTBEATINTERVAL"] = "30",
            ["MIMIR__DISPLAYID"] = "",
            ["MIMIR__DISPLAYNAME"] = "Mimir Windows Display",
            ["MIMIR__DISPLAYLOCATION"] = "Unknown",
            ["MIMIR__FULLSCREEN"] = "false",
            ["MIMIR__DISPLAYORIENTATION"] = "landscape",
            ["MIMIR__HDMISCALEMODE"] = "fit",
            ["MIMIR__ARTWORKOVERLAYFONTSCALE"] = "1.0",
            ["MIMIR__ARTWORKOVERLAYPOSITONOVERRIDE"] = "",
            ["MIMIR__ARTWORKOVERLAYWRAPWIDTH"] = "0",
        };
    }

    private void PopulateUI()
    {
        PlatformUrlTextBox.Text = GetSetting("MIMIR__PLATFORMURL");
        MqttBrokerHostTextBox.Text = GetSetting("MIMIR__MQTTBROKERHOST");
        MqttBrokerPortTextBox.Text = GetSetting("MIMIR__MQTTBROKERPORT");
        MqttUsernameTextBox.Text = GetSetting("MIMIR__MQTTUSERNAME");
        MqttPasswordBox.Password = GetSetting("MIMIR__MQTTPASSWORD");
        HeartbeatIntervalTextBox.Text = GetSetting("MIMIR__MQTTHEARTBEATINTERVAL");

        DisplayIdTextBox.Text = GetSetting("MIMIR__DISPLAYID");
        DisplayNameTextBox.Text = GetSetting("MIMIR__DISPLAYNAME");
        DisplayLocationTextBox.Text = GetSetting("MIMIR__DISPLAYLOCATION");

        FullscreenCheckBox.IsChecked = GetSetting("MIMIR__FULLSCREEN").Equals("true", StringComparison.OrdinalIgnoreCase);

        SelectComboItem(OrientationComboBox, GetSetting("MIMIR__DISPLAYORIENTATION"));
        SelectComboItem(ScaleModeComboBox, GetSetting("MIMIR__HDMISCALEMODE"));

        ArtworkFontScaleTextBox.Text = GetSetting("MIMIR__ARTWORKOVERLAYFONTSCALE");
        SelectArtworkPositionItem(GetSetting("MIMIR__ARTWORKOVERLAYPOSITONOVERRIDE"));
        ArtworkWrapWidthTextBox.Text = GetSetting("MIMIR__ARTWORKOVERLAYWRAPWIDTH");
    }

    private string GetSetting(string key)
    {
        return _settings.TryGetValue(key, out var value) ? value : "";
    }

    private void SelectComboItem(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content.ToString() == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void SelectArtworkPositionItem(string value)
    {
        foreach (ComboBoxItem item in ArtworkPositionComboBox.Items)
        {
            var tag = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
            if (tag == value)
            {
                ArtworkPositionComboBox.SelectedItem = item;
                return;
            }
        }
        ArtworkPositionComboBox.SelectedIndex = 0;
    }

    private void SaveSettings()
    {
        _settings["MIMIR__PLATFORMURL"] = PlatformUrlTextBox.Text.Trim();
        _settings["MIMIR__MQTTBROKERHOST"] = MqttBrokerHostTextBox.Text.Trim();
        _settings["MIMIR__MQTTBROKERPORT"] = MqttBrokerPortTextBox.Text.Trim();
        _settings["MIMIR__MQTTUSERNAME"] = MqttUsernameTextBox.Text.Trim();
        _settings["MIMIR__MQTTPASSWORD"] = MqttPasswordBox.Password;
        _settings["MIMIR__MQTTHEARTBEATINTERVAL"] = HeartbeatIntervalTextBox.Text.Trim();

        _settings["MIMIR__DISPLAYID"] = DisplayIdTextBox.Text.Trim();
        _settings["MIMIR__DISPLAYNAME"] = DisplayNameTextBox.Text.Trim();
        _settings["MIMIR__DISPLAYLOCATION"] = DisplayLocationTextBox.Text.Trim();

        _settings["MIMIR__FULLSCREEN"] = FullscreenCheckBox.IsChecked == true ? "true" : "false";
        _settings["MIMIR__DISPLAYORIENTATION"] = (OrientationComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "landscape";
        _settings["MIMIR__HDMISCALEMODE"] = (ScaleModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "fit";

        _settings["MIMIR__ARTWORKOVERLAYFONTSCALE"] = ArtworkFontScaleTextBox.Text.Trim();
        var posItem = ArtworkPositionComboBox.SelectedItem as ComboBoxItem;
        _settings["MIMIR__ARTWORKOVERLAYPOSITONOVERRIDE"] = posItem?.Tag?.ToString() ?? posItem?.Content?.ToString() ?? "";
        _settings["MIMIR__ARTWORKOVERLAYWRAPWIDTH"] = ArtworkWrapWidthTextBox.Text.Trim();

        try
        {
            var lines = new List<string>
            {
                "# Mimir Windows Display Client — Environment Configuration",
                "# This file is managed by the Settings window.",
                "",
                "# ── Connection ───────────────────────────────────────────────────────",
                $"MIMIR__PLATFORMURL={_settings["MIMIR__PLATFORMURL"]}",
                $"MIMIR__MQTTBROKERHOST={_settings["MIMIR__MQTTBROKERHOST"]}",
                $"MIMIR__MQTTBROKERPORT={_settings["MIMIR__MQTTBROKERPORT"]}",
                $"MIMIR__MQTTUSERNAME={_settings["MIMIR__MQTTUSERNAME"]}",
                $"MIMIR__MQTTPASSWORD={_settings["MIMIR__MQTTPASSWORD"]}",
                $"MIMIR__MQTTHEARTBEATINTERVAL={_settings["MIMIR__MQTTHEARTBEATINTERVAL"]}",
                "",
                "# ── Identity ────────────────────────────────────────────────────────",
                $"MIMIR__DISPLAYID={_settings["MIMIR__DISPLAYID"]}",
                $"MIMIR__DISPLAYNAME={_settings["MIMIR__DISPLAYNAME"]}",
                $"MIMIR__DISPLAYLOCATION={_settings["MIMIR__DISPLAYLOCATION"]}",
                "",
                "# ── Display ─────────────────────────────────────────────────────────",
                $"MIMIR__FULLSCREEN={_settings["MIMIR__FULLSCREEN"]}",
                $"MIMIR__DISPLAYORIENTATION={_settings["MIMIR__DISPLAYORIENTATION"]}",
                $"MIMIR__HDMISCALEMODE={_settings["MIMIR__HDMISCALEMODE"]}",
                "",
                "# ── Artwork Overlay ─────────────────────────────────────────────────────────",
                $"MIMIR__ARTWORKOVERLAYFONTSCALE={_settings["MIMIR__ARTWORKOVERLAYFONTSCALE"]}",
                $"MIMIR__ARTWORKOVERLAYPOSITONOVERRIDE={_settings["MIMIR__ARTWORKOVERLAYPOSITONOVERRIDE"]}",
                $"MIMIR__ARTWORKOVERLAYWRAPWIDTH={_settings["MIMIR__ARTWORKOVERLAYWRAPWIDTH"]}",
            };

            File.WriteAllLines(_envPath, lines);

            MessageBox.Show("Settings saved successfully.\n\nRestart the application for changes to take effect.",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Reset all settings to defaults?", "Confirm Reset",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            LoadDefaults();
            PopulateUI();
        }
    }
}

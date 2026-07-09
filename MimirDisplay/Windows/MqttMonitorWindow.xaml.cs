using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MimirDisplay.Models;
using MimirDisplay.Services;

namespace MimirDisplay.Windows;

public partial class MqttMonitorWindow : Window
{
    private readonly MqttService _mqttService;
    private readonly ObservableCollection<MqttMessageViewModel> _allMessages = new();
    private readonly ObservableCollection<MqttMessageViewModel> _filteredMessages = new();
    private const int MaxMessages = 500;

    public MqttMonitorWindow(MqttService mqttService)
    {
        if (mqttService == null)
            throw new ArgumentNullException(nameof(mqttService));

        InitializeComponent();
        _mqttService = mqttService;

        MessageListBox.ItemsSource = _filteredMessages;

        // Subscribe to MQTT events
        _mqttService.MessageReceived += OnMqttMessageReceived;

        Closed += (s, e) =>
        {
            _mqttService.MessageReceived -= OnMqttMessageReceived;
        };

        UpdateStatus();
    }

    private void OnMqttMessageReceived(object? sender, MqttMessageEvent e)
    {
        // Must update UI on UI thread
        Dispatcher.InvokeAsync(() =>
        {
            var vm = new MqttMessageViewModel(e);
            _allMessages.Add(vm);

            // Limit total messages
            while (_allMessages.Count > MaxMessages)
            {
                _allMessages.RemoveAt(0);
            }

            ApplyFilter();
            UpdateStatus();

            if (AutoScrollCheckBox.IsChecked == true && _filteredMessages.Count > 0)
            {
                MessageListBox.ScrollIntoView(_filteredMessages.Last());
            }
        });
    }

    private void ApplyFilter()
    {
        // Guard against being called during InitializeComponent before controls are ready
        if (FilterTextBox == null || ShowSentCheckBox == null || ShowReceivedCheckBox == null)
            return;

        var filter = FilterTextBox.Text?.ToLowerInvariant() ?? "";
        var showSent = ShowSentCheckBox.IsChecked == true;
        var showReceived = ShowReceivedCheckBox.IsChecked == true;

        _filteredMessages.Clear();

        foreach (var msg in _allMessages)
        {
            // Direction filter
            if (msg.Event.Direction == MqttMessageDirection.Sent && !showSent) continue;
            if (msg.Event.Direction == MqttMessageDirection.Received && !showReceived) continue;

            // Text filter
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var matchesTopic = msg.Topic.ToLowerInvariant().Contains(filter);
                var matchesPayload = msg.PayloadPreview.ToLowerInvariant().Contains(filter);
                var matchesType = msg.MessageType?.ToLowerInvariant().Contains(filter) == true;

                if (!matchesTopic && !matchesPayload && !matchesType)
                    continue;
            }

            _filteredMessages.Add(msg);
        }
    }

    private void UpdateStatus()
    {
        var connected = _mqttService.IsConnected ? "Connected" : "Disconnected";
        var deviceId = _mqttService.DeviceId ?? "N/A";
        StatusTextBlock.Text = $"{connected} | Device: {deviceId}";
        MessageCountTextBlock.Text = $"{_filteredMessages.Count} / {_allMessages.Count} messages";
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _allMessages.Clear();
        _filteredMessages.Clear();
        UpdateStatus();
    }

    private void MessageListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+C to copy
        if (e.Key == System.Windows.Input.Key.C && 
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            CopyAllSelected_Click(sender, e);
            e.Handled = true;
        }
    }

    private void CopyTopic_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListBox.SelectedItem is MqttMessageViewModel vm)
        {
            Clipboard.SetText(vm.Topic);
            StatusTextBlock.Text = "Topic copied to clipboard";
        }
    }

    private void CopyPayload_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListBox.SelectedItem is MqttMessageViewModel vm)
        {
            Clipboard.SetText(vm.Event.Payload);
            StatusTextBlock.Text = $"Payload copied to clipboard ({vm.Event.PayloadSize} bytes)";
        }
    }

    private void CopyFullMessage_Click(object sender, RoutedEventArgs e)
    {
        if (MessageListBox.SelectedItem is MqttMessageViewModel vm)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = vm.Timestamp.ToString("o"),
                direction = vm.Direction,
                topic = vm.Topic,
                messageType = vm.MessageType,
                payload = vm.Event.Payload,
                payloadSize = vm.Event.PayloadSize
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            Clipboard.SetText(json);
            StatusTextBlock.Text = "Full message copied to clipboard (JSON)";
        }
    }

    private void CopyAllSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = MessageListBox.SelectedItems.Cast<MqttMessageViewModel>().ToList();
        if (selected.Count == 0)
        {
            StatusTextBlock.Text = "No messages selected";
            return;
        }

        var text = string.Join("\n---\n", selected.Select(vm =>
            $"[{vm.Timestamp:HH:mm:ss.fff}] {vm.Direction} | {vm.Topic}\n" +
            $"Type: {vm.MessageType ?? "N/A"}\n" +
            $"Payload ({vm.Event.PayloadSize} bytes):\n{vm.Event.Payload}"));

        Clipboard.SetText(text);
        StatusTextBlock.Text = $"Copied {selected.Count} message(s) to clipboard";
    }
}

public class MqttMessageViewModel
{
    public MqttMessageEvent Event { get; }

    public MqttMessageViewModel(MqttMessageEvent evt)
    {
        Event = evt;
    }

    public DateTime Timestamp => Event.Timestamp;
    public string Direction => Event.Direction == MqttMessageDirection.Sent ? "SENT" : "RECV";
    public Brush DirectionColor => Event.Direction == MqttMessageDirection.Sent
        ? new SolidColorBrush(Color.FromRgb(78, 201, 176)) // Teal
        : new SolidColorBrush(Color.FromRgb(86, 156, 214)); // Blue
    public string Topic => Event.Topic;
    public string? MessageType => Event.MessageType;
    public bool HasMessageType => !string.IsNullOrWhiteSpace(Event.MessageType);
    public string PayloadPreview
    {
        get
        {
            var payload = Event.Payload;
            if (payload.Length > 300)
                return payload[..297] + "...";
            return payload;
        }
    }
    public string SizeText => $"{Event.PayloadSize} bytes";
}

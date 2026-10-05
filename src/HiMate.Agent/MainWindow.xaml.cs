using System.Collections.ObjectModel;
using System.Windows;
using HiMate.Agent.Models;
using HiMate.Agent.Protocol;
using HiMate.Agent.Services;

namespace HiMate.Agent;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly DpapiSecretStore _secretStore = new();
    private readonly LogService _log = new();
    private EventStore _store = null!;
    private readonly HiMateApiClient _api = new();
    private SerialDeviceService _serial = null!;
    private SyncService _sync = null!;
    private AgentSettings _settings = new();
    private CancellationTokenSource _cts = new();
    private readonly ObservableCollection<CardEvent> _events = [];
    private readonly ObservableCollection<ServerCommand> _commands = [];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += (_, _) => ShutdownServices();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsService.LoadAsync();
            _store = new EventStore(_settingsService.DatabasePath);
            await _store.InitializeAsync();
            _serial = new SerialDeviceService(_log);
            _serial.LineReceived += Serial_LineReceived;
            _sync = new SyncService(_store, _api, _log);

            LogsList.ItemsSource = _log.Items;
            EventsGrid.ItemsSource = _events;
            CommandsGrid.ItemsSource = _commands;

            LoadSettingsIntoUi();
            RefreshPorts();
            ConfigureApi();
            await RefreshDashboardAsync();
            await RefreshTransactionsAsync();
            _log.Info("HiMate Agent ready");
            _ = BackgroundLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "HiMate Agent startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task BackgroundLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (HasServerSettings())
                {
                    await _sync.SyncOnceAsync(ct);
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await RefreshDashboardAsync();
                        await RefreshTransactionsAsync();
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warn($"Background sync: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_settings.SyncIntervalSeconds, 10, 300)), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async void Serial_LineReceived(string line)
    {
        if (!DeviceProtocolParser.TryParseCardEvent(line, out var cardEvent))
        {
            if (line.StartsWith("EVENT_ACK_RESULT|", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            return;
        }

        try
        {
            var result = await _store.SaveFromDeviceAsync(cardEvent);
            if (result == SaveEventResult.Conflict)
            {
                _log.Error($"LOCAL EVENT CONFLICT: {cardEvent.EventKey}; device ACK withheld");
                return;
            }

            // Durable SQLite commit happened before this ACK.
            _serial.Send($"EVENT_ACK|ID={cardEvent.DeviceEventId}");
            await _store.MarkDeviceAckedAsync(cardEvent);
            _log.Info($"Event saved locally: ID={cardEvent.DeviceEventId} {cardEvent.Type} UID={cardEvent.Uid}");

            await Dispatcher.InvokeAsync(async () =>
            {
                await RefreshDashboardAsync();
                await RefreshTransactionsAsync();
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Event persistence failed; no ACK sent: {ex.Message}");
        }
    }

    private void ConfigureApi()
    {
        _api.BaseUrl = _settings.ServerUrl;
        _api.DeviceCode = _settings.DeviceCode;
        try { _api.DeviceSecret = _secretStore.Load(_settingsService.SecretPath); }
        catch (Exception ex) { _log.Warn($"Could not load protected secret: {ex.Message}"); }
        SideDevice.Text = $"Device: {(_settings.DeviceCode.Length > 0 ? _settings.DeviceCode : "-")}";
    }

    private bool HasServerSettings() =>
        !string.IsNullOrWhiteSpace(_api.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_api.DeviceCode) &&
        !string.IsNullOrWhiteSpace(_api.DeviceSecret);

    private async Task RefreshDashboardAsync()
    {
        if (_store is null) return;
        PendingCountText.Text = (await _store.CountPendingAsync()).ToString();
        PortText.Text = _serial?.IsConnected == true ? _serial.ConnectedPort : (_settings.ComPort.Length > 0 ? _settings.ComPort : "-");
        SerialBadge.Text = _serial?.IsConnected == true ? "SERIAL ONLINE" : "SERIAL OFFLINE";
        SerialBadge.Foreground = (System.Windows.Media.Brush)FindResource(_serial?.IsConnected == true ? "Good" : "Bad");
    }

    private async Task RefreshTransactionsAsync()
    {
        if (_store is null) return;
        var items = await _store.GetRecentAsync();
        _events.Clear();
        foreach (var x in items) _events.Add(x);
    }

    private void LoadSettingsIntoUi()
    {
        ServerUrlBox.Text = _settings.ServerUrl;
        DeviceCodeBox.Text = _settings.DeviceCode;
        BaudBox.Text = _settings.BaudRate.ToString();
        try { DeviceSecretBox.Password = _secretStore.Load(_settingsService.SecretPath); } catch { DeviceSecretBox.Password = ""; }
    }

    private void RefreshPorts()
    {
        var ports = SerialDeviceService.GetPorts();
        ComPortBox.ItemsSource = ports;
        if (!string.IsNullOrWhiteSpace(_settings.ComPort) && ports.Contains(_settings.ComPort))
        {
            ComPortBox.SelectedItem = _settings.ComPort;
        }
        else if (ports.Length > 0)
        {
            ComPortBox.SelectedIndex = 0;
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BaudBox.Text.Trim(), out var baud) || baud <= 0) baud = 115200;
        _settings.ServerUrl = ServerUrlBox.Text.Trim();
        _settings.DeviceCode = DeviceCodeBox.Text.Trim();
        _settings.ComPort = ComPortBox.SelectedItem?.ToString() ?? ComPortBox.Text.Trim();
        _settings.BaudRate = baud;
        await _settingsService.SaveAsync(_settings);
        if (!string.IsNullOrWhiteSpace(DeviceSecretBox.Password))
        {
            _secretStore.Save(_settingsService.SecretPath, DeviceSecretBox.Password);
        }
        ConfigureApi();
        _log.Info("Settings saved");
        MessageBox.Show("تنظیمات ذخیره شد.", "HiMate", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _api.BaseUrl = ServerUrlBox.Text.Trim();
            var ping = await _api.PingAsync();
            CoreVersionText.Text = ping.Version;
            ServerBadge.Text = "SERVER ONLINE";
            ServerBadge.Foreground = (System.Windows.Media.Brush)FindResource("Good");
            _log.Info($"Server ping OK: Core {ping.Version}");
        }
        catch (Exception ex)
        {
            ServerBadge.Text = "SERVER ERROR";
            ServerBadge.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            _log.Error($"Server ping failed: {ex.Message}");
        }
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serial.IsConnected)
            {
                _serial.Disconnect();
                ConnectButton.Content = "اتصال دستگاه";
            }
            else
            {
                var port = ComPortBox.SelectedItem?.ToString() ?? _settings.ComPort;
                if (string.IsNullOrWhiteSpace(port)) throw new InvalidOperationException("COM port را انتخاب کنید.");
                _serial.Connect(port, _settings.BaudRate);
                ConnectButton.Content = "قطع اتصال";
                _serial.Send("GETDEBIT");
                _serial.Send("EVENTS");
            }
            _ = RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"Serial connection failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Serial", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigureApi();
            if (!HasServerSettings()) throw new InvalidOperationException("Server URL, Device Code و Device Secret را تنظیم کنید.");
            var ping = await _api.PingAsync();
            CoreVersionText.Text = ping.Version;
            ServerBadge.Text = "SERVER ONLINE";
            ServerBadge.Foreground = (System.Windows.Media.Brush)FindResource("Good");
            await _sync.SyncOnceAsync();
            await RefreshDashboardAsync();
            await RefreshTransactionsAsync();
        }
        catch (Exception ex)
        {
            ServerBadge.Text = "SERVER ERROR";
            ServerBadge.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            _log.Error($"Manual sync failed: {ex.Message}");
        }
    }

    private async void RefreshCommands_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigureApi();
            await _api.PingAsync();
            var response = await _api.GetCommandsAsync();
            _commands.Clear();
            foreach (var cmd in response.Commands) _commands.Add(cmd);
            _log.Info($"Commands loaded: {_commands.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"Get commands failed: {ex.Message}");
        }
    }

    private void SendDevice(string cmd)
    {
        try { _serial.Send(cmd); }
        catch (Exception ex) { _log.Error(ex.Message); }
    }

    private void RequestEvents_Click(object sender, RoutedEventArgs e) => SendDevice("EVENTS");
    private void GetDebit_Click(object sender, RoutedEventArgs e) => SendDevice("GETDEBIT");
    private void Status_Click(object sender, RoutedEventArgs e) => SendDevice("STATUS");
    private void Balance_Click(object sender, RoutedEventArgs e) => SendDevice("BALANCE");
    private void RefreshTransactions_Click(object sender, RoutedEventArgs e) => _ = RefreshTransactionsAsync();
    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;
    private void ShowTransactions_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void ShowCommands_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 3;
    private void ShowLogs_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 4;

    private void ShutdownServices()
    {
        try { _cts.Cancel(); } catch { }
        try { _serial?.Dispose(); } catch { }
        _cts.Dispose();
    }
}

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    // Coalesce bursts of card events into a single pending immediate sync wake-up.
    private readonly SemaphoreSlim _realtimeSyncSignal = new(0, 1);
    private readonly ObservableCollection<CardEvent> _events = [];
    private readonly ObservableCollection<ServerCommand> _commands = [];
    private readonly ObservableCollection<UserSummary> _userResults = [];

    private string _serverVersion = "-";
    private DateTime? _lastSyncAt;

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
            CustomerResultsBox.ItemsSource = _userResults;

            LoadSettingsIntoUi();
            RefreshPorts();
            ConfigureApiFromSettings();

            SetActiveNav(NavHome, 0);
            ApplyDeviceState(false, "در انتظار اتصال");
            ApplyServerState(false, HasServerSettings() ? "در حال بررسی" : "تنظیم نشده", unknown: true);

            if (_settings.AutoConnect)
            {
                TryConnectSavedDevice(quiet: true);
            }

            if (HasServerSettings())
            {
                await TryPingServerAsync(quiet: true);
            }

            await RefreshAllUiAsync();

            _log.Info("HiMate Credit ready");
            _ = Task.Run(() => RealtimeSyncLoopAsync(_cts.Token));
            _ = BackgroundLoopAsync(_cts.Token);
            // Upload any durable unsent events from a previous session immediately.
            RequestRealtimeSync();
        }
        catch (Exception ex)
        {
            _log.Error($"Startup failed: {ex.Message}");
            MessageBox.Show(ex.Message, "HiMate Credit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task BackgroundLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_settings.AutoConnect && _serial?.IsConnected != true)
                {
                    TryConnectSavedDevice(quiet: true);
                }

                if (HasServerSettings())
                {
                    var online = await TryPingServerAsync(quiet: true, ct);
                    if (online)
                    {
                        var synced = await _sync.SyncOnceAsync(ct);
                        if (synced > 0)
                        {
                            _lastSyncAt = DateTime.Now;
                        }
                    }
                }

                await RefreshUiFromAnyThreadAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warn($"Background task: {ex.Message}");
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

    // Wait for new durable events without polling the server every time.
    // This worker never runs inside the serial callback, so offline card reads
    // are never blocked by network latency or an unavailable WordPress server.
    private async Task RealtimeSyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _realtimeSyncSignal.WaitAsync(ct);
                if (!HasServerSettings()) continue;

                var synced = await _sync.SyncOnceAsync(ct);
                if (synced > 0)
                {
                    _lastSyncAt = DateTime.Now;
                    await RefreshUiFromAnyThreadAsync();
                }

                // SyncService sends at most 100 events per request; drain large
                // batches without waiting for the periodic recovery scan.
                if (synced == 100) RequestRealtimeSync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warn($"Immediate event sync deferred: {ex.Message}");
                // Failed items remain in SQLite; periodic background retry handles them.
            }
        }
    }

    private void RequestRealtimeSync()
    {
        try
        {
            _realtimeSyncSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another wake-up is already pending; this event is in durable SQLite.
        }
    }

    private async void Serial_LineReceived(string line)
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                LatestDeviceResponseText.Text = line;
                UpdateDeviceResponseUi(line);
            });

            if (!DeviceProtocolParser.TryParseCardEvent(line, out var cardEvent))
            {
                return;
            }

            var result = await _store.SaveFromDeviceAsync(cardEvent);
            if (result == SaveEventResult.Conflict)
            {
                _log.Error($"LOCAL EVENT CONFLICT: {cardEvent.EventKey}; device ACK withheld");
                return;
            }

            // Trigger server upload immediately *after* the SQLite transaction
            // commits. Do not await the network or depend on a device ACK.
            RequestRealtimeSync();

            _serial.Send($"EVENT_ACK|ID={cardEvent.DeviceEventId}");
            await _store.MarkDeviceAckedAsync(cardEvent);
            _log.Info($"Event saved locally: ID={cardEvent.DeviceEventId} {cardEvent.Type} UID={cardEvent.Uid}");

            if (cardEvent.Type.Equals("ADD", StringComparison.OrdinalIgnoreCase))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    TopupFeedbackText.Text = $"شارژ ثبت شد؛ مانده کارت: {cardEvent.Remaining}";
                    TopupFeedbackText.Foreground = Brush("Good");
                });
            }

            await RefreshUiFromAnyThreadAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"Event processing failed: {ex.Message}");
        }
    }

    private void UpdateDeviceResponseUi(string line)
    {
        if (line.StartsWith("DEFAULT_DEBIT", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(line, @"AMOUNT\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(line, @"(\d+)\s*$");
            }

            if (match.Success)
            {
                CurrentDebitText.Text = $"مقدار فعلی دستگاه: {match.Groups[1].Value}";
                CurrentDebitText.Foreground = Brush("Muted");
            }
        }

        // STATUS/BALANCE prints a dedicated UID line and never deducts credit.
        var uidMatch = Regex.Match(line, @"^\s*UID\s*:\s*([0-9A-Fa-f:\- ]+)\s*$", RegexOptions.IgnoreCase);
        if (uidMatch.Success)
        {
            var uid = uidMatch.Groups[1].Value.Trim().ToUpperInvariant();
            AssignCardUidBox.Text = uid;
            AssignmentFeedbackText.Text = "UID کارت خوانده شد؛ در حال بررسی مالک روی سرور...";
            AssignmentFeedbackText.Foreground = Brush("Accent");
            _ = LookupCardOwnerAsync(uid);
        }
    }

    private void LoadSettingsIntoUi()
    {
        ServerUrlBox.Text = _settings.ServerUrl;
        DeviceCodeBox.Text = _settings.DeviceCode;
        BaudBox.Text = _settings.BaudRate.ToString();
        AutoConnectCheck.IsChecked = _settings.AutoConnect;

        try
        {
            DeviceSecretBox.Password = _secretStore.Load(_settingsService.SecretPath);
        }
        catch
        {
            DeviceSecretBox.Password = "";
        }
    }

    private void ConfigureApiFromSettings()
    {
        _api.BaseUrl = _settings.ServerUrl;
        _api.DeviceCode = _settings.DeviceCode;

        try
        {
            _api.DeviceSecret = _secretStore.Load(_settingsService.SecretPath);
        }
        catch (Exception ex)
        {
            _api.DeviceSecret = "";
            _log.Warn($"Could not load protected secret: {ex.Message}");
        }
    }

    private void ConfigureApiFromUi()
    {
        _api.BaseUrl = ServerUrlBox.Text.Trim();
        _api.DeviceCode = DeviceCodeBox.Text.Trim();
        _api.DeviceSecret = DeviceSecretBox.Password;
    }

    private bool HasServerSettings() =>
        !string.IsNullOrWhiteSpace(_api.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_api.DeviceCode) &&
        !string.IsNullOrWhiteSpace(_api.DeviceSecret);

    private void RefreshPorts()
    {
        var ports = SerialDeviceService.GetPorts();
        ComPortBox.ItemsSource = ports;

        if (ports.Length == 0)
        {
            ComPortBox.SelectedIndex = -1;
            DeviceTestFeedbackText.Text = "ویندوز هیچ پورت COM فعالی گزارش نکرد. اگر دستگاه وصل است، درایور USB-Serial را بررسی کنید.";
            DeviceTestFeedbackText.Foreground = Brush("Warn");
            return;
        }

        DeviceTestFeedbackText.Text = $"پورت‌های شناسایی‌شده: {string.Join("، ", ports)}";
        DeviceTestFeedbackText.Foreground = Brush("Muted");

        if (!string.IsNullOrWhiteSpace(_settings.ComPort) && ports.Contains(_settings.ComPort))
        {
            ComPortBox.SelectedItem = _settings.ComPort;
        }
        else if (ports.Length == 1)
        {
            ComPortBox.SelectedIndex = 0;
        }
        else
        {
            ComPortBox.SelectedIndex = 0;
        }
    }

    private bool TryConnectSavedDevice(bool quiet)
    {
        if (_serial.IsConnected)
        {
            ApplyDeviceState(true, _serial.ConnectedPort);
            return true;
        }

        var ports = SerialDeviceService.GetPorts();
        var port = _settings.ComPort;

        if (ports.Length == 0)
        {
            ApplyDeviceState(false, "هیچ COM پیدا نشد");
            if (!quiet)
            {
                DeviceTestFeedbackText.Text = "ویندوز هیچ پورت COM فعالی گزارش نکرد. کابل/درایور USB-Serial را بررسی کنید.";
                DeviceTestFeedbackText.Foreground = Brush("Warn");
            }
            return false;
        }

        if (string.IsNullOrWhiteSpace(port) || !ports.Contains(port))
        {
            if (ports.Length == 1)
            {
                port = ports[0];
                _settings.ComPort = port;
                ComPortBox.SelectedItem = port;
                _ = _settingsService.SaveAsync(_settings);
                _log.Info($"Auto-selected serial port: {port}");
            }
            else
            {
                ApplyDeviceState(false, string.IsNullOrWhiteSpace(port) ? "پورت تنظیم نشده" : $"پورت {port} پیدا نشد");
                if (!quiet)
                {
                    DeviceTestFeedbackText.Text = $"چند پورت پیدا شد: {string.Join("، ", ports)}. پورت دستگاه را انتخاب کنید.";
                    DeviceTestFeedbackText.Foreground = Brush("Warn");
                }
                return false;
            }
        }

        try
        {
            _serial.Connect(port, _settings.BaudRate);
            _serial.Send("GETDEBIT");
            _serial.Send("EVENTS");

            ApplyDeviceState(true, port);
            DeviceTestFeedbackText.Text = $"اتصال با {port} برقرار شد.";
            DeviceTestFeedbackText.Foreground = Brush("Good");
            return true;
        }
        catch (Exception ex)
        {
            ApplyDeviceState(false, "خطای اتصال");
            DeviceTestFeedbackText.Text = ex.Message;
            DeviceTestFeedbackText.Foreground = Brush("Bad");
            _log.Warn($"Serial auto-connect failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryPingServerAsync(bool quiet, CancellationToken ct = default)
    {
        if (!HasServerSettings())
        {
            ApplyServerState(false, "تنظیم نشده", unknown: true);
            if (!quiet)
            {
                ServerTestFeedbackText.Text = "Server URL، Device Code و Device Secret را کامل کنید.";
                ServerTestFeedbackText.Foreground = Brush("Warn");
            }
            return false;
        }

        try
        {
            var ping = await _api.PingAsync(ct);
            _serverVersion = string.IsNullOrWhiteSpace(ping.Version) ? "-" : ping.Version;
            ApplyServerState(true, $"Core {_serverVersion}");

            if (!quiet)
            {
                ServerTestFeedbackText.Text = $"سرور در دسترس است — HiMate Core {_serverVersion}";
                ServerTestFeedbackText.Foreground = Brush("Good");
            }

            _log.Info($"Server ping OK: Core {_serverVersion}");
            return true;
        }
        catch (Exception ex)
        {
            ApplyServerState(false, "خطای ارتباط");
            if (!quiet)
            {
                ServerTestFeedbackText.Text = ex.Message;
                ServerTestFeedbackText.Foreground = Brush("Bad");
            }
            _log.Warn($"Server ping failed: {ex.Message}");
            return false;
        }
    }

    private void ApplyDeviceState(bool connected, string detail)
    {
        var brush = Brush(connected ? "Good" : "Bad");
        DeviceDot.Fill = brush;
        HeaderDeviceState.Text = connected ? "دستگاه متصل" : "دستگاه قطع";
        HomeDeviceState.Text = connected ? "متصل" : "در انتظار اتصال";
        HomeDeviceState.Foreground = brush;
        HomeDeviceSub.Text = detail;
        SupportDeviceText.Text = connected ? $"متصل — {detail}" : detail;
        SupportDeviceText.Foreground = brush;
        BottomDeviceText.Text = connected ? $"دستگاه متصل: {detail}" : $"دستگاه متصل نیست — {detail}";
        BottomDeviceText.Foreground = connected ? Brush("Good") : Brush("Muted");
    }

    private void ApplyServerState(bool online, string detail, bool unknown = false)
    {
        var brush = unknown ? Brush("Warn") : Brush(online ? "Good" : "Bad");

        ServerDot.Fill = brush;
        HeaderServerState.Text = online ? "سرور متصل" : (unknown ? "سرور نامشخص" : "سرور قطع");
        HomeServerState.Text = online ? $"وصل — {detail}" : detail;
        HomeServerState.Foreground = brush;
        SyncServerStateText.Text = online ? $"متصل — {detail}" : detail;
        SyncServerStateText.Foreground = brush;
        SupportServerText.Text = online ? $"متصل — {detail}" : detail;
        SupportServerText.Foreground = brush;
        CoreVersionText.Text = _serverVersion;
    }

    private async Task RefreshDashboardAsync()
    {
        if (_store is null) return;

        var pending = await _store.CountPendingAsync();
        var problems = await _store.CountProblemsAsync();
        var recent = await _store.GetRecentAsync(1);

        PendingCountText.Text = pending.ToString();
        ProblemCountText.Text = problems.ToString();
        SyncPendingText.Text = pending.ToString();
        SyncProblemText.Text = problems.ToString();

        HomeDeviceCode.Text = string.IsNullOrWhiteSpace(_settings.DeviceCode) ? "-" : _settings.DeviceCode;
        HomePortText.Text = _serial.IsConnected ? _serial.ConnectedPort : (string.IsNullOrWhiteSpace(_settings.ComPort) ? "-" : _settings.ComPort);
        HomeLastSyncText.Text = _lastSyncAt.HasValue ? _lastSyncAt.Value.ToString("HH:mm:ss") : "-";

        if (recent.Count == 0)
        {
            LastEventText.Text = "هنوز رویدادی ثبت نشده است.";
            CardDetailsText.Text = "اطلاعات آخرین کارت پس از دریافت رویداد در این بخش نمایش داده می‌شود.";
            CardPromptText.Text = "کارت را روی دستگاه قرار دهید.";
            return;
        }

        var e = recent[0];
        LastEventText.Text = $"UID: {e.Uid}   |   {TranslateType(e.Type)}   |   مقدار: {e.Amount}   |   مانده: {e.Remaining}   |   {TranslateSync(e.SyncStatus)}";
        CardPromptText.Text = e.Uid;
        CardDetailsText.Text = $"اعتبار کل: {e.Total}     مانده: {e.Remaining}     TX: {e.Tx}     GEN: {e.Gen}     SEQ: {e.Seq}";
    }

    private async Task RefreshTransactionsAsync()
    {
        if (_store is null) return;

        var items = await _store.GetRecentAsync();
        _events.Clear();
        foreach (var item in items)
        {
            _events.Add(item);
        }
    }

    private async Task RefreshAllUiAsync()
    {
        await RefreshDashboardAsync();
        await RefreshTransactionsAsync();
    }

    private Task RefreshUiFromAnyThreadAsync()
    {
        if (Dispatcher.CheckAccess())
        {
            return RefreshAllUiAsync();
        }

        var op = Dispatcher.InvokeAsync(() => RefreshAllUiAsync());
        return op.Task.Unwrap();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(BaudBox.Text.Trim(), out var baud) || baud <= 0)
            {
                baud = 115200;
                BaudBox.Text = "115200";
            }

            var oldPort = _settings.ComPort;
            var oldBaud = _settings.BaudRate;

            _settings.ServerUrl = ServerUrlBox.Text.Trim();
            _settings.DeviceCode = DeviceCodeBox.Text.Trim();
            _settings.ComPort = ComPortBox.SelectedItem?.ToString() ?? ComPortBox.Text.Trim();
            _settings.BaudRate = baud;
            _settings.AutoConnect = AutoConnectCheck.IsChecked == true;

            await _settingsService.SaveAsync(_settings);

            if (!string.IsNullOrWhiteSpace(DeviceSecretBox.Password))
            {
                _secretStore.Save(_settingsService.SecretPath, DeviceSecretBox.Password);
            }

            ConfigureApiFromSettings();
            // Send any events accumulated before server setup completed.
            RequestRealtimeSync();

            if (_serial.IsConnected && (!string.Equals(oldPort, _settings.ComPort, StringComparison.OrdinalIgnoreCase) || oldBaud != _settings.BaudRate))
            {
                _serial.Disconnect();
            }

            if (_settings.AutoConnect && !_serial.IsConnected)
            {
                TryConnectSavedDevice(quiet: true);
            }

            SettingsFeedbackText.Text = "تنظیمات ذخیره شد.";
            SettingsFeedbackText.Foreground = Brush("Good");
            _log.Info("Settings saved");

            if (HasServerSettings())
            {
                await TryPingServerAsync(quiet: true);
            }

            await RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            SettingsFeedbackText.Text = ex.Message;
            SettingsFeedbackText.Foreground = Brush("Bad");
            _log.Error($"Save settings failed: {ex.Message}");
        }
    }

    private void TestDevice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(BaudBox.Text.Trim(), out var baud) || baud <= 0)
            {
                baud = 115200;
            }

            var port = ComPortBox.SelectedItem?.ToString() ?? ComPortBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(port))
            {
                DeviceTestFeedbackText.Text = "پورت دستگاه را انتخاب کنید.";
                DeviceTestFeedbackText.Foreground = Brush("Warn");
                return;
            }

            if (_serial.IsConnected)
            {
                _serial.Disconnect();
            }

            _serial.Connect(port, baud);
            _serial.Send("GETDEBIT");
            _serial.Send("EVENTS");

            ApplyDeviceState(true, port);
            DeviceTestFeedbackText.Text = $"اتصال با {port} موفق بود.";
            DeviceTestFeedbackText.Foreground = Brush("Good");
        }
        catch (Exception ex)
        {
            ApplyDeviceState(false, "خطای اتصال");
            DeviceTestFeedbackText.Text = ex.Message;
            DeviceTestFeedbackText.Foreground = Brush("Bad");
            _log.Warn($"Serial test failed: {ex.Message}");
        }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        ConfigureApiFromUi();
        await TryPingServerAsync(quiet: false);
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigureApiFromSettings();

            if (!HasServerSettings())
            {
                SetActiveNav(NavSettings, 5);
                SettingsFeedbackText.Text = "ابتدا تنظیمات سرور را تکمیل و ذخیره کنید.";
                SettingsFeedbackText.Foreground = Brush("Warn");
                return;
            }

            if (!await TryPingServerAsync(quiet: true))
            {
                return;
            }

            var synced = await _sync.SyncOnceAsync();
            if (synced > 0)
            {
                _lastSyncAt = DateTime.Now;
            }
            await RefreshAllUiAsync();
        }
        catch (Exception ex)
        {
            ApplyServerState(false, "خطای Sync");
            _log.Error($"Manual sync failed: {ex.Message}");
        }
    }

    private async void RefreshCommands_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigureApiFromSettings();
            if (!HasServerSettings())
            {
                throw new InvalidOperationException("تنظیمات سرور کامل نیست.");
            }

            var ping = await _api.PingAsync();
            _serverVersion = string.IsNullOrWhiteSpace(ping.Version) ? "-" : ping.Version;

            var response = await _api.GetCommandsAsync();
            _commands.Clear();
            foreach (var cmd in response.Commands)
            {
                _commands.Add(cmd);
            }

            ApplyServerState(true, $"Core {_serverVersion}");
            _log.Info($"Commands loaded: {_commands.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"Get commands failed: {ex.Message}");
            ApplyServerState(false, "خطای دریافت دستور");
        }
    }

    private void StartTopup_Click(object sender, RoutedEventArgs e)
    {
        if (!_serial.IsConnected)
        {
            TopupFeedbackText.Text = "دستگاه متصل نیست. اتصال را از تنظیمات بررسی کنید.";
            TopupFeedbackText.Foreground = Brush("Bad");
            return;
        }

        if (!int.TryParse(TopupAmountBox.Text.Trim(), out var amount) || amount <= 0 || amount > 65535)
        {
            TopupFeedbackText.Text = "مبلغ شارژ معتبر نیست.";
            TopupFeedbackText.Foreground = Brush("Warn");
            return;
        }

        try
        {
            _serial.Send($"CREDIT {amount}");
            TopupFeedbackText.Text = $"آماده شارژ {amount} اعتبار؛ کارت را روی دستگاه قرار دهید.";
            TopupFeedbackText.Foreground = Brush("Accent");
        }
        catch (Exception ex)
        {
            TopupFeedbackText.Text = ex.Message;
            TopupFeedbackText.Foreground = Brush("Bad");
        }
    }

    private void SetDebit_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DefaultDebitBox.Text.Trim(), out var amount) || amount <= 0 || amount > 65535)
        {
            CurrentDebitText.Text = "مقدار برداشت معتبر نیست.";
            CurrentDebitText.Foreground = Brush("Warn");
            return;
        }

        try
        {
            _serial.Send($"SETDEBIT {amount}");
            _serial.Send("GETDEBIT");
            CurrentDebitText.Text = "درخواست ذخیره مقدار برداشت ارسال شد.";
            CurrentDebitText.Foreground = Brush("Accent");
        }
        catch (Exception ex)
        {
            CurrentDebitText.Text = ex.Message;
            CurrentDebitText.Foreground = Brush("Bad");
        }
    }

    private void SendDevice(string command)
    {
        try
        {
            if (!_serial.IsConnected)
            {
                throw new InvalidOperationException("دستگاه متصل نیست.");
            }

            _serial.Send(command);
        }
        catch (Exception ex)
        {
            _log.Warn(ex.Message);
            LatestDeviceResponseText.Text = ex.Message;
        }
    }

    private async Task LookupCardOwnerAsync(string uid)
    {
        try
        {
            ConfigureApiFromSettings();
            if (!HasServerSettings())
            {
                CardOwnerText.Text = "مالک فعلی: سرور تنظیم نشده";
                return;
            }

            await _api.PingAsync();
            var response = await _api.GetCardAsync(uid);
            if (!response.Found || response.Card is null)
            {
                CardOwnerText.Text = "مالک فعلی: ثبت نشده";
                AssignmentFeedbackText.Text = "این UID هنوز به مشتری متصل نشده است.";
                AssignmentFeedbackText.Foreground = Brush("Warn");
                return;
            }

            var owner = response.Card.Owner;
            CardOwnerText.Text = owner is null
                ? $"مالک فعلی: بدون مالک — مانده {response.Card.Remaining}"
                : $"مالک فعلی: {owner.Name} — {owner.Phone} — مانده {response.Card.Remaining}";
            AssignmentFeedbackText.Text = owner is null ? "کارت در سرور شناخته شده ولی بدون مالک است." : "اطلاعات مالک کارت از سرور دریافت شد.";
            AssignmentFeedbackText.Foreground = owner is null ? Brush("Warn") : Brush("Good");
        }
        catch (Exception ex)
        {
            CardOwnerText.Text = "مالک فعلی: خطا در دریافت";
            AssignmentFeedbackText.Text = ex.Message;
            AssignmentFeedbackText.Foreground = Brush("Bad");
            _log.Warn($"Card lookup failed: {ex.Message}");
        }
    }

    private void ReadCardForAssign_Click(object sender, RoutedEventArgs e)
    {
        AssignCardUidBox.Text = "";
        CardOwnerText.Text = "مالک فعلی: -";
        AssignmentFeedbackText.Text = "کارت را روی دستگاه قرار دهید؛ این عملیات اعتبار کم نمی‌کند.";
        AssignmentFeedbackText.Foreground = Brush("Accent");
        SendDevice("STATUS");
    }

    private async void SearchCustomers_Click(object sender, RoutedEventArgs e)
    {
        var q = CustomerSearchBox.Text.Trim();
        if (q.Length < 2)
        {
            AssignmentFeedbackText.Text = "حداقل ۲ کاراکتر برای جستجوی مشتری وارد کنید.";
            AssignmentFeedbackText.Foreground = Brush("Warn");
            return;
        }

        try
        {
            ConfigureApiFromSettings();
            if (!HasServerSettings()) throw new InvalidOperationException("تنظیمات سرور کامل نیست.");

            await _api.PingAsync();
            var response = await _api.SearchUsersAsync(q);
            _userResults.Clear();
            foreach (var user in response.Users) _userResults.Add(user);
            if (_userResults.Count > 0) CustomerResultsBox.SelectedIndex = 0;

            AssignmentFeedbackText.Text = _userResults.Count == 0
                ? "مشتری مطابق جستجو پیدا نشد."
                : $"{_userResults.Count} مشتری پیدا شد.";
            AssignmentFeedbackText.Foreground = _userResults.Count == 0 ? Brush("Warn") : Brush("Good");
        }
        catch (Exception ex)
        {
            AssignmentFeedbackText.Text = ex.Message;
            AssignmentFeedbackText.Foreground = Brush("Bad");
            _log.Warn($"User search failed: {ex.Message}");
        }
    }

    private async void AssignCard_Click(object sender, RoutedEventArgs e)
    {
        var uid = AssignCardUidBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            AssignmentFeedbackText.Text = "ابتدا کارت را با دکمه «خواندن کارت بدون برداشت» بخوانید.";
            AssignmentFeedbackText.Foreground = Brush("Warn");
            return;
        }

        if (CustomerResultsBox.SelectedItem is not UserSummary user)
        {
            AssignmentFeedbackText.Text = "ابتدا مشتری را جستجو و انتخاب کنید.";
            AssignmentFeedbackText.Foreground = Brush("Warn");
            return;
        }

        try
        {
            ConfigureApiFromSettings();
            if (!HasServerSettings()) throw new InvalidOperationException("تنظیمات سرور کامل نیست.");

            await _api.PingAsync();
            var lookup = await _api.GetCardAsync(uid);
            var force = false;

            if (lookup.Found && lookup.Card?.Owner is not null && lookup.Card.Owner.Id != user.Id)
            {
                var old = lookup.Card.Owner;
                var answer = MessageBox.Show(
                    $"این کارت اکنون متعلق به «{old.Name}» است.\n\nکارت از مالک قبلی جدا و به «{user.Name}» منتقل شود؟",
                    "تغییر مالک کارت",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes)
                {
                    AssignmentFeedbackText.Text = "تغییر مالک لغو شد.";
                    AssignmentFeedbackText.Foreground = Brush("Warn");
                    return;
                }
                force = true;
            }

            var result = await _api.AssignCardAsync(uid, user.Id, force);
            if (!result.Success || result.Card is null) throw new InvalidOperationException("سرور ثبت کارت را تأیید نکرد.");

            CardOwnerText.Text = $"مالک فعلی: {user.Name} — {user.Phone} — مانده {result.Card.Remaining}";
            AssignmentFeedbackText.Text = $"کارت {result.Card.Uid} با موفقیت برای {user.Name} ثبت شد.";
            AssignmentFeedbackText.Foreground = Brush("Good");
            _log.Info($"Card assigned: UID={result.Card.Uid} USER={user.Id}");
        }
        catch (Exception ex)
        {
            AssignmentFeedbackText.Text = ex.Message;
            AssignmentFeedbackText.Foreground = Brush("Bad");
            _log.Error($"Card assignment failed: {ex.Message}");
        }
    }

    private async void UnassignCard_Click(object sender, RoutedEventArgs e)
    {
        var uid = AssignCardUidBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            AssignmentFeedbackText.Text = "ابتدا کارت را بخوانید.";
            AssignmentFeedbackText.Foreground = Brush("Warn");
            return;
        }

        if (MessageBox.Show("اتصال این کارت به مشتری قطع شود؟ خود اعتبار فیزیکی کارت پاک نمی‌شود.",
                "قطع اتصال کارت", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ConfigureApiFromSettings();
            if (!HasServerSettings()) throw new InvalidOperationException("تنظیمات سرور کامل نیست.");
            await _api.PingAsync();
            var result = await _api.UnassignCardAsync(uid);
            if (!result.Success) throw new InvalidOperationException("سرور قطع اتصال را تأیید نکرد.");

            CardOwnerText.Text = "مالک فعلی: بدون مالک";
            AssignmentFeedbackText.Text = "اتصال کارت به مشتری قطع شد؛ اعتبار روی خود کارت تغییری نکرد.";
            AssignmentFeedbackText.Foreground = Brush("Good");
            _log.Info($"Card unassigned: UID={uid}");
        }
        catch (Exception ex)
        {
            AssignmentFeedbackText.Text = ex.Message;
            AssignmentFeedbackText.Foreground = Brush("Bad");
            _log.Error($"Card unassign failed: {ex.Message}");
        }
    }

    private void Balance_Click(object sender, RoutedEventArgs e) => SendDevice("BALANCE");
    private void Status_Click(object sender, RoutedEventArgs e) => SendDevice("STATUS");
    private void RefreshTransactions_Click(object sender, RoutedEventArgs e) => _ = RefreshTransactionsAsync();
    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void ComPortBox_DropDownOpened(object sender, EventArgs e) => RefreshPorts();

    private void MemStatus_Click(object sender, RoutedEventArgs e) => SendDevice("MEMSTATUS");
    private void SyncLedger_Click(object sender, RoutedEventArgs e) => SendDevice("SYNCLEDGER");
    private void Reboot_Click(object sender, RoutedEventArgs e) => ConfirmAndSend("REBOOT", "دستگاه راه‌اندازی مجدد شود؟");
    private void ResetEventId_Click(object sender, RoutedEventArgs e) => ConfirmAndSend("RESET_EVENT_ID CONFIRM", "Event ID دستگاه ریست شود؟ این عملیات فقط وقتی صف دستگاه خالی است باید انجام شود.");
    private void ClearEvents_Click(object sender, RoutedEventArgs e) => ConfirmAndSend("CLEAR EVENTS", "صف رویدادهای دستگاه پاک شود؟");
    private void ClearRuntime_Click(object sender, RoutedEventArgs e) => ConfirmAndSend("CLEAR RUNTIME", "اطلاعات Runtime دستگاه پاک شود؟");
    private void ClearMemory_Click(object sender, RoutedEventArgs e) => ConfirmAndSend("CLEAR MEMORY", "حافظه دستگاه پاک شود؟ این عملیات برگشت‌پذیر نیست.");

    private void ConfirmAndSend(string command, string message)
    {
        var result = MessageBox.Show(message, "HiMate Credit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            SendDevice(command);
        }
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavHome, 0);
    private void ShowCard_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavCard, 1);
    private void ShowTransactions_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavTransactions, 2);
    private void ShowTopup_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavTopup, 3);
    private void ShowSync_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavSync, 4);
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavSettings, 5);
    private void ShowSupport_Click(object sender, RoutedEventArgs e) => SetActiveNav(NavSupport, 6);

    private void SetActiveNav(Button active, int index)
    {
        foreach (var button in new[] { NavHome, NavCard, NavTransactions, NavTopup, NavSync, NavSettings, NavSupport })
        {
            button.Foreground = Brush("Text");
            button.BorderBrush = Brushes.Transparent;
        }

        active.Foreground = Brush("Accent");
        active.BorderBrush = Brush("Accent");
        MainTabs.SelectedIndex = index;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static string TranslateType(string type) => type.ToUpperInvariant() switch
    {
        "ISSUE" => "صدور",
        "ADD" => "شارژ",
        "DEBIT" => "برداشت",
        _ => type
    };

    private static string TranslateSync(string status) => status.ToUpperInvariant() switch
    {
        "SYNCED" => "همگام‌شده",
        "PENDING" => "در انتظار ارسال",
        "RETRY" => "تلاش مجدد",
        "CONFLICT" => "نیازمند بررسی",
        "INVALID" => "نامعتبر",
        _ => status
    };

    private void ShutdownServices()
    {
        try { _cts.Cancel(); } catch { }
        try { _serial?.Dispose(); } catch { }
        _cts.Dispose();
    }
}

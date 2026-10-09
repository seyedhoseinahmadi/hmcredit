using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using HiMate.Agent.Models;

namespace HiMate.Agent;

public partial class MainWindow
{
    private readonly ObservableCollection<OrderDebitRequest> _siteOrders = [];
    private readonly HashSet<long> _heldOrderCommands = [];
    private readonly DispatcherTimer _orderPoll = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _orderClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private OrderDebitRequest? _armedOrder;
    private DateTimeOffset? _orderDeadline;
    private bool _pollBusy, _serialHooked, _gotArmAck, _manualCreditArmed;

    private void OrderDebitController_Loaded(object sender, RoutedEventArgs e)
    {
        OrderDebitsGrid.ItemsSource = _siteOrders;
        _orderPoll.Tick += async (_, _) => await PollWebsiteOrdersAsync();
        _orderClock.Tick += (_, _) => UpdateOrderClock();
        _orderPoll.Start();
        _orderClock.Start();
        ManualTopupButton.Click += (_, _) =>
        {
            if (_serial?.IsConnected == true && _armedOrder is null &&
                int.TryParse(TopupAmountBox.Text, out int amount) && amount is > 0 and <= 65535)
                _manualCreditArmed = true;
        };
        Closing += (_, _) =>
        {
            _orderPoll.Stop(); _orderClock.Stop();
            if (_serialHooked && _serial is not null) _serial.LineReceived -= HandleWebsiteOrderSerial;
        };
    }

    private void UpdateOrderClock()
    {
        if (_armedOrder is null)
        {
            OrderDebitCountdownText.Text = "منتظر فرمان پرداخت از سایت...";
            return;
        }
        if (!_orderDeadline.HasValue)
        {
            OrderDebitCountdownText.Text = "در حال آماده‌سازی دستگاه...";
            return;
        }
        var left = _orderDeadline.Value - DateTimeOffset.UtcNow;
        OrderDebitCountdownText.Text = left > TimeSpan.Zero
            ? $"سفارش #{_armedOrder.OrderId} | انتظار کارت: {(int)left.TotalMinutes:00}:{left.Seconds:00}"
            : "منتظر نتیجه قطعی دستگاه...";
    }

    private void CancelManualTopup_Click(object sender, RoutedEventArgs e)
    {
        if (_manualCreditArmed && _serial?.IsConnected == true) _serial.Send("USE");
        _manualCreditArmed = false;
        TopupFeedbackText.Text = "انتظار شارژ دستی لغو شد.";
    }
}

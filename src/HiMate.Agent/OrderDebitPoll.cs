using HiMate.Agent.Models;
namespace HiMate.Agent;
public partial class MainWindow
{
    private async Task PollWebsiteOrdersAsync()
    {
        if (_pollBusy) return;
        _pollBusy = true;
        try
        {
            if (_serial is null || _store is null || _sync is null) return;
            if (!_serialHooked) { _serial.LineReceived += HandleWebsiteOrderSerial; _serialHooked = true; }
            if (!_serial.IsConnected || !HasServerSettings()) return;
            var response = await _api.GetPendingOrderDebitsAsync();
            _siteOrders.Clear();
            foreach (var item in response.Debits) _siteOrders.Add(item);
            if (_armedOrder is not null)
            {
                var current = await _api.GetOrderDebitAsync(_armedOrder.OrderId);
                if (current.Debit?.Status == "APPLIED")
                {
                    OrderDebitFeedbackText.Text = $"سفارش #{_armedOrder.OrderId} پرداخت شد.";
                    OrderDebitFeedbackText.Foreground = Brush("Good");
                    _armedOrder = null; _orderDeadline = null;
                }
                return;
            }
            await TryArmOrderAsync(response.Debits);
        }
        catch (Exception ex)
        {
            _log.Warn("Order queue: " + ex.Message);
            OrderDebitFeedbackText.Text = ex.Message;
            OrderDebitFeedbackText.Foreground = Brush("Warn");
        }
        finally { _pollBusy = false; }
    }
}

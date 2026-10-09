using HiMate.Agent.Protocol;
namespace HiMate.Agent;
public partial class MainWindow
{
    private void HandleWebsiteOrderSerial(string line)
    {
        _ = Dispatcher.InvokeAsync(() => _ = ProcessWebsiteOrderLineAsync(line));
    }

    private async Task ProcessWebsiteOrderLineAsync(string line)
    {
        if (DeviceProtocolParser.TryParseCardEvent(line, out var evt))
        {
            if (evt.Type == "ADD" && !evt.Cid.HasValue) _manualCreditArmed = false;
            if (evt.Type == "DEBIT" && evt.Cid.HasValue && evt.Oid.HasValue)
            {
                await Task.Delay(400);
                await ReconcileOrderDebitEventAsync(evt);
            }
            return;
        }
        if (!line.StartsWith("DEBITCMD_RESULT|", StringComparison.OrdinalIgnoreCase)) return;
        var fields = DeviceProtocolParser.ParseFields(line);
        if (!fields.TryGetValue("CID", out var textCid) || !long.TryParse(textCid, out var cid) ||
            _armedOrder?.Cid != cid) return;
        fields.TryGetValue("STATUS", out var status);
        fields.TryGetValue("REASON", out var reason);
        var oid = _armedOrder.OrderId;

        if (status == "ARMED")
        {
            _gotArmAck = true;
            _orderDeadline = DateTimeOffset.UtcNow.AddMinutes(5);
            OrderDebitFeedbackText.Text = $"سفارش #{oid} آماده خواندن کارت است.";
            OrderDebitFeedbackText.Foreground = Brush("Accent");
        }
        else if (status == "REJECTED" && reason == "UID_MISMATCH")
        {
            OrderDebitFeedbackText.Text = "کارت متعلق به این سفارش نیست؛ اعتبار کسر نشد.";
            OrderDebitFeedbackText.Foreground = Brush("Warn");
        }
        else if (status == "APPLIED" || status == "ALREADY_APPLIED")
        {
            _gotArmAck = true; _orderDeadline = null;
            OrderDebitFeedbackText.Text = "برداشت دستگاه گزارش شد؛ تا تأیید سایت کارت را دوباره نکشید.";
            OrderDebitFeedbackText.Foreground = Brush("Warn");
            _serial.Send("EVENTS");
            RequestRealtimeSync();
        }
        else if (status == "EXPIRED" || status == "REJECTED" || status == "INVALID_COMMAND")
        {
            await FinishWebsiteOrderWaitAsync(oid, cid, status, reason);
        }
    }
}

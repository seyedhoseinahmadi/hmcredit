using HiMate.Agent.Models;
namespace HiMate.Agent;
public partial class MainWindow
{
    private async Task TryArmOrderAsync(List<OrderDebitRequest> queue)
    {
        if (_manualCreditArmed || _activeServerTopup is not null ||
            _cardRegistrationStage is CardRegistrationStage.WaitingForCard
                or CardRegistrationStage.SelectingCustomer
                or CardRegistrationStage.ReadyToConfirm) return;

        var next = queue.FirstOrDefault(x =>
            (x.Status == "PENDING" || x.Status == "DELIVERED") &&
            !_heldOrderCommands.Contains(x.Cid));
        if (next is null) return;

        var claimed = await _api.ClaimOrderDebitAsync(next.OrderId);
        var d = claimed.Debit ?? throw new InvalidOperationException("اطلاعات فرمان ناقص است.");
        if (d.Cid <= 0 || d.OrderId <= 0 || d.Amount is < 1 or > 65535 || string.IsNullOrWhiteSpace(d.Uid))
            throw new InvalidOperationException("UID یا مقدار فرمان نامعتبر است.");

        _armedOrder = d; _gotArmAck = false; _orderDeadline = null;
        _serial.Send("USE");
        _serial.Send($"DEBITCMD|CID={d.Cid}|OID={d.OrderId}|UID={d.Uid}|AMOUNT={d.Amount}");
        OrderDebitFeedbackText.Text = $"سفارش #{d.OrderId}: کارت {d.Uid} متعلق به {d.CustomerName} را بگذارید.";
        OrderDebitFeedbackText.Foreground = Brush("Accent");
        _log.Info($"Order debit CID={d.Cid} OID={d.OrderId} UID={d.Uid}");
        _ = DebitArmTimeoutAsync(d.Cid);
    }

    private async Task DebitArmTimeoutAsync(long cid)
    {
        await Task.Delay(5000);
        if (_armedOrder?.Cid != cid || _gotArmAck) return;
        _heldOrderCommands.Add(cid);
        _armedOrder = null; _orderDeadline = null;
        OrderDebitFeedbackText.Text = "دستگاه پاسخ آماده‌بودن نداد؛ Firmware 9.5.15 را بررسی کنید.";
        OrderDebitFeedbackText.Foreground = Brush("Bad");
    }
}

using HiMate.Agent.Models;
namespace HiMate.Agent;
public partial class MainWindow
{
    private async Task FinishWebsiteOrderWaitAsync(long oid, long cid, string status, string? reason)
    {
        if (status == "EXPIRED")
        {
            try
            {
                await _api.MarkOrderDebitExpiredAsync(oid, cid);
                OrderDebitFeedbackText.Text = $"مهلت سفارش #{oid} تمام شد؛ با همان CID در سایت دوباره درخواست بدهید.";
                OrderDebitFeedbackText.Foreground = Brush("Warn");
            }
            catch (Exception ex)
            {
                _log.Warn("Order debit expiry server failed: " + ex.Message);
                OrderDebitFeedbackText.Text = "مهلت دستگاه تمام شد ولی تأیید سرور ناموفق بود؛ بررسی لازم است.";
                OrderDebitFeedbackText.Foreground = Brush("Bad");
            }
        }
        else
        {
            try { await _api.MarkOrderDebitReviewAsync(oid, cid, reason ?? status); }
            catch (Exception ex) { _log.Warn("Order debit review: " + ex.Message); }
            OrderDebitFeedbackText.Text = $"سفارش #{oid}: وضعیت برداشت نامطمئن ({reason ?? status}). بررسی کارت ضروری است.";
            OrderDebitFeedbackText.Foreground = Brush("Bad");
        }
        _heldOrderCommands.Add(cid);
        _armedOrder = null; _orderDeadline = null;
    }

    private async Task ReconcileOrderDebitEventAsync(CardEvent evt)
    {
        try
        {
            await _sync.SyncOnceAsync();
            var result = await _api.GetOrderDebitAsync(evt.Oid!.Value);
            if (result.Debit?.Status == "APPLIED")
            {
                if (_armedOrder?.Cid == evt.Cid)
                {
                    _armedOrder = null; _orderDeadline = null;
                }
                OrderDebitFeedbackText.Text = $"سفارش #{evt.Oid} با {evt.Amount} کردیت پرداخت و تأیید شد.";
                OrderDebitFeedbackText.Foreground = Brush("Good");
            }
            else
            {
                OrderDebitFeedbackText.Text = "برداشت ثبت شده ولی سایت هنوز آن را تأیید نکرد؛ وضعیت Sync را بررسی کنید.";
                OrderDebitFeedbackText.Foreground = Brush("Warn");
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Order debit sync retry: " + ex.Message);
            OrderDebitFeedbackText.Text = "ممکن است برداشت انجام شده باشد؛ دوباره کارت نکشید. Event برای Sync ذخیره است.";
            OrderDebitFeedbackText.Foreground = Brush("Warn");
        }
    }
}

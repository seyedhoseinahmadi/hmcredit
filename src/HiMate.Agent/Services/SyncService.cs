using HiMate.Agent.Models;

namespace HiMate.Agent.Services;

public sealed class SyncService
{
    private readonly EventStore _store;
    private readonly HiMateApiClient _api;
    private readonly LogService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(EventStore store, HiMateApiClient api, LogService log)
    {
        _store = store;
        _api = api;
        _log = log;
    }

    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        // Never silently drop an immediate wake-up while another sync is running.
        // Serialize uploads so retries and event-triggered sends cannot race.
        await _gate.WaitAsync(ct);
        try
        {
            var pending = await _store.GetPendingAsync(100);
            if (pending.Count == 0) return 0;

            EventBatchResponse response;
            try
            {
                // Refresh server clock offset before authenticated requests.
                // HiMate Core 2.2 rejects timestamps outside ±300 seconds.
                await _api.PingAsync(ct);
                response = await _api.UploadEventsAsync(pending, ct);
            }
            catch (Exception ex)
            {
                foreach (var e in pending) await _store.MarkRetryAsync(e.LocalId, ex.Message);
                _log.Warn($"Server sync deferred: {ex.Message}");
                return 0;
            }

            // HiMate Core 2.2 returns one result per input event, in the same loop/order.
            // Pair by index, not event_id, because a device event ID can be reset and reused later.
            var n = Math.Min(pending.Count, response.Accepted.Count);
            var synced = 0;
            for (var i = 0; i < n; i++)
            {
                var local = pending[i];
                var result = response.Accepted[i];
                var status = result.Status.ToUpperInvariant();
                switch (status)
                {
                    case "ACK":
                    case "DUPLICATE":
                        await _store.MarkSyncedAsync(local.LocalId, result.ServerId, status);
                        synced++;
                        break;
                    case "CONFLICT":
                        await _store.MarkPermanentFailureAsync(local.LocalId, "CONFLICT", "Server reported CONFLICT");
                        break;
                    case "INVALID":
                        await _store.MarkPermanentFailureAsync(local.LocalId, "INVALID", "Server reported INVALID");
                        break;
                    default:
                        await _store.MarkRetryAsync(local.LocalId, $"Server status: {status}");
                        break;
                }
            }

            // A short/malformed response must not silently lose the remaining items.
            for (var i = n; i < pending.Count; i++)
            {
                await _store.MarkRetryAsync(pending[i].LocalId, "Server response did not include this event.");
            }

            if (synced > 0) _log.Info($"Server synced {synced} event(s)");
            return synced;
        }
        finally
        {
            _gate.Release();
        }
    }
}

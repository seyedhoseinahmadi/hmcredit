using HiMate.Agent.Models;

namespace HiMate.Agent.Protocol;

public static class DeviceProtocolParser
{
    public static bool TryParseCardEvent(string line, out CardEvent cardEvent)
    {
        cardEvent = new CardEvent();
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("CARD_EVENT|", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fields = ParseFields(line);
        if (!TryLong(fields, "ID", out var id) ||
            !fields.TryGetValue("UID", out var uid) || string.IsNullOrWhiteSpace(uid) ||
            !fields.TryGetValue("TYPE", out var type) || string.IsNullOrWhiteSpace(type) ||
            !TryInt(fields, "AMOUNT", out var amount) ||
            !TryInt(fields, "TOTAL", out var total) ||
            !TryInt(fields, "REMAINING", out var remaining) ||
            !TryInt(fields, "TX", out var tx) ||
            !TryInt(fields, "GEN", out var gen) ||
            !TryInt(fields, "SEQ", out var seq))
        {
            return false;
        }

        long? cid = null;
        if (TryLong(fields, "CID", out var parsedCid) && parsedCid > 0)
        {
            cid = parsedCid;
        }

        long? oid = null;
        if (TryLong(fields, "OID", out var parsedOid) && parsedOid > 0)
        {
            oid = parsedOid;
        }

        cardEvent = new CardEvent
        {
            DeviceEventId = id,
            Uid = NormalizeUid(uid),
            Type = type.Trim().ToUpperInvariant(),
            Amount = amount,
            Total = total,
            Remaining = remaining,
            Tx = tx,
            Gen = gen,
            Seq = seq,
            Cid = cid,
            Oid = oid,
            ReceivedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        return true;
    }

    public static Dictionary<string, string> ParseFields(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 1; i < parts.Length; i++)
        {
            var p = parts[i];
            var eq = p.IndexOf('=');
            if (eq <= 0 || eq == p.Length - 1)
            {
                continue;
            }

            result[p[..eq].Trim()] = p[(eq + 1)..].Trim();
        }

        return result;
    }

    public static string NormalizeUid(string uid) => uid.Trim().ToUpperInvariant();

    private static bool TryInt(IReadOnlyDictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out var raw) && int.TryParse(raw, out value);
    }

    private static bool TryLong(IReadOnlyDictionary<string, string> fields, string key, out long value)
    {
        value = 0;
        return fields.TryGetValue(key, out var raw) && long.TryParse(raw, out value);
    }
}

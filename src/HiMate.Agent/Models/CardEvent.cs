namespace HiMate.Agent.Models;

public sealed class CardEvent
{
    public long LocalId { get; set; }
    public long DeviceEventId { get; set; }
    public string Uid { get; set; } = "";
    public string Type { get; set; } = "";
    public int Amount { get; set; }
    public int Total { get; set; }
    public int Remaining { get; set; }
    public int Tx { get; set; }
    public int Gen { get; set; }
    public int Seq { get; set; }
    public long? Cid { get; set; }
    public long? Oid { get; set; }
    public string ReceivedAtUtc { get; set; } = "";
    public bool DeviceAcked { get; set; }
    public string SyncStatus { get; set; } = "PENDING";
    public long? ServerId { get; set; }
    public string LastError { get; set; } = "";
    public int RetryCount { get; set; }

    public string EventKey => $"{Uid}|{Gen}|{Tx}|{Seq}".ToUpperInvariant();
}

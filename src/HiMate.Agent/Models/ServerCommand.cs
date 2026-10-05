namespace HiMate.Agent.Models;

public sealed class ServerCommand
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Uid { get; set; } = "";
    public int Amount { get; set; }
    public string Status { get; set; } = "";
    public string Note { get; set; } = "";
}

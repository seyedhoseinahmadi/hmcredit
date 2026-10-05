namespace HiMate.Agent.Models;

public sealed class AgentSettings
{
    public string ServerUrl { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public string ComPort { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int SyncIntervalSeconds { get; set; } = 30;
}

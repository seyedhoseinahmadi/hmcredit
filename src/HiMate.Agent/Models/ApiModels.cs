using System.Text.Json.Serialization;

namespace HiMate.Agent.Models;

public sealed class PingResponse
{
    public bool Success { get; set; }
    public string Service { get; set; } = "";
    public string Version { get; set; } = "";
    public string Time { get; set; } = "";
}

public sealed class EventUploadDto
{
    [JsonPropertyName("event_id")]
    public long EventId { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }

    [JsonPropertyName("tx")]
    public int Tx { get; set; }

    [JsonPropertyName("gen")]
    public int Gen { get; set; }

    [JsonPropertyName("seq")]
    public int Seq { get; set; }

    [JsonPropertyName("command_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CommandId { get; set; }

    [JsonPropertyName("order_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OrderId { get; set; }
}

public sealed class EventBatchRequest
{
    [JsonPropertyName("events")]
    public List<EventUploadDto> Events { get; set; } = [];
}

public sealed class EventResultDto
{
    [JsonPropertyName("event_id")]
    public long? EventId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("server_id")]
    public long? ServerId { get; set; }
}

public sealed class EventBatchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("accepted")]
    public List<EventResultDto> Accepted { get; set; } = [];

    [JsonPropertyName("server_time")]
    public string ServerTime { get; set; } = "";
}

public sealed class CommandsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("commands")]
    public List<ServerCommand> Commands { get; set; } = [];

    [JsonPropertyName("server_time")]
    public string ServerTime { get; set; } = "";
}

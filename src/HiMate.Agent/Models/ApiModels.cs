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

public sealed class UserSearchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("users")]
    public List<UserSummary> Users { get; set; } = [];
}

public sealed class UserSummary
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Phone) ? $"{Name} (#{Id})" : $"{Name} — {Phone}";
}

public sealed class CardOwnerInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";
}

public sealed class CardInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("user_id")]
    public long? UserId { get; set; }

    [JsonPropertyName("owner")]
    public CardOwnerInfo? Owner { get; set; }

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
}

public sealed class CardLookupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    [JsonPropertyName("card")]
    public CardInfo? Card { get; set; }
}

public sealed class CardAssignResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("card")]
    public CardInfo? Card { get; set; }
}

public sealed class TopupRequest
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("topup_id")]
    public long TopupId { get; set; }

    [JsonPropertyName("command_id")]
    public long? CommandId { get; set; }

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("customer_name")]
    public string CustomerName { get; set; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";

    [JsonPropertyName("credits")]
    public int Credits { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    [JsonPropertyName("card_id")]
    public long? CardId { get; set; }

    [JsonPropertyName("topup_status")]
    public string TopupStatus { get; set; } = "";

    [JsonPropertyName("topup_status_label")]
    public string TopupStatusLabel { get; set; } = "";

    [JsonPropertyName("command_status")]
    public string CommandStatus { get; set; } = "";

    [JsonPropertyName("assigned_device_id")]
    public long AssignedDeviceId { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("sale_amount")]
    public long SaleAmount { get; set; }

    [JsonPropertyName("pay_amount")]
    public long PayAmount { get; set; }

    [JsonPropertyName("payment_ref")]
    public string PaymentRef { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("paid_at")]
    public string? PaidAt { get; set; }

    [JsonPropertyName("applied_at")]
    public string? AppliedAt { get; set; }

    [JsonPropertyName("can_apply")]
    public bool CanApply { get; set; }

    [JsonPropertyName("claimed_by_this_device")]
    public bool ClaimedByThisDevice { get; set; }

    [JsonIgnore]
    public string DisplayStatus => ClaimedByThisDevice ? "در حال اعمال روی این دستگاه" : TopupStatusLabel;
}

public sealed class TopupsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topups")]
    public List<TopupRequest> Topups { get; set; } = [];

    [JsonPropertyName("server_time")]
    public string ServerTime { get; set; } = "";
}

public sealed class TopupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topup")]
    public TopupRequest? Topup { get; set; }

    [JsonPropertyName("recovered")]
    public bool Recovered { get; set; }

    [JsonPropertyName("already_applied")]
    public bool AlreadyApplied { get; set; }

    [JsonPropertyName("server_event_id")]
    public long? ServerEventId { get; set; }
}


public sealed class OrderDebitRequest
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("cid")]
    public long Cid { get; set; }
    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
    [JsonPropertyName("customer_name")]
    public string CustomerName { get; set; } = "";
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";
    [JsonPropertyName("plate")]
    public string Plate { get; set; } = "";
    [JsonPropertyName("device_id")]
    public long DeviceId { get; set; }
    [JsonPropertyName("payment_status")]
    public string PaymentStatus { get; set; } = "";
}
public sealed class OrderDebitsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("debits")]
    public List<OrderDebitRequest> Debits { get; set; } = [];
}
public sealed class OrderDebitResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("debit")]
    public OrderDebitRequest? Debit { get; set; }
}

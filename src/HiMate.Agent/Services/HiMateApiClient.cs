using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HiMate.Agent.Models;

namespace HiMate.Agent.Services;

public sealed class HiMateApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private TimeSpan _serverOffset = TimeSpan.Zero;

    public string BaseUrl { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public string DeviceSecret { get; set; } = "";

    public HiMateApiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HiMate-Credit/0.2.8");
    }

    public async Task<PingResponse> PingAsync(CancellationToken ct = default)
    {
        var url = Url("/wp-json/himate/v1/ping");
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        var ping = JsonSerializer.Deserialize<PingResponse>(body, _json) ?? throw new InvalidOperationException("Invalid ping response.");
        if (DateTimeOffset.TryParse(ping.Time, out var serverTime))
        {
            _serverOffset = serverTime - DateTimeOffset.UtcNow;
        }
        return ping;
    }

    public async Task<EventBatchResponse> UploadEventsAsync(IReadOnlyList<CardEvent> events, CancellationToken ct = default)
    {
        var dto = new EventBatchRequest
        {
            Events = events.Select(e => new EventUploadDto
            {
                EventId = e.DeviceEventId,
                Uid = e.Uid,
                Type = e.Type,
                Amount = e.Amount,
                Total = e.Total,
                Remaining = e.Remaining,
                Tx = e.Tx,
                Gen = e.Gen,
                Seq = e.Seq,
                CommandId = e.Cid,
                OrderId = e.Oid
            }).ToList()
        };

        var raw = JsonSerializer.Serialize(dto, _json);
        using var req = CreateSignedRequest(HttpMethod.Post, "/wp-json/himate/v1/events", raw);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<EventBatchResponse>(body, _json) ?? throw new InvalidOperationException("Invalid event response.");
    }

    public async Task<CommandsResponse> GetCommandsAsync(CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Get, "/wp-json/himate/v1/commands", "");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<CommandsResponse>(body, _json) ?? throw new InvalidOperationException("Invalid commands response.");
    }

    public async Task<UserSearchResponse> SearchUsersAsync(string search, CancellationToken ct = default)
    {
        var path = "/wp-json/himate/v1/users?search=" + Uri.EscapeDataString(search.Trim());
        using var req = CreateSignedRequest(HttpMethod.Get, path, "");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<UserSearchResponse>(body, _json) ?? throw new InvalidOperationException("Invalid users response.");
    }

    public async Task<CardLookupResponse> GetCardAsync(string uid, CancellationToken ct = default)
    {
        var path = "/wp-json/himate/v1/cards/" + Uri.EscapeDataString(uid.Trim());
        using var req = CreateSignedRequest(HttpMethod.Get, path, "");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<CardLookupResponse>(body, _json) ?? throw new InvalidOperationException("Invalid card response.");
    }

    public async Task<CardAssignResponse> AssignCardAsync(string uid, long userId, bool force = false, CancellationToken ct = default)
    {
        var raw = JsonSerializer.Serialize(new { uid, user_id = userId, force }, _json);
        using var req = CreateSignedRequest(HttpMethod.Post, "/wp-json/himate/v1/cards/assign", raw);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<CardAssignResponse>(body, _json) ?? throw new InvalidOperationException("Invalid card assignment response.");
    }

    public async Task<CardAssignResponse> UnassignCardAsync(string uid, CancellationToken ct = default)
    {
        var raw = JsonSerializer.Serialize(new { uid }, _json);
        using var req = CreateSignedRequest(HttpMethod.Post, "/wp-json/himate/v1/cards/unassign", raw);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<CardAssignResponse>(body, _json) ?? throw new InvalidOperationException("Invalid card unassign response.");
    }

    public async Task<TopupsResponse> GetPendingTopupsAsync(CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Get, "/wp-json/himate/v1/topups/pending", "");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<TopupsResponse>(body, _json) ?? throw new InvalidOperationException("Invalid topups response.");
    }

    public async Task<TopupResponse> GetTopupAsync(long topupId, CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Get, $"/wp-json/himate/v1/topups/{topupId}", "");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<TopupResponse>(body, _json) ?? throw new InvalidOperationException("Invalid topup response.");
    }

    public async Task<TopupResponse> ClaimTopupAsync(long topupId, CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Post, $"/wp-json/himate/v1/topups/{topupId}/claim", "{}");
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<TopupResponse>(body, _json) ?? throw new InvalidOperationException("Invalid topup claim response.");
    }

    public async Task<TopupResponse> RecoverTopupAsync(
        long topupId,
        long commandId,
        int amount,
        int total,
        int remaining,
        int tx,
        int gen,
        int seq,
        CancellationToken ct = default)
    {
        var raw = JsonSerializer.Serialize(new
        {
            command_id = commandId,
            amount,
            total,
            remaining,
            tx,
            gen,
            seq
        }, _json);

        using var req = CreateSignedRequest(HttpMethod.Post, $"/wp-json/himate/v1/topups/{topupId}/recover", raw);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<TopupResponse>(body, _json) ?? throw new InvalidOperationException("Invalid topup recovery response.");
    }

    public async Task MarkCommandStatusAsync(long commandId, string status, string? reason = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?> { ["status"] = status.ToUpperInvariant() };
        if (!string.IsNullOrWhiteSpace(reason)) payload["reason"] = reason;
        var raw = JsonSerializer.Serialize(payload, _json);
        using var req = CreateSignedRequest(HttpMethod.Post, $"/wp-json/himate/v1/commands/{commandId}/status", raw);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }
    }


    // Orders are initiated on the website; this Agent polls the signed queue
    // because USB-attached ESP32s cannot receive inbound Internet requests.
    public async Task<OrderDebitsResponse> GetPendingOrderDebitsAsync(CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Get, "/wp-json/himate/v1/orders/debits", "");
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<OrderDebitsResponse>(body, _json) ?? throw new InvalidOperationException("Invalid order debits response.");
    }

    public async Task<OrderDebitResponse> ClaimOrderDebitAsync(long orderId, CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Post, $"/wp-json/himate/v1/orders/{orderId}/debit/claim", "{}");
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<OrderDebitResponse>(body, _json) ?? throw new InvalidOperationException("Invalid debit claim response.");
    }

    public async Task<OrderDebitResponse> GetOrderDebitAsync(long orderId, CancellationToken ct = default)
    {
        using var req = CreateSignedRequest(HttpMethod.Get, $"/wp-json/himate/v1/orders/{orderId}/debit", "");
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<OrderDebitResponse>(body, _json) ?? throw new InvalidOperationException("Invalid order debit response.");
    }

    public async Task MarkOrderDebitExpiredAsync(long orderId, long cid, CancellationToken ct = default)
        => await PostOrderDebitStatusAsync(orderId, "expire", new { command_id = cid }, ct);

    public async Task MarkOrderDebitReviewAsync(long orderId, long cid, string reason, CancellationToken ct = default)
        => await PostOrderDebitStatusAsync(orderId, "review", new { command_id = cid, reason }, ct);

    private async Task PostOrderDebitStatusAsync(long orderId, string operation, object data, CancellationToken ct)
    {
        var raw = JsonSerializer.Serialize(data, _json);
        using var req = CreateSignedRequest(HttpMethod.Post, $"/wp-json/himate/v1/orders/{orderId}/debit/{operation}", raw);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}: {body}");
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string path, string rawBody)
    {
        if (string.IsNullOrWhiteSpace(DeviceCode) || string.IsNullOrWhiteSpace(DeviceSecret))
        {
            throw new InvalidOperationException("Device code/secret are not configured.");
        }

        var unix = (DateTimeOffset.UtcNow + _serverOffset).ToUnixTimeSeconds().ToString();
        var canonical = DeviceCode + "\n" + unix + "\n" + rawBody;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(DeviceSecret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        var req = new HttpRequestMessage(method, Url(path));
        req.Headers.Add("X-HiMate-Device", DeviceCode);
        req.Headers.Add("X-HiMate-Timestamp", unix);
        req.Headers.Add("X-HiMate-Signature", sig);
        if (method != HttpMethod.Get || rawBody.Length > 0)
        {
            req.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        return req;
    }

    private string Url(string path)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) throw new InvalidOperationException("Server URL is not configured.");
        return BaseUrl.TrimEnd('/') + path;
    }
}

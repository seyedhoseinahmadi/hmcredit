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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HiMate-Credit/0.2.2");
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

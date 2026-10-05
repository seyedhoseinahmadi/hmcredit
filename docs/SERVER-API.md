# HiMate Core 2.2 REST contract used by Agent 0.1

## Ping

```text
GET /wp-json/himate/v1/ping
```

## Authentication

Authenticated endpoints use:

```text
X-HiMate-Device
X-HiMate-Timestamp
X-HiMate-Signature
```

Canonical HMAC input:

```text
device_code + "\n" + unix_timestamp + "\n" + raw_request_body
```

Signature:

```text
hex-lowercase HMAC-SHA256(secret, canonical_input)
```

The exact JSON UTF-8 string that is signed is also sent as the request body.

## Events

```text
POST /wp-json/himate/v1/events
```

Batch max: 100.

```json
{
  "events": [
    {
      "event_id": 29,
      "uid": "46:08:5A:B8",
      "type": "DEBIT",
      "amount": 1,
      "total": 44,
      "remaining": 17,
      "tx": 20,
      "gen": 15101,
      "seq": 22
    }
  ]
}
```

Success statuses treated as synced:

- `ACK`
- `DUPLICATE`

Permanent operator-visible statuses:

- `CONFLICT`
- `INVALID`

`ERROR` and HTTP/network failures are retried.

Important: Core 2.2 returns `event_id` in each result. Because device IDs may later be reset/reused, Agent pairs the response to the current batch by **index/order**, matching the current server implementation which emits exactly one result per input in the same loop order.

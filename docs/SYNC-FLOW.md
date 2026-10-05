# Sync Flow

## Offline-first event flow

```text
Card
 ↓
ESP mutation + local event queue
 ↓ Serial
Windows Agent
 ↓
SQLite COMMIT
 ↓
EVENT_ACK to ESP
 ↓
background HTTPS upload
 ↓
HiMate Core
```

Device ACK and server sync are intentionally separate concepts.

## Local states

- `PENDING` — stored locally, waiting for server
- `RETRY` — transient/network/server error, retry later
- `SYNCED` — server returned `ACK` or `DUPLICATE`
- `CONFLICT` — server says same logical event key has different semantics
- `INVALID` — rejected by server validation

## Current idempotency alignment

The local SQLite unique key is:

```text
uid + gen + tx + seq
```

which matches the logical key currently used by HiMate Core 2.2.

# HiMate Credit Windows Agent

Windows WPF (.NET 8) bridge between ESP32 card reader and HiMate Core WordPress.

**Agent:** 0.2.10 — **Core:** 2.6.0 or later for autonomous car-wash credit settlement.

## Payment contract

- Operator registers an order in Core; POS/cash/bank-transfer payments are recorded by Core, not executed by Agent.
- For a CREDIT order, Core creates a pending payment; **it does not send DEBIT commands**.
- Card firmware independently performs a real physical `DEBIT`. Agent durably records each `CARD_EVENT` in local SQLite (WAL, FULL synchronous), sends `EVENT_ACK` only after commit, then retries posting the event to `POST /wp-json/himate/v1/events`.
- Core independently matches physical DEBIT to an eligible pending order by card UID, owner, amount and the allowed matching window. Agent does not mark orders paid, choose an order for a DEBIT, or insert synthetic DEBIT events.
- An unmatched/ambiguous/late event requires review on the website. An HTTP upload acknowledgement indicates event storage, **not** that an order is paid.
- `ADD` (physical topup) remains bound to a claimed, UID/CID-specific CREDIT command with crash recovery; do **not** remove its claim or replay safeguards.
- Never delete pending SQLite events as part of an upgrade. Older Core 2.5.5 order-debit commands require server-side review before reenabling payments.

**Limitation:** For automatic matching, the firmware's physical debit amount must equal the order's credit due. Manually setting the device default debit (`SETDEBIT`) is not the same as a website payment command.

## Supported server endpoints

```text
GET  /wp-json/himate/v1/ping
POST /wp-json/himate/v1/events
GET  /wp-json/himate/v1/commands
POST /wp-json/himate/v1/commands/{id}/status
GET  /wp-json/himate/v1/users?search=...
GET  /wp-json/himate/v1/cards/{UID}
POST /wp-json/himate/v1/cards/assign
POST /wp-json/himate/v1/cards/unassign
GET  /wp-json/himate/v1/topups/pending
GET  /wp-json/himate/v1/topups/{id}
POST /wp-json/himate/v1/topups/{id}/claim
POST /wp-json/himate/v1/topups/{id}/recover
```

The legacy `/orders/debits` and `/orders/{id}/debit/claim` APIs are **not called by Agent 0.2.10**. Core may retain legacy review routes for migration only. Commands with `device_id=0` are claimed by the first authorized Agent that confirms delivery; this is for remaining legitimate commands such as CREDIT, not new order DEBITs.

### Event identity

Device events are deduplicated by `SHA256(normalized_UID|GEN|TX|SEQ)`. All `ISSUE`, `ADD`, and `DEBIT` events must preserve their physical generation, transaction and sequence counters. Do not generate synthetic event identifiers or attach `OID/CID` to a new autonomous DEBIT.

## Setup / build

Requires Windows 10/11 x64 and a USB serial device, typically 115200 baud.
In WordPress create a device under **HiMate Core → Devices**, then configure site URL, device code and secret inside the Agent. The shared device secret is stored with Windows DPAPI.

```powershell
dotnet restore .\HiMate.Agent.sln
dotnet build .\HiMate.Agent.sln -c Release
.\scripts\publish-win-x64.ps1
```

Local persistent data: `%LOCALAPPDATA%\HiMate\Agent\` (`agent.db`, `settings.json`, `secret.dat`). Updates must preserve this directory.

Official installer packaging is handled by the GitHub **Release HiMate Credit** workflow on Windows runners, including the official signed CP210x driver staging. A green build is not proof of physical card or production settlement safety.

See [Core 2.6 compatibility checks](docs/CORE_2.6_COMPAT.md) before production deployment.

## License

No public/open-source license has been selected yet.

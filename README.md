# HiMate Agent

Windows bridge between the HiMate ESP32 card reader and **HiMate Core** on WordPress.

This repository is prepared for GitHub Actions and produces a self-contained `win-x64` build.

## Current status — v0.1.0

Implemented:

- Windows WPF UI (.NET 8)
- Serial connection to the ESP32 at 115200 baud
- Parsing `CARD_EVENT|...`
- Durable SQLite event queue (`WAL` + `synchronous=FULL`)
- `EVENT_ACK` is sent only after the event is committed locally
- Automatic/retryable event upload to HiMate Core 2.2
- HMAC authentication compatible with HiMate Core 2.2
- Server-time offset from `/ping` to reduce clock-skew failures
- Device code + secret settings in the Agent
- Device secret protected with Windows DPAPI
- Read-only server command list
- GitHub Actions build/publish pipeline

Not enabled yet:

- Automatic server CREDIT execution. It is intentionally blocked until firmware supports a UID-bound command such as:
  `CREDIT|CID=125|UID=46:08:5A:B8|AMOUNT=7`
- automatic server `default_debit` propagation (HiMate Core 2.2 does not expose it through the REST response yet)

## Build locally

Requirements:

- Windows 10/11 x64
- .NET 8 SDK

```powershell
dotnet restore .\HiMate.Agent.sln
dotnet build .\HiMate.Agent.sln -c Release
.\scripts\publish-win-x64.ps1
```

The publish output is written to `artifacts/win-x64`.

## Build on GitHub

Push this repository to GitHub and open **Actions → Build Windows Agent → Run workflow**.
The workflow produces a downloadable artifact named `HiMate-Agent-win-x64`.

## Server configuration

Create a device in WordPress under HiMate Core → Devices. Copy the generated:

- Device Code
- Device Secret

Then enter them in Agent → Settings together with the site base URL.

Example base URL:

```text
https://example.com
```

The Agent calls:

```text
GET  /wp-json/himate/v1/ping
POST /wp-json/himate/v1/events
GET  /wp-json/himate/v1/commands
```

## Local data

The Agent stores runtime data under:

```text
%LOCALAPPDATA%\HiMate\Agent\
```

- `agent.db` — SQLite durable queue
- `settings.json` — non-secret configuration
- `secret.dat` — DPAPI protected device secret

## License

No public/open-source license has been selected yet. Add the intended license before publishing the repository publicly.

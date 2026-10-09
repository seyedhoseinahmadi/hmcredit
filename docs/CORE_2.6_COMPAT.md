# Compatibility: HiMate Agent 0.2.10 + Core 2.6.0

## Required rollout order

1. Back up WordPress database and the installed Core directory. Back up the existing Windows Agent SQLite directory (`%LOCALAPPDATA%\HiMate\Agent\`).
2. Update Core on staging to **2.6.0** and check `GET /wp-json/himate/v1/ping` returns version `2.6.0`.
3. Inspect old 2.5.5 order-debit commands in Core. PENDING/REVIEW commands need review; DELIVERED commands must **not** be casually cancelled because the physical card may have been charged.
4. Install the tested Agent from a successful Windows build; ensure its existing settings and local queued events remain intact.
5. Configure device UID assignment and debit amount. A manual `SETDEBIT` on firmware sets local behavior; **the website does not send DEBIT commands**.
6. Before any real customer payment, perform the tests below with test cards and orders. Confirm all figures both on physical card and Core reports.

## Integration

- `GET /ping`: verify Core version >= 2.6.0 for automatic credit-order reconciliation. Agent shows an upgrade warning for older versions, but does **not** block event backup or syncing.
- `POST /events`: sends authentic `ISSUE`, `ADD`, and `DEBIT` card events; Core responds per event with `ACK`, `DUPLICATE`, `CONFLICT`, or `INVALID`. ACK means stored, **not paid**.
- Signature: Core HMAC contract with Device Code + timestamp + original body remains unchanged.
- Event key: `SHA256(normalized UID|GEN|TX|SEQ)`. The Agent must never forge or rewrite physical identities.
- `GET /commands`: Core excludes website-generated DEBIT in 2.6.0. Legitimate topup CREDIT commands remain supported.
- `/topups/pending`, `/topups/{id}/claim`, `/topups/{id}/recover`: preserve CID/UID-bound write and recovery, and never use autonomous matching for `ADD`.
- No Agent polling of `/orders/debits`; no Agent call to `/orders/{id}/debit/claim`; no `DEBITCMD` serial action.
- Core, not Agent, links received DEBIT events to orders using active card UID, customer, credit amount, and an approximately 30-minute server-receipt window.

## Required smoke tests

| Case | Expected |
| --- | --- |
| One pending credit order, one matching real card DEBIT | One settled order, one debit journal entry |
| Two same-value orders near same time | Ambiguity; no automatic settlement |
| Two same-value DEBIT events near same time | Ambiguity; manual review required |
| Offline device/Agent uploads later | Events are saved, uploaded once connection resumes; may require manual match |
| Replayed identical CARD_EVENT | `DUPLICATE`; never a second debit payment |
| Wrong card, wrong owner, wrong value | No automatic settlement |
| Physical ADD after a claimed topup | One successful topup with correct CID and UID |
| Pending older 2.5.5 DEBIT command | Flag/review; no unsafe automatic cancellation |
| POS/cash/bank-transfer order | Payment is recorded in Core; no change to physical card |
| Power failure after local SQLite commit | Event survives; ACK/retry semantics preserved |

## Deployment gate

A passing .NET build only proves compilation and packaging. It does not verify actual ESP32 firmware behavior, serial port, WordPress database migrations, or cash/credit settlement. Keep production payments disabled until the staging hardware tests above pass.

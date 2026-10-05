# Device Serial Protocol

Current compatible firmware events:

```text
CARD_EVENT|ID=29|UID=46:08:5A:B8|TYPE=DEBIT|AMOUNT=1|TOTAL=44|REMAINING=17|TX=20|GEN=15101|SEQ=22
```

The parser also accepts future optional fields:

```text
CID=125
OID=418137
```

Durability rule:

```text
ESP CARD_EVENT
  -> Agent SQLite transaction COMMIT
  -> EVENT_ACK|ID=n
```

The Agent never acknowledges an event that failed local durable storage.

## Planned server-credit command

```text
CREDIT|CID=125|UID=46:08:5A:B8|AMOUNT=7
```

Firmware must validate the presented UID before card mutation. A wrong card must not be modified.

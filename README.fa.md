# HiMate Agent — راهنمای فارسی

این نسخه، پایه‌ی تمیز ویندوز برای اتصال:

```text
ESP32 ⇄ Windows Agent ⇄ HiMate Core
```

است و برای قرار گرفتن روی GitHub و گرفتن خروجی ویندوز آماده شده.

## چیزی که همین نسخه انجام می‌دهد

1. به COM دستگاه وصل می‌شود.
2. `CARD_EVENT`های ESP را می‌خواند.
3. هر Event را اول به SQLite با Commit واقعی ذخیره می‌کند.
4. فقط بعد از ذخیره موفق، `EVENT_ACK|ID=...` را به ESP می‌فرستد.
5. Eventها را با HMAC به HiMate Core 2.2 ارسال می‌کند.
6. اگر اینترنت قطع باشد Event در SQLite می‌ماند و دوباره Retry می‌شود.
7. `ACK` و `DUPLICATE` سرور را Sync موفق حساب می‌کند.
8. `CONFLICT` و `INVALID` را جدا نگه می‌دارد تا پنهان نشوند.
9. Device Code و Device Secret داخل خود Agent تنظیم می‌شوند.
10. Secret با DPAPI ویندوز ذخیره می‌شود، نه داخل `settings.json`.

## نکته مهم درباره Event ID

`ID` دستگاه شناسه محلی صف ESP است و می‌تواند در آینده Reset شود. Agent برای تشخیص Event محلی به ترکیب:

```text
UID + GEN + TX + SEQ
```

تکیه می‌کند. همچنین پاسخ Batch فعلی Core 2.2 را بر اساس ترتیب ورودی/خروجی Pair می‌کند، نه صرفاً `event_id`.

## CREDIT سروری

در این نسخه Commandها از سایت قابل دریافت و مشاهده هستند، اما اعمال خودکار Credit عمداً غیرفعال است تا Firmware این قرارداد را پشتیبانی کند:

```text
CREDIT|CID=125|UID=46:08:5A:B8|AMOUNT=7
```

بعد از اضافه شدن این پروتکل به Firmware، مرحله بعدی Agent این خواهد بود:

```text
Server Command
→ Claim
→ UID-bound CREDIT به ESP
→ CARD_EVENT با CID
→ Upload
→ Core = APPLIED
```

## گرفتن خروجی با GitHub

فولدر را داخل یک Repository قرار بده و Push کن. سپس:

```text
GitHub → Actions → Build Windows Agent → Run workflow
```

خروجی `win-x64` به صورت Artifact ساخته می‌شود و روی سیستم مقصد نیازی به نصب .NET ندارد.

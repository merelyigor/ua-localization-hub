# API клієнт — документація

## Endpoint

BDO aggregate endpoint:

```
GET https://bdo-ua.com.ua/api/public/v1/releases
```

Авторизація не потрібна. Відповідь — JSON.

## Winds4UA latest feed

WWM має окремий API-контракт і DTO; він не десеріалізується у BDO `ReleasesResponse`.

`Winds4UaApiClient.GetLatestAsync()` виконує `GET https://winds4ua.com.ua/api/public/v1/releases/latest`. Відповідь містить `success`, `generated_at`, nullable `data.current` і динамічний `data.modes[]`. `WwmPackageResolver` зіставляє вибраний artifact тільки за парою `slug + variant`, вимагає `available=true` та рівно один відповідний current file. Display label, порядок масиву, верхній `current.download_url` і історичні релізи не визначають package identity. URL має бути абсолютним HTTPS, size та SHA-256 — валідними. Невідомі поля й майбутні режими не ламають parsing.

Перед WWM install/update виконується свіжий latest-запит; лише актуальні metadata можуть дозволити mutation. `current=null`, недоступний режим або некоректний artifact блокують запис. Steam build ID та `game_tested` не утворюють compatibility mapping, тому перед mutation показується нейтральне явне підтвердження. ZIP завантажується з вибраного mode/current-file URL, перевіряється за outer size/SHA і розпаковується лише у staging.

Деталі production contract: [Where Winds Meet integration contract](design/where-winds-meet-integration-contract.md). API тести використовують контрольовані локальні HTTP fixtures.

## Ідентифікація desktop client

Application-owned HTTP requests до BDO-UA services використовують User-Agent:

```text
BdoUaClient/<version> (+https://bdo-ua.com.ua)
```

Наприклад, public release використовує `BdoUaClient/1.4.2 (+https://bdo-ua.com.ua)`, development build — `BdoUaClient/DEV (+https://bdo-ua.com.ua)`, а невідома версія — `BdoUaClient/unknown (+https://bdo-ua.com.ua)`.

Це потрібне лише для server-side aggregate source attribution вже наявних localization downloads. Це не user telemetry: клієнт не надсилає user, installation, machine або інший персональний ідентифікатор, а також не створює окремих analytics-запитів.

---

## BdoUaApiClient

Клас: `BdoClient.Api.BdoUaApiClient`

Обгортає `HttpClient`. Повертає `ApiResult<T>` замість виключень.

| Параметр | Значення |
|---|---|
| Base URL | `https://bdo-ua.com.ua/api/public/v1` |
| Per-request timeout | 30 секунд (`GetReleasesAsync`) |
| JSON | `PropertyNameCaseInsensitive = true` |

### Методи

```csharp
Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default);

Task WarmupConnectionAsync(CancellationToken cancellationToken = default);
```

### WarmupConnectionAsync

Lightweight HEAD-запит до `https://bdo-ua.com.ua/` для попереднього прогріву DNS/TLS з'єднання. Викликається fire-and-forget при startup, власний timeout 5 секунд, помилки не прокидаються (тільки Debug-лог). Не впливає на бізнес-логіку.

### Обробка помилок

| Ситуація | ApiErrorKind |
|---|---|
| HTTP 4xx/5xx | `Http` |
| Порожня відповідь | `InvalidResponse` |
| Невалідний JSON | `InvalidResponse` |
| `success == false` | `InvalidResponse` |
| `data == null` | `InvalidResponse` |
| Timeout | `Timeout` |
| Скасовано | `Cancelled` |
| Мережева помилка | `Network` |
| Інше | `Unexpected` |

При помилках клієнт логує correlation headers відповіді (`X-Request-ID`, `Server-Timing`, `CF-Ray`), а також structured timing-логи тривалості запиту — для діагностики з server-side логами.

---

## Конфігурація HttpClient

Клас: `BdoClient.Api.BdoUaHttpClientConfiguration` (static)

- `BuildUserAgent(AppVersionInfo)` — формує User-Agent (див. розділ «Ідентифікація desktop client»)
- `Configure(...)` / `CreateHttpClient(...)` — створює `HttpClient` з:
  - `SocketsHttpHandler` + `ResilientConnectionConnector` як `ConnectCallback`
  - `UseProxy = false`
  - User-Agent application-owned заголовком

## ResilientConnectionConnector

Internal helper: кастомний TCP connect через `SocketsHttpHandler.ConnectCallback`. Паралельно відкриває спроби з'єднання до всіх IP-адрес хоста (результатів DNS) зі stagger 250 мс та обирає перше успішне з'єднання (happy-eyeballs-подібна поведінка). Спроби впорядковуються за AddressFamily. Це зменшує затримку підключення при повільних/недоступних DNS-маршрутах.

## NetworkDiagnostics

Internal static helper: `FormatNetworkError(Exception)` — формує читабельний опис мережевої помилки (DNS failure, connection refused, timeout тощо) для структурованих логів.

---

## ApiResult<T>

Клас: `BdoClient.Api.ApiResult<T>`

| Властивість | Тип | Опис |
|---|---|---|
| `IsSuccess` | `bool` | Успіх операції |
| `Value` | `T?` | Дані (якщо успіх) |
| `ErrorKind` | `ApiErrorKind` | Тип помилки |
| `ErrorMessage` | `string?` | Текст помилки |

### ApiErrorKind

```
None, Cancelled, Timeout, Network, Http, InvalidResponse, Unexpected
```

---

## Моделі відповіді

### ReleasesResponse

Кореневий об'єкт відповіді.

| JSON поле | C# властивість | Тип |
|---|---|---|
| `success` | `Success` | `bool` |
| `generated_at` | `GeneratedAt` | `string?` |
| `data` | `Data` | `ReleaseData?` |

### ReleaseData

| JSON поле | C# властивість | Тип |
|---|---|---|
| `official_patch` | `OfficialPatch` | `int` |
| `official_patch_checked_at` | `OfficialPatchCheckedAt` | `string?` |
| `official_source_url` | `OfficialSourceUrl` | `string?` |
| `filename` | `Filename` | `string?` |
| `install_path_patterns` | `InstallPathPatterns` | `List<InstallPathPattern>?` |
| `install_guide_url` | `InstallGuideUrl` | `string?` |
| `progress` | `Progress` | `ProgressInfo?` |
| `modes` | `Modes` | `List<LocalizationMode>?` |

### LocalizationMode

| JSON поле | C# властивість | Тип |
|---|---|---|
| `slug` | `Slug` | `string?` |
| `public_name` | `PublicName` | `string?` |
| `description` | `Description` | `string?` |
| `audience` | `Audience` | `string?` |
| `current` | `Current` | `CurrentRelease?` |
| `history` | `History` | `List<ReleaseHistoryItem>?` |

Набір режимів контролюється сервером. Клієнт не захаркоджує конкретні slug — API може додавати/видаляти режими без зміни клієнта. `mode.Slug` для клієнта є opaque identity.

> **Важливо:** `current` може бути `null`. Це нормальний стан — означає, що актуальний release для цього режиму ще не опубліковано. Не є помилкою API чи десеріалізації.

### CurrentRelease

| JSON поле | C# властивість | Тип |
|---|---|---|
| `public_id` | `PublicId` | `string?` |
| `version` | `Version` | `int` |
| `filename` | `Filename` | `string?` |
| `download_url` | `DownloadUrl` | `string?` |
| `size_bytes` | `SizeBytes` | `long` |
| `sha256` | `Sha256` | `string?` |
| `patch` | `Patch` | `int` |
| `compatible_with_official_patch` | `CompatibleWithOfficialPatch` | `bool` |
| `published_at` | `PublishedAt` | `string?` |
| `game_tested_at` | `GameTestedAt` | `string?` |
| `game_test` | `GameTest` | `GameTestInfo?` |
| `stats` | `Stats` | `StatsInfo?` |
| `announcements` | `Announcements` | `AnnouncementsInfo?` |

Якщо `compatible_with_official_patch == false` — встановлення обраного релізу заблоковане, download не починається. `LocalizationState.UpdateAvailable` може існувати як factual internal стан, але окремої публічної кнопки "Оновити" немає.

### ReleaseHistoryItem

| JSON поле | C# властивість | Тип |
|---|---|---|
| `public_id` | `PublicId` | `string?` |
| `version` | `Version` | `int` |
| `patch` | `Patch` | `int` |
| `status` | `Status` | `string?` |
| `published_at` | `PublishedAt` | `string?` |
| `retired_at` | `RetiredAt` | `string?` |

Статуси: `superseded`, `withdrawn`. Поточний (`current`) ніколи не з'являється в history.

### InstallPathPattern

Підказки для автоматичного пошуку гри. Не є довіреними filesystem instructions.

| JSON поле | C# властивість | Тип |
|---|---|---|
| `pattern` | `Pattern` | `string?` |
| `launcher` | `Launcher` | `string?` |
| `description` | `Description` | `string?` |

Значення `launcher`: `steam`, `official`.

### GameTestInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `state` | `State` | `string?` |
| `label` | `Label` | `string?` |
| `note` | `Note` | `string?` |

### ProgressInfo

Глобальний прогрес перекладу (для всіх режимів).

| JSON поле | C# властивість | Тип |
|---|---|---|
| `total_rows` | `TotalRows` | `int` |
| `translated_percent` | `TranslatedPercent` | `double` |
| `manual_rows` | `ManualRows` | `int` |
| `manual_percent` | `ManualPercent` | `double` |
| `machine_rows` | `MachineRows` | `int` |
| `machine_percent` | `MachinePercent` | `double` |

### StatsInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `rows_in_file` | `RowsInFile` | `int` |

`rows_in_file` може відрізнятися від `progress.total_rows`.

### AnnouncementsInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `discord_releases` | `DiscordReleases` | `AnnouncementChannel?` |
| `telegram_main` | `TelegramMain` | `AnnouncementChannel?` |

### AnnouncementChannel

| JSON поле | C# властивість | Тип |
|---|---|---|
| `sent` | `Sent` | `bool` |
| `sent_at` | `SentAt` | `string?` |

---

## Ієрархія моделей

```
ReleasesResponse
├── success: bool
├── generated_at: string?
└── data: ReleaseData?
    ├── official_patch: int
    ├── official_patch_checked_at: string?
    ├── official_source_url: string?
    ├── filename: string?
    ├── install_guide_url: string?
    ├── install_path_patterns: List<InstallPathPattern>?
    │   └── InstallPathPattern { pattern, launcher, description }
    ├── progress: ProgressInfo?
    │   └── ProgressInfo { total_rows, translated_percent, manual_rows, manual_percent, machine_rows, machine_percent }
    └── modes: List<LocalizationMode>?
        └── LocalizationMode { slug, public_name, description, audience, current?, history? }
            ├── current: CurrentRelease?  ← nullable, null = release не опубліковано
            │   └── CurrentRelease { public_id, version, filename, download_url, size_bytes, sha256, patch, compatible_with_official_patch, published_at, game_tested_at, game_test?, stats?, announcements? }
            │       ├── game_test: GameTestInfo? { state, label, note }
            │       ├── stats: StatsInfo? { rows_in_file }
            │       └── announcements: AnnouncementsInfo? { discord_releases?, telegram_main? }
            │           └── AnnouncementChannel { sent, sent_at }
            └── history: List<ReleaseHistoryItem>?
                └── ReleaseHistoryItem { public_id, version, patch, status, published_at, retired_at }
```

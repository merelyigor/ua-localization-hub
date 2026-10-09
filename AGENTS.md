# AGENTS.md

---

## 📑 ЗМІСТ

| § | Розділ | Опис |
|---|---|---|
| [§1](#1--мова)] | 🌍 Мова | Спілкування, код, документація |
| [§2](#2--призначення) | 🎯 Призначення | Що робить застосунок |
| [§3](#3--принцип-роботи) | 🧠 Принцип роботи | Як агент підходить до задач |
| [§4](#4--пріоритети) | ⚖️ Пріоритети | Порядок важливості |
| [§5](#5--код) | 💻 Код | Як писати код |
| [§6](#6--не-ускладнювати) | 🧩 Не ускладнювати | Зайві абстракції |
| [§7](#7--архітектура) | 🏗️ Архітектура | Розділення відповідальності |
| [§8](#8--ui) | 🖥️ UI | Межі UI шару |
| [§9](#9--не-блокувати-ui) | ⚡ Не блокувати UI | Async/background |
| [§10](#10--api) | 🔌 API | Взаємодія з сервером |
| [§11](#11--api-contract-bdo-uacomua) | 📋 API Contract | Структура відповідей |
| [§12](#12--secrets) | 🔐 Secrets | Заборонені дані |
| [§13](#13--пошук-гри) | 🔍 Пошук гри | Game detection |
| [§14](#14--файли-ta-backup) | 💾 Файли та backup | Захист файлів, бекапи |
| [§15](#15--api-release-metadata) | 📦 API Release Metadata | Installation workflow |
| [§16](#16--захист-шляхів) | 🛡️ Захист шляхів | Path traversal |
| [§17](#17--download) | ⬇️ Download | Завантаження файлів |
| [§18](#18--стани-та-оновлення) | 🔄 Стани та оновлення | LocalizationState / OperationState |
| [§19](#19--відновлення-та-повернення-до-оригіналу) | 🗑️ Відновлення та повернення до оригіналу | Uninstall |
| [§20](#20--windows) | 🪟 Windows | Платформа |
| [§21](#21--ux) | 🎨 UX | Інтерфейс |
| [§22](#22--логування-та-exceptions) | 📝 Логування та exceptions | Обробка помилок |
| [§23](#23--конфігурація-та-cache) | ⚙️ Конфігурація та cache | Збереження даних |
| [§24](#24--мережа) | 🌐 Мережа | Retry, timeout |
| [§25](#25--залежності) | 📚 Залежності | Бібліотеки |
| [§26](#26--build-ta-exe) | 📦 Build та .exe | Збірка |
| [§27](#27--тести) | 🧪 Тести | Тестування |
| [§28](#28--security) | 🔒 Security | Захист |
| [§29](#29--сумісність) | 🔗 Сумісність | Patch compatibility |
| [§30](#30--правила-змін) | ✏️ Правила змін | Як вносити зміни |
| [§31](#31--коментарі-та-документація) | 📄 Коментарі та документація | Як документувати |
| [§32](#32--не-вигадувати) | 🚫 Не вигадувати | Що заборонено |
| [§33](#33--definition-of-done) | ✅ Definition of Done | Коли задача виконана |
| [§34](#34--звіт-та-коміти) | 📊 Звіт та коміти | Як комітити |
| [§35](#35--публічний-репозиторій) | 🔐 Публічний репозиторій | Безпека даних |
| [§36](#36--заборони) | 🚫 Заборони | Що не можна |
| [§37](#37--правило-сумнівів) | 🤔 Правило сумнівів | Надійність > швидкість |
| [§38](#38--головний-принцип) | 💎 Головний принцип | Головне правило |
| [§39](#39--плани) | 📋 Плани | Історичні плани |
| [§40](#40--архів-реліз-нотаток) | 📦 Архів реліз-нотаток | Збереження текстів релізів |
| [§41](#41--оновлення-клієнта) | 🔄 Оновлення клієнта | Client self-update |
| [§42](#42--persistent-development-context) | 🧭 Persistent development context | Engineering handoff та journal |
| [§43](#43--ai-development-workflow) | 🤖 AI Development Workflow | Orchestration, prompts, reviews та handoffs |

---

## §1 🌍 Мова

§1.1 Спілкування, коментарі, звіти — **тільки українською**.

§1.2 Англійська дозволена для: назв технологій, API, бібліотек, ідентифікаторів, системних термінів.

§1.3 Код — англійськими назвами змінних/функцій.

§1.4 Не змішувати українську з російською. Російську не використовувати.

---

## §2 🎯 Призначення

§2.1 Windows `.exe` застосунок для встановлення локалізацій у комп'ютерні ігри.

§2.2 Можливості:
- Автоматичний/ручний пошук гри
- Валідація директорії
- Отримання локалізацій через API
- Завантаження, встановлення, оновлення
- Перевірка стану, видалення/відновлення
- Повідомлення про помилки

---

## §3 🧠 Принцип роботи

§3.1 Агент працює як інженер: зрозуміти структуру → знайти пов'язані файли → зрозуміти data flow → визначити мінімум змін → редагувати.

§3.2 Не створювати нову архітектуру без потреби.

§3.3 Не переписувати великі частини заради дрібної зміни.

---

## §4 ⚖️ Пріоритети

§4.1 Коректність.

§4.2 Безпека даних.

§4.3 Неможливість пошкодження файлів гри.

§4.4 Стабільність.

§4.5 Простота підтримки.

§4.6 Зрозуміла архітектура.

§4.7 Хороший UX.

§4.8 Продуктивність.

§4.9 Краса коду.

§4.10 Не жертвувати коректністю заради коротшого коду.

§4.11 Не жертвувати надійністю заради швидкого завершення задачі.

---

## §5 💻 Код

§5.1 Код повинен бути: простим, читабельним, передбачуваним, типізованим.

§5.2 Розділений за відповідальністю.

§5.3 Без зайвих абстракцій та дублювання.

§5.4 Зрозумілі назви: `game_installation_path` ≠ `data`, `temp`, `obj`.

---

## §6 🧩 Не ускладнювати

§6.1 Не використовувати складні patterns без реальної потреби.

§6.2 Не створювати: зайві interfaces, зайві abstract classes, фабрики для одного об'єкта, dependency injection без необхідності, надмірно глибоку ієрархію класів.

§6.3 Кожна абстракція повинна вирішувати конкретну проблему.

§6.4 Якщо просте рішення достатнє — використовувати просте рішення.

---

## §7 🏗️ Архітектура

§7.1 Не змішувати в одному місці: UI / API / файлову роботу / пошук гри / установку / конфігурацію / логування.

§7.2 Бажане розділення:
```
UI → Application/Services → API Client → Game Detection
                                         → Localization Installer
                                         → File System
                                         → Configuration / Logging / Models
```

---

## §8 🖥️ UI

§8.1 UI показує інформацію та викликає service-методи.

§8.2 UI **не** повинен: будувати API-запити, копіювати файли, перевіряти структуру гри, реалізовувати алгоритм встановлення.

---

## §9 ⚡ Не блокувати UI

§9.1 Довгі операції (HTTP, download, scan, розпакування, копіювання, checksum) — в async/background.

§9.2 UI має залишатися responsive.

---

## §10 🔌 API

§10.1 Вся взаємодія через окремий API client (base URL, headers, timeout, serialization, error handling).

§10.2 Не вважати API ідеальним: обробляти timeout, DNS error, 4xx/5xx, порожню відповідь, malformed JSON.

§10.3 Відповіді перетворювати на моделі через `ApiResult<T>`. `null` НЕ використовується як generic signal failure.

§10.4 Base URL — централізовано (Config/Environment).

§10.5 API версію враховувати явно.

§10.6 API-контракт не є статичним. Сервер може додавати нові поля, змінювати структуру, вводити нові endpoint-и. При зміні API потрібно:
1. Оновити моделі клієнта.
2. Оновити розділ §11.
3. Перевірити backward/forward compatibility.

---

## §11 📋 API Contract: bdo-ua.com.ua

Base URL: `https://bdo-ua.com.ua/api/public/v1`

**GET /releases** — основний агрегований feed застосунку. Окремий latest endpoint повертає свіжі metadata одного режиму; клієнт не повинен сам вирішувати, яка локалізація актуальна.

§11.1 Структура відповіді:
```json
{
  "success": true,
  "generated_at": "ISO datetime",
  "data": {
    "official_patch": int,
    "official_patch_checked_at": "ISO datetime",
    "official_source_url": "https://naeu-o-dn.playblackdesert.com/.../languagedata_en.loc",
    "filename": "languagedata_en.loc",
    "install_path_patterns": [
      { "pattern": "{drive}:\\...\\Black Desert Online\\ads\\", "launcher": "steam|official", "description": "..." }
    ],
    "install_guide_url": "https://bdo-ua.com.ua/download",
    "progress": { "total_rows", "translated_percent", "manual_rows", "manual_percent", "machine_rows", "machine_percent" },
    "modes": [
      {
        "slug": "full-ukrainian|full-ukrainian-bosia|english-items",
        "public_name": "...",
        "description": "...",
        "audience": "...",
        "current": {
          "public_id": "ULID (26 chars)",
          "version": int,
          "filename": "languagedata_en.loc",
          "download_url": "https://bdo-ua.com.ua/download/releases/{public_id}",
          "size_bytes": int,
          "sha256": "hex string",
          "patch": int,
          "compatible_with_official_patch": bool,
          "published_at": "ISO datetime",
          "game_tested_at": "ISO datetime",
          "game_test": { "state": "verified|known_issues|unverified", "label": "...", "note": "string|null" },
          "stats": { "rows_in_file": int },
          "announcements": { "discord_releases": {"sent": bool, "sent_at": str|null}, "telegram_main": {"sent": bool, "sent_at": str|null} }
        },
        "history": [ { "public_id", "version", "patch", "status", "published_at", "retired_at" } ]
      }
    ]
  }
}
```

§11.2 `current` відсутній для режиму — нормальний стан (актуальний release ще не опубліковано). `current` є nullable. `current == null` — валідний бізнес-стан, не deserialization/API error.

§11.3 `history` використовувати лише для інформації. Завантажувати старі версії ЗАБОРОНЕНО.

§11.4 `official_source_url` — для відновлення оригіналу. SHA-256 для нього НЕ надається.

§11.5 `install_path_patterns` — лише hints для detection, не довірені filesystem instructions.

§11.6 Download: прямий (без redirect), авторизація не потрібна.

§11.7 `progress` — глобальний для всіх режимів. `stats.rows_in_file` може відрізнятися від `progress.total_rows`.

§11.8 Поточні канонічні режими сервера (slug):
- `full-ukrainian`
- `full-ukrainian-bosia`
- `english-items`
- `english-items-npc`
- `english-items-npc-world`
- `english-items-npc-world-pearl`
- `english-guide`
- `minimal`

Hub отримує режими динамічно з API і не використовує цей перелік як runtime whitelist. Сервер може сумісно додавати нові режими; невідомі JSON-поля клієнт ігнорує.

`current.game_test.state` має відомі значення `verified`, `known_issues` та `unverified`. Hub встановлює `verified` без додаткового підтвердження; для інших, відсутніх або невідомих майбутніх значень просить явне підтвердження.

§11.9 Статуси history: `superseded` / `withdrawn` ( `current` ніколи не з'являється в history ).

### Per-mode latest endpoint

`GET /api/public/v1/releases/latest/{slug}` повертає свіжі metadata одного режиму для перевірки актуальності перед встановленням/оновленням. Він не замінює агрегований `/releases`, що залишається основним джерелом списку режимів, progress, detection hints, restore metadata та polling.

Успіх `200` має envelope `{ "success": true, "generated_at": "...", "data": { "official_patch": ..., "filename": "...", "install_guide_url": "...", "mode": { "slug": "...", "public_name": "..." }, "current": ... } }`. `current` повторно використовує структуру `modes[].current` і може бути `null`: це валідний стан без доступного актуального релізу.

- Валідний `200` є свіжими авторитетними metadata, навіть якщо перед ним надіслано `If-None-Match`; це не вимагає відповіді `304`.
- `304` для відповідного `If-None-Match` означає, що можна використати кешовані latest metadata. Сам статус не означає, що локально встановлений реліз актуальний.
- `404`: `{"success":false,"error":"unknown_mode","allowed":[...]}`; запитаний slug невідомий.
- `503`: `Retry-After` у секундах і тіло на кшталт `{"success":false,"error":"official_patch_unconfirmed","message":"..."}`; офіційний патч ще не підтверджений. Не виконувати автоматичний retry у клієнті.

### Permanent latest download link

`GET /download/latest/{slug}` перенаправляє `302` на поточний `current.download_url`; коли поточного файлу немає, невідомий slug або patch не підтверджено — на `/download`. Hub installer продовжує завантажувати immutable `current.download_url` з перевіркою розміру/SHA-256, а не рухоме `/download/latest/{slug}`, щоб metadata та bytes належали тому самому релізу.

Для install/update Hub спершу перевіряє latest metadata для вибраного режиму; збої перевірки блокують операцію без fallback до stale aggregate metadata. `game_test.state == verified` проходить без попередження; усі інші, null або невідомі значення вимагають явного підтвердження.

### Winds4UA / Where Winds Meet API

WWM має окремий dynamic contract; не десеріалізувати його в BDO `ReleasesResponse` і не розширювати `BdoUaApiClient` до універсального клієнта.

`GET https://winds4ua.com.ua/api/public/v1/releases/latest` повертає `success`, `generated_at`, nullable `data.current` і `data.modes[]`. Mode identity — `slug + variant`; лише mode з `available=true` та рівно одним matching `current.files[]` artifact із валідними абсолютним HTTPS URL, size і SHA-256 можна встановлювати. Невідомі JSON fields/modes мають лишатися forward-compatible; unavailable mode з порожнім URL/null size/hash не є installable. Не використовувати top-level current URL, history, label або array order для package selection.

Steam WWM identity — AppID `3564740`; automatic discovery використовує Steam library metadata та `appmanifest_3564740.acf`, а root validation — `Engine/Binaries/Win64r/wwm.exe` і `Package/HD/oversea/locale/`. Steam build ID є observational та snapshot-staleness metadata, не compatibility mapping. Fresh latest feed обов'язковий перед кожною WWM mutation. ZIP дозволено staging після HTTPS/size/SHA verification; приймаються лише дві allow-listed game-root-relative locale files, без direct extraction або script execution.

WWM install state/transaction — окрема game-scoped versioned schema. До першої mutation зберігати pre-Hub existence/bytes/size/SHA кожного target; per-operation journal відновлює обидва targets і попередній state після partial failure. Restore означає exact **pre-Hub state**, не guaranteed stock, і має називатися `Відновити попередній стан`. Official launcher deferred; compatibility із конкретним Steam build API не підтверджує, тому перед mutation потрібне нейтральне явне підтвердження.

---

## §12 🔐 Secrets

§12.1 Ніколи не додавати в репозиторій: API keys, tokens, passwords, signing secrets, credentials.

§12.2 Desktop `.exe` не може надійно приховати секрет.

---

## §13 🔍 Пошук гри

§13.1 Game detection — окремий модуль.

§13.2 Порядок: збережений шлях → registry → Steam libraryfolders → appmanifest_582660.acf → `install_path_patterns` з API (hints) → ручний вибір.

§13.3 Steam detection: читати `libraryfolders.vdf`, знаходити `appmanifest_582660.acf`, витягувати `installdir`.

§13.4 `install_path_patterns` з API — лише hints для перебору дисків, не довірені filesystem instructions.

§13.5 Директорія валідується: наявність `{game_path}\ads\languagedata_en.loc`.

§13.6 Ручний вибір завжди доступний.

§13.7 WWM Steam-first detection читає Steam library metadata та `appmanifest_3564740.acf`; автоматично приймає manifest-derived root лише з `Engine/Binaries/Win64r/wwm.exe` і `Package/HD/oversea/locale/`. Ручний вибір застосовує ті самі product markers. Official launcher detection не підтримується. Steam `buildid` не є localization compatibility version.

---

## §14 💾 Файли та backup

§14.1 Перед зміною/видаленням файлу знати, як його відновити.

§14.2 Бекап створювати один раз, не перезаписувати good backup модифікованим файлом.

§14.3 Атомарна установка: download у temp → validation → backup → apply → verify → cleanup.

§14.4 Temporary files: `.tmp`/`.download`, переміщати після перевірки.

§14.5 Перевірка hash (SHA-256) при наявності від сервера.

§14.6 **Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

§14.7 **Metadata original snapshot:** `created_at`, `game_patch` (якщо достовірно), `sha256` (локально, не з API), `size_bytes`.

§14.8 **Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback
4. `Відновити backup` — повернення до попереднього restore point

§14.9 Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

§14.10 Для WWM два managed targets — `Package/HD/oversea/locale/translate_words_map_en` і `_diff`. Перед першою Hub mutation зберігаються попереднє існування, exact bytes, size і SHA-256 кожного файла; кожна наступна mutation також має окрему rollback точку та durable journal. Rollback/restore повертає точні pre-operation/pre-Hub bytes або видаляє файл, якого раніше не існувало. Не називати це відновленням stock/original.

---

## §15 📦 API Release Metadata

API надає release metadata через `GET /releases`. Клієнт виконує валідовані операції.

§15.1 **Installation Safety Workflow:**
1. Для install/update отримати свіжий latest release вибраного режиму; не використовувати aggregate `current` як fallback при помилці перевірки.
2. Завантажити immutable `current.download_url` у cache/temp
3. Перевірити HTTP result
4. Перевірити `size_bytes` (якщо доступний)
5. Перевірити SHA-256 (для release files; для official source — ні)
6. Створити pre-operation snapshot/restore point
7. Підготувати заміну
8. Замінити game file
9. Перевірити встановлений файл
10. Записати installation state ТІЛЬКИ після успіху
11. Видалити temporary file
12. Commit success

§15.2 Якщо помилка на будь-якому кроці до replace — файл гри НЕ змінено, metadata НЕ стверджує, що release встановлено.

§15.3 Якщо помилка сталася після replace — клієнт повинен спробувати rollback до pre-operation snapshot. Якщо rollback не вдався:
- не заявляти успішну установку;
- стан вважати пошкодженим (`Corrupted`);
- показати користувачу критичну помилку;
- записати деталі в log.

§15.4 WWM install/update використовує окремий latest endpoint/client і два-file transaction; BDO aggregate/poller/installer schemas не змінюються. Fresh `data.modes[]` package за `slug + variant` проходить HTTPS, outer size/SHA і bounded ZIP allow-list перевірки у staging до mutation. Кожен staged target повторно перевіряється за size/SHA після запису. WWM target set змінився — операція fail-closed до окремого contract review.

---

## §16 🛡️ Захист шляхів

§16.1 Path traversal protection: нормалізація + перевірка, що файл в межах дозволеної директорії.

§16.2 Не довіряти filenames від API: не писати в system directories, не запускати executable.

---

## §17 ⬇️ Download

§17.1 Підтримка: timeout, progress, cancellation, error handling, partial cleanup, hash check, streaming для великих файлів.

---

## §18 🔄 Стани та оновлення

§18.1 Перевіряти фактичний стан файлів, а не лише config flag.

§18.2 Перед update: поточна версія, серверна версія, сумісність.

§18.3 `compatible_with_official_patch == false` → Install та Update заборонені, download не починається.

§18.4 **LocalizationState (постійний стан файлу):**
- `NotInstalled` — installation.json відсутній; АБО metadata valid і `source == "official"`
- `UpToDate` — `installed.public_id == current.public_id` (ordinal exact string comparison)
- `UpdateAvailable` — `installed.public_id != current.public_id`
- `WaitingForRelease` — встановлено, hash збігається, але `current` відсутній; якщо `current != null` але `PublicId` null/empty/whitespace — теж `WaitingForRelease`, але result містить diagnostic Error
- `InstalledVersionUnknown` — installation.json існує, але `InstallationStateStore.Load()` повертає Invalid
- `Corrupted` — API metadata valid, але фактичний файл missing/unreadable, inconsistent або hash mismatch без достовірного пояснення; hash mismatch при фактичному `ads_files` patch, новішому за `InstallationMetadata.GamePatch`, є нормальною transition-подією і не є `Corrupted`

§18.5 **OperationState (тимчасовий стан операції):**
- `Idle` / `DetectingGame` / `LoadingApi` / `Downloading` / `Verifying`
- `BackingUp` / `Installing` / `Restoring` / `Completed` / `Failed` / `Cancelled`

§18.6 Основний ідентифікатор release — `public_id`, а не `patch` чи `version`.

§18.7 **Product decision:** Існує один underlying install/update flow. Кожна API-driven картка режиму показує контекстну дію: `Встановити` для першої установки або зміни режиму, `Оновити` — лише для новішого release вже встановленого того самого режиму. Exact current target показує `✓ Встановлено` без дії. Restore Backup є внутрішньою safety capability, не public UI.

---

## §19 🗑️ Відновлення та повернення до оригіналу

§19.1 **Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

§19.2 **Metadata original snapshot:** `created_at`, `game_patch` (якщо достовірно), `sha256` (локально, не з API), `size_bytes`.

§19.3 **Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback ТІЛЬКИ якщо `snapshot.game_patch == current.official_patch`
4. `Відновити backup` — повернення до попереднього restore point

§19.4 Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

---

## §20 🪟 Windows

§20.1 Коректні Windows paths, Unicode, пробіли, permissions, read-only/locked files.

§20.2 Не вимагати Admin без потреби. Permission error → зрозуміле повідомлення.

---

## §21 🎨 UX

§21.1 Простий інтерфейс: де гра, яка локалізація, версія, стан, прогрес, результат.

§21.2 Помилки — зрозумілою мовою, деталі в log.

---

## §22 📝 Логування та exceptions

§22.1 Структуроване логування (DEBUG/INFO/WARNING/ERROR).

§22.2 Логувати: запуск, detection, API errors, download failures, installation stages, exceptions.

§22.3 Не логувати: passwords, tokens, secrets.

§22.4 **Заборонено** порожні `catch/pass`. Кожна помилка обробляється/логується/re-throw.

---

## §23 ⚙️ Конфігурація та cache

§23.1 User settings у Windows user-data location (не поруч з `.exe` в Program Files).

§23.2 Cache окремо від game files. Не змішувати cache/backup/config/logs/game files.

§23.3 **Структура даних (%LocalAppData%\BDO-UA-Client\):**
```
BDO-UA-Client/
├── config.json
├── state/
│   └── installation.json
├── logs/
├── cache/
└── backups/
    ├── original/
    └── restore-points/
```

---

## §24 🌐 Мережа

§24.1 Retry тільки для безпечних операцій, з backoff та лімітом.

§24.2 Кожна мережева операція має timeout.

---

## §25 📚 Залежності

§25.1 Не додавати dependency, якщо standard library справляється.

§25.2 Не змінювати framework/build system/package manager без потреби.

---

## §26 📦 Build та .exe

§26.1 Після змін: syntax check → build → tests → виправлення.

§26.2 Пакетований `.exe` може відрізнятися від dev mode (working directory, bundled resources).

§26.3 **GitHub Actions CI:**
- Локальні build/tests перед commit залишаються обов'язковими.
- GitHub Actions є незалежною remote-перевіркою.
- Не заявляти, що CI успішний, якщо результат реально не перевірено.
- Failure CI не ігнорувати перед переходом до наступного етапу.

§26.4 Для meaningful UI/visual implementation tasks, де потрібне owner visual approval, automated build/tests недостатні для handoff: перед ручним review агент зобов'язаний створити local runnable preview build. Preview не замінює build/tests або CI; GitHub Actions artifacts не є обов'язковою залежністю для локальної visual inspection, якщо можливий local publish. Preview artifacts є тимчасовим локальним output і ніколи не commit-яться.

§26.5 Якщо локальний GitHub CLI (`gh`) автентифікований, Implementation Agent може використовувати його для дозволених task-ом операцій із цим repository та GitHub Actions. Перед операцією перевірити `gh auth status`, repository/remote і exact ref/SHA; токени, credentials і повні auth-виводи не друкувати, не зберігати в repository та не включати у звіти. Якщо task не дозволяє мутацію — використовувати лише read-only GitHub/API/CI перевірки.

§26.6 Коли Implementation Agent працює у Windows-середовищі з доступним native UI control, він може самостійно виконати й задокументувати механічну перевірку застосунку: зібрати та запустити WinForms UI, перевірити runtime-сценарії, перемикання екранів/ігор і tray/window lifecycle, переглянути логи, запустити автоматизовані тести та зберегти screenshots як evidence. У звіті вказувати точні source/commit SHA, фактично запущений EXE, сценарії, PASS/FAIL і шляхи screenshots. Не вимагати від Owner повторення суто механічної перевірки, якщо агент може безпечно виконати її та надати evidence. Architect зберігає незалежний technical review; Owner — суб'єктивне product/visual acceptance та рішення про publication/release. Owner input залишається обов'язковим для явно визначених Owner gates і рішень, що потребують суб'єктивної оцінки. Native smoke не надає дозволу змінювати реальні game files, user data, external services, release/publication state чи інші destructive/high-risk ресурси: без явної авторизації використовувати read-only дії, synthetic/temp fixtures або зупинятися перед destructive confirmation boundary. Screenshots не повинні без потреби розкривати приватні дані.

---

## §27 🧪 Тести

§27.1 Тестувати без UI: path validation, release metadata parsing, checksum, game detection, version comparison.

§27.2 Tests використовують temp directories, не працюють з реальними game files.

§27.3 Edge cases: гра не знайдена, кілька копій, Unicode/пробіли, locked files, переповнений диск, перерваний download, пошкоджені файли.

§27.4 Test project: `BdoClient.Tests`. Framework: xUnit (test-only NuGet dependency). Не тестувати WinForms layout. TargetFramework: `net8.0-windows` (для ProjectReference на WinForms app).

§27.5 Тести додавати разом з testable logic (Етапи 1-7), а не відкладати все до фіналу.

---

## §28 🔒 Security

§28.1 Усе зовнішнє — недовірене: API responses, filenames, URLs, API release metadata.

§28.2 TLS verification ніколи не вимикати.

§28.3 Не запускати executable отриманий з сервера (окрім whitelist операцій: copy/replace localization file/create directory/restore backup/delete temporary/cache files). Основний `languagedata_en.loc` не видаляється фізично як uninstall. Повернення до стану без української виконується через `Restore Original`.

§28.4 Невідомі operation відхиляти.

---

## §29 🔗 Сумісність

§29.1 `compatible_with_official_patch == false` → Install та Update заборонені. Download не починається. Користувачу показується причина. Ніякого override в MVP.

---

## §30 ✏️ Правила змін

§30.1 Мінімальний patch. Не змішувати feature/refactor/formatting.

§30.2 Не чіпати непов'язаний код.

§30.3 Refactor не змінює поведінку ненавмисно.

§30.4 Не залишати TODO замість реалізації.

§30.5 Не підміняти реалізацію placeholder/mock.

§30.6 Не hardcode: шляхи, usernames, tokens, credentials.

---

## §31 📄 Коментарі та документація

§31.1 Коментарі пояснюють **чому**, а не **що**.

§31.2 Оновлювати документацію при зміні build/config/API/structure.

§31.3 Невідомий код — дослідити перед зміною.

---

## §32 🚫 Не вигадувати

§32.1 Використовувати існуючий API contract.

§32.2 Не вигадувати API бібліотек.

§32.3 Складні задачі — маленькими кроками.

---

## §33 ✅ Definition of Done

Задача виконана, коли:

§33.1 Реалізована функціональність без placeholder.

§33.2 Оброблені error cases.

§33.3 UI responsive.

§33.4 Файлові операції захищені.

§33.5 Код відповідає архітектурі.

§33.6 `dotnet build BdoUaClient.sln` проходить без помилок.

§33.7 `dotnet test BdoUaClient.sln --no-build` проходить (якщо є тести).

---

## §34 📊 Звіт та коміти

§34.1 Коротко: що змінено, ключові файли, що перевірено, build/tests, обмеження. Не заявляти "все працює" без перевірки.

§34.2 **Правила комітів:**
- Формат першого рядка: `v{ЕТАП}.{ПІДЕТАП} — {короткий опис}`
- Після першого рядка — порожній рядок, потім **детальний опис** того, що зроблено
- Опис містить: що реалізовано, що змінено, що оновлено, чому саме так (якщо неочевидно)
- Кожен завершений етап/підетап — окремий коміт
- Деталі у [`docs/ai-workflow/HANDOFF.md`](docs/ai-workflow/HANDOFF.md) та, коли застосовно, у relevant ACTIVE plan.

§34.3 **Звіт після коміту/пушу — ОБОВ'ЯЗКОВИЙ.** Після кожного коміту та пушу агент повинен чітко повідомити:

1. **Що закомічено:** короткий опис змін
2. **Який коміт message:** повний текст коміту
3. **Які файли змінено:** список
4. **Що запушено:** підтвердження push + branch
5. **Hash коміту:** короткий hash

Приклад звіту:
```
✅ Коміт створено та запушено.

📝 Commit message: v1.0 — project skeleton + API models + API client + tests

Створено проект з нуля:
- BdoUaClient.sln з двома проектами (BdoClient.csproj + BdoClient.Tests.csproj)
- WinForms skeleton (Program.cs, MainForm.cs, MainForm.Designer.cs)
- API models на основі фактичного /releases endpoint (ReleasesResponse, LocalizationMode, CurrentRelease тощо)
- ApiResult<T> — простий Result pattern без зовнішніх залежностей
- BdoUaApiClient — HttpClient + base URL + CancellationToken + timeout + error handling
- ILogger contract — мінімальний logging interface
- 14 unit tests (JSON deserialization, null current, malformed JSON, empty response, HTTP errors)

Виправлено §28.3 — прибрано "delete localization file" з whitelist.

📁 Змінені файли:
- AGENTS.md
- BdoUaClient.sln
- BdoClient.csproj
- BdoClient.Tests/BdoClient.Tests.csproj
- BdoClient.Tests/GlobalUsings.cs
- BdoClient.Tests/Api/ApiResultTests.cs
- BdoClient.Tests/Api/BdoUaApiClientTests.cs
- BdoClient.Tests/Models/ReleasesResponseTests.cs
- Api/ApiResult.cs
- Api/BdoUaApiClient.cs
- Models/ReleasesResponse.cs, ReleaseData.cs, LocalizationMode.cs, CurrentRelease.cs, ReleaseHistoryItem.cs, InstallPathPattern.cs, GameTestInfo.cs, ProgressInfo.cs, StatsInfo.cs, AnnouncementsInfo.cs
- Logging/ILogger.cs
- Program.cs, MainForm.cs, MainForm.Designer.cs

🔖 Hash: 1bd50b6
🌿 Branch: main → origin/main
```

§34.4 Не залишати незакомічені файли без уваги. Якщо є untracked/modified файли — агент повинен або закомітити, або явно повідомити про них.

---

## §35 🔐 Публічний репозиторій

§35.1 Репозиторій **публічний**. Все, що потрапляє в git, стає доступним для всіх.

§35.2 **Перед кожним комітом агент зобов'язаний перевірити `git status` та `git diff` на наявність:**
- API keys, tokens, secrets
- Паролі, credentials
- Приватні ключі (SSH, TLS)
- Access tokens (GitHub, AWS, інші)
- Будь-які дані, які не повинні бути публічними

§35.3 **ЗАБОРОНЕНО** додавати в коміт:
- `.env` файли
- Файли з секретами навіть для "тимчасового" зберігання
- Будь-які credentials, отримані від користувача
- Token-и, створені для тестування

§35.4 Якщо випадково секрет потрапив в коміт:
1. Не пушити (якщо ще не запушено)
2. Видалити секрет з коду
3. Перезаписати git history (якщо вже запушено) — повідомити користувача
4. Попередити про необхідність ротації скомпрометованого секрету

§35.5 `.gitignore` повинен містити типові виключення для .NET проекту та секретів.

---

## §36 🚫 Заборони

Без прямої необхідності не:

§36.1 Видаляти великі частини проєкту.

§36.2 Змінювати framework/architecture/API contract.

§36.3 Оновлювати всі dependencies.

§36.4 Форматувати весь repository.

§36.5 Відключати security/TLS.

§36.6 Приховувати помилки.

---

## §37 🤔 Правило сумнівів

§37.1 Між швидким-небезпечним та складнішим-надійним — обирати надійне. Особливо для game files, backup, rollback, download validation, path handling.

---

## §38 💎 Головний принцип

§38.1 Користувач запускає → знаходить гру → вибирає локалізацію → встановлює без розуміння internals.

§38.2 **Ніколи не залишати гру у пошкодженому стані заради "успішної" операції.**

---

## §39 📋 Плани

§39.1 `docs/plans/README.md` — canonical plans registry. Реєстр містить ACTIVE, BACKLOG та ARCHIVED плани.

§39.2 Implementation plans live only in `docs/plans/active/`, `docs/plans/backlog/`, `docs/plans/archive/`. No canonical `/plan.md` in repository root.

§39.3 Якщо існують ACTIVE плани, рівно один із них має бути позначений PRIMARY. Нуль ACTIVE планів є валідним, коли немає затвердженої executable roadmap; не створювати placeholder plan лише для bookkeeping. BACKLOG plans must NOT be implemented automatically. ARCHIVED plans are historical references only.

§39.4 Moving lifecycle state requires file move + registry update in same commit. Folder status and registry status must always match.
§39.5 Після кожного meaningful implementation commit, що просуває ACTIVE план, той самий commit повинен оновити відповідний active plan і `docs/plans/README.md`, правдиво зафіксувавши реалізовану задачу, фактичний validation/review status, невиконані частини та точну наступну дію.
§39.6 Implementation-agent не має права позначати власну роботу як `REVIEWED`, `ACCEPTED` або `OWNER ACCEPTED`, якщо відповідний зовнішній review/acceptance фактично не відбувся. До нього використовувати фактичні статуси на кшталт `IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW`.
§39.7 Звичайне plan/progress bookkeeping належить implementation commit, який спричинив прогрес. Не створювати пізніший docs-only commit для виправлення застарілого плану; окремий corrective docs-only commit дозволений лише для виправлення вже закоміченої фактичної невідповідності.
§39.8 Registry/plan не може продовжувати вказувати вже завершену задачу як current/next.

---

## §40 📦 Архів реліз-нотаток

§40.1 Текст кожного релізу зберігати у `docs/releases/vX.Y.Z.md` (де `X.Y.Z` — версія релізу).

§40.2 Файл створюється на основі `docs/releases/RELEASE_TEMPLATE.md` та заповнюється конкретними даними релізу.

§40.3 Поле `{{SHA256}}` заповнюється після завершення Release Candidate workflow (береться з artifact).

§40.4 Архів реліз-нотаток зберігається в репозиторії як історична довідка. Кожен реліз — окремий файл.

§40.5 `docs/releases/NEXT.json` — canonical structured source для public copy наступного релізу. User-facing implementation task оновлює relevant fragments у тому самому implementation commit.

§40.6 Git history є provenance, а не public release prose. Generator не виводить основний public copy з commit subjects.

§40.7 CI release-note generation має бути deterministic і не залежати від LLM, API або secrets. Internal plan/review/task labels не повинні потрапляти в public notes.

§40.8 Після стабільної публікації post-release finalization архівує факти релізу та скидає `NEXT.json` для наступної розробки.

---

## §41 🔄 Оновлення клієнта

§41.1 Джерело оновлень — публічні GitHub Releases канонічного репозиторію `merelyigor/ua-localization-hub`; legacy slug `merelyigor/bdo-ua-client` дозволений лише як compatibility fallback. Не використовувати bdo-ua.com.ua, custom backend, або GitHub token.

§41.2 Перевірка оновлень — автоматична у background одразу після startup lifecycle maintenance і приблизно кожні 5 хвилин, поки процес працює, включно з tray/background. Restore та tray `Перевірити зараз` можуть запросити негайну перевірку. Запити single-flight, не перекриваються, не блокують UI, game detection або API loading і не завантажують/встановлюють оновлення автоматично.

§41.3 Встановлення оновлення вимагає explicit натискання кнопки "Оновити до vX.Y.Z". Ніякого silent/forced update.

§41.4 Порівняння версій — тільки numeric (0.1.9 < 0.1.10). Ніколи lexicographic string comparison.

§41.5 Channel policy: якщо current release prerelease=true → дозволити newer prerelease + stable; якщо prerelease=false → тільки newer stable.

§41.6 Перед зміною current EXE: manifest + SHA-256 direct EXE validation + EXE version verification. Current EXE untouched до моменту, поки verified candidate не готовий.

§41.7 Staged new EXE сам виконує internal updater mode (`--apply-update <session-id>`). Не додавати permanent Updater.exe, PowerShell/BAT updater, Windows Service.

§41.8 Backup перед replace. Rollback при будь-якій помилці replacement/verification/restart. НІКОЛИ: "old deleted, new not installed".

§41.9 Application update та localization operations (Install, Restore Original) взаємовиключні.

§41.10 TLS verification ніколи не вимикати. Не використовувати GitHub token. Не використовувати HTTP.

§41.10.1 Final production application releases use exactly one GitHub-generated application ZIP: `BDO-UA-Client-vX.Y.Z-win-x64.zip`. The Actions artifact contains exactly four flat files: `BDO-UA-Client.exe`, `release-manifest.json`, `SHA256SUMS.txt`, and `RELEASE_NOTES-vX.Y.Z.md`; there is no project-created nested ZIP. The schema-2 manifest is internal to the bundle and has no outer ZIP hash. The public GitHub Release uploads only the exact artifact ZIP; standalone EXE, manifest, sums, and release notes are not separate release assets. GitHub asset digest validates the outer ZIP, while the updater validates internal EXE SHA-256 and version metadata. No `.7z`, `.rar`, `.tar`, `.tar.gz`, nested archive, or arbitrary package paths. Schema 1 releases and historical direct-EXE releases/manifests remain supported, but clients older than ZIP-support may require manual update. v0.1.8+ supports canonical ZIP transport; v0.1.13+ supports the schema-2 bundle.

§41.11 Stage 13 first implementation: NO automatic UAC elevation.

§41.12 Detailed implementation roadmap живе тільки у canonical `docs/plans/active/client-self-update.md` (розділ Stage 13).

---

## §42 🧭 Persistent development context

§42.1 `docs/development/CURRENT.md` — canonical living engineering handoff. Після кожної завершеної meaningful implementation task агент зобов'язаний визначити, чи потребує він оновлення.

§42.2 `docs/development/history/YYYY-MM.md` — append-only chronological journal. Після кожної завершеної meaningful implementation task агент додає один стислий engineering entry до поточного місячного файлу.

§42.3 Оновлення `CURRENT.md` і journal повинні входити до **того самого implementation commit**, що й відповідні зміни. Окремі "update changelog" commits заборонені.

§42.4 Journal не дублює raw Git diffs. Він фіксує лише problem, decision, changed areas, validation, consequences та follow-up.

§42.5 Meaningful implementation task включає feature implementation, bug fix, architecture decision, API behavior change, storage/state behavior change, security behavior change, networking behavior change, build/release workflow change або significant investigation, що призвела до persistent project decision. Minor typo-only documentation edits journal entry не потребують.

§42.6 `CURRENT.md` — не append-only і має зазвичай залишатися меншим приблизно за 400 рядків. Старий контекст можна видаляти з нього лише після перевірки, що потрібна історична інформація вже є в monthly journal або Git history.

§42.7 Один journal-файл створюється на календарний місяць у форматі `YYYY-MM.md`. При першій завершеній meaningful task нового місяця створюється новий файл. Попередні monthly files не переписуються, окрім factual corrections.

§42.8 Не створювати довільні journal files на кшталт `DEVLOG-2.md` і не видаляти старі monthly files автоматично. Git залишається джерелом точних історичних diff.

§42.9 Persistent context є публічним repository content. Journal і CURRENT **ніколи** не повинні містити passwords, API keys, tokens, credentials, private URLs, private user information, personal ChatGPT conversation details, raw sensitive logs або local environment secrets. Дозволені лише repository-relevant engineering facts.

§42.10 Не дублювати в `CURRENT.md` точний поточний Git HEAD/commit/version, якщо сам documentation commit зробить це значення недійсним. Git history є canonical source для поточного repository HEAD; version references дозволені, коли описують meaningful historical/runtime baseline або architectural milestone.

---

## §43 🤖 AI Development Workflow

§43.1 Детальний canonical orchestration contract живе у [`docs/ai-workflow/README.md`](docs/ai-workflow/README.md) та linked files у цьому каталозі.

§43.2 Roles є vendor-neutral: Owner, Architect / Analyst / Reviewer, Implementation Agent. Implementation Agent — executor; task-level architecture/review authority належить external Architect/Reviewer, якщо Owner явно не вирішив інакше.

§43.3 Implementation Agent перевіряє repository context/baseline і дотримується approved scope. При unapproved API/schema/dependency/framework/architecture/safety/scope expansion або конфлікті з mandatory rules він STOP і звітує.

§43.4 Local autonomous decisions дозволені лише якщо не змінюють architecture, correctness, safety, compatibility, persistence/public contracts.

§43.5 Коли workflow вимагає acceptance, implementation follow-up проходить external review. Implementation Agent не може self-mark нову роботу як `REVIEWED / ACCEPTED`.

§43.6 Unresolved BLOCKER/IMPORTANT блокують dependent next work. OPTIONAL не вимагає corrective iteration автоматично.

§43.7 External conversations є coordination channels, а не canonical persistent state. Material decisions синхронізуються у repository-owned sources, де це доречно.

§43.8 Нова session відновлює context із repository documentation/source, а не з hidden/opaque session history; canonical bootstrap описано у [`docs/ai-workflow/README.md`](docs/ai-workflow/README.md).

§43.9 **Combined mode** є default для bounded low/medium-risk задач із визначеним scope: `Architect prompt → Implementation Agent implement + tests/docs/plan sync + validation + commit/push + CI → один external Architect review`. Цільовий цикл — один implementation prompt і один external review.

§43.10 Pre-commit review mode використовується лише для реально high-risk змін: destructive/data-loss risk, security-critical behavior, schema/data migration, public API redesign, architecture/framework change або іншого випадку, де commit до review створює суттєвий ризик. Окремий finalization prompt не створюється, якщо Implementation Agent уже має право commit/push і всі required gates пройдені.

§43.11 Corrective iteration дозволена лише для `BLOCKER`, `IMPORTANT`, failed required validation або material baseline mismatch. `OPTIONAL` сам по собі не створює новий prompt.

§43.12 Після завершення approved work, коли немає unresolved `BLOCKER`/`IMPORTANT`, already-approved dependent next step і активного незавершеного roadmap, кінцевий стан: `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`. Architect повинен STOP і не створювати автоматично audit, roadmap, refactoring, feature, release cycle або наступний task. Новий development cycle починає Owner.

§43.13 Для UI/visual changes commit/push дозволені у Combined mode, але Owner native/visual smoke може залишатися окремим acceptance або release gate, якщо automated evidence недостатній. Task lifecycle і release lifecycle не змішуються: task — `IMPLEMENTED → VALIDATED → PENDING EXTERNAL REVIEW → REVIEWED / ACCEPTED`; release — `RC READY → OWNER SMOKE ACCEPTED → RELEASED → PUBLIC VERIFIED → RELEASE REVIEWED / ACCEPTED`.

§43.14 Actual repository/diff/CI/test/artifact evidence має пріоритет. Structured agent evidence з exact SHA, run IDs і hashes є допустимим fallback лише за тимчасової недоступності external connector; prose без evidence недостатній. Plan створюється лише для справжнього multi-step roadmap або роботи, яку потрібно переносити між сесіями; bounded task не створює plan автоматично. Implementation prompt посилається на current `AGENTS.md` і `docs/ai-workflow/` та повторює лише task-specific scope, invariants і validation.

§43.15 `docs/ai-workflow/PROJECT_CHAT_RULES.md` — portable bootstrap prompt для Architect Project Instructions, а не повний handbook. Його hard limit — `7500` UTF-16 code units; detailed rules залишаються в `AGENTS.md` та інших `docs/ai-workflow/*.md`. Не дублювати canonical docs лише для self-contained prose.

§43.16 Після кожної зміни `PROJECT_CHAT_RULES.md` Implementation Agent зобов'язаний запустити `scripts/Check-ProjectChatRules.ps1`; цей gate також є обов'язковим кроком normal CI.

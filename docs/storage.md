# Storage — шар зберігання стану та конфігурації

Відповідальність: зчитування/запис конфігурації, стану встановлення, backup-файлів та restore points. UI та сервіси не працюють з файловою системою напряму — використовують класи з цього шару.

---

## AppPaths

**Файл:** `Storage/AppPaths.cs`

Централізований доступ до application-global шляхів. Root-директорія — `%LocalAppData%\BDO-UA-Client`. Game-specific config, installation state і backups належать `GamePersistencePaths`.

### Властивості

| Властивість | Шлях | Призначення |
|---|---|---|
| `Root` | `%LocalAppData%\BDO-UA-Client` | Коренева директорія |
| `LogsDir` | `{root}\logs` | Лог-файли |
| `CacheDir` | `{root}\cache` | Тимчасові завантаження та legacy compatibility residue |
| `UpdatesDir` | `{root}\updates` | Self-update сесії (staged candidate EXE, див. docs/update.md) |
| `ApplicationConfigFile` | `{root}\application-config.json` | Application-global settings, зокрема autostart prompt state |
| `StateDir`, `BackupsDir`, `ConfigFile`, `InstallationFile` | legacy root paths | Лише legacy BDO compatibility/migration |

### Конструктори

- **`AppPaths()`** — default root = `Path.Combine(LocalAppData, "BDO-UA-Client")`
- **`AppPaths(string root)`** — кастомний root (для тестів)

### EnsureGlobalDirectories

```csharp
public void EnsureGlobalDirectories()
```

Створює лише global `LogsDir`, `CacheDir` та `UpdatesDir`. Викликається перед legacy migration.

`EnsureDirectories()` збережено для compatibility fixtures, але production composition його не використовує.

## GamePersistencePaths

**Файл:** `Storage/GamePersistencePaths.cs`

`new GamePersistencePaths(appPaths.Root, gameDefinition.Id)` будує canonical scope:

```
{root}\games\{stable-game-id}\
├── config.json
├── cache\
│   └── release-feed.json
├── state\installation.json
└── backups\
    ├── original\
    └── restore-points\
```

Game id перевіряється як контрольований lowercase path segment. Тому різні stable ids не можуть ділити state, а user-controlled path traversal не проходить.

`ConfigStore`, `InstallationStateStore` і `BackupStore` у production отримують саме цей scope. `ApplicationConfigStore` залишається на global `application-config.json`; app-global settings не дублюються в game scopes.
`ReleaseFeedCacheStore` також отримує цей scope і читає/пише лише `{root}\games\{stable-game-id}\cache\release-feed.json`. Cache schema та display-only/degraded semantics не змінюються.

Historical `{root}\cache\release-feed.json` є legacy BDO cache. Під час BDO composition він best-effort імпортується в canonical BDO scope лише якщо scoped cache відсутній; source не видаляється, а scoped cache authoritative. Synthetic/інші game scopes legacy BDO cache не читають.

## ApplicationConfigStore

**Файл:** `Storage/ApplicationConfigStore.cs`

Зберігає лише application-global settings, які не належать окремій грі (`autostart_prompt_dismissed` і optional `selected_game_id`). Запис використовує той самий atomic tmp → replace/move pattern, але файл не входить до game scope. Відсутній ID означає зареєстровану BDO-гру; невідомий ID безпечно fallback-иться до неї та ремонтується best-effort без втрати інших settings.

## LegacyBdoPersistenceMigrator

**Файл:** `Storage/LegacyBdoPersistenceMigrator.cs`

Історичні `{root}\config.json`, `{root}\state` та `{root}\backups` трактуються лише як BDO legacy data. Legacy `config.json` розкладається у game-scoped `config.json` (game path/last mode) і global `application-config.json` (autostart prompt). Файлові каталоги переносяться same-volume move лише коли canonical destination відсутній. Якщо обидва існують, canonical state authoritative, merge/overwrite не виконується. Malformed або unreadable legacy `config.json` зберігається без змін і логується, але не блокує незалежну міграцію валідних `state`/`backups`; canonical config з нього не створюється. Реальні filesystem failure під час write/move/verification, які роблять ownership небезпечним, залишаються fatal/fail-closed. Повторний запуск після partial або успішної міграції є idempotent.

### Updates directory (self-update)

`UpdatesDir` використовується `UpdateSessionStore` (Update/ шар): кожна сесія оновлення — піддиректорія `{root}\updates\<GUID>\` з `update-session.json` та staged candidate EXE. Детальніше — [update.md](update.md).

---

## ConfigStore

**Файл:** `Storage/ConfigStore.cs`

Зчитування та збереження `config.json`.

### Модель Config

**Файл:** `Storage/Config.cs`

| Поле | JSON-назва | Тип | Опис |
|---|---|---|---|
| `GamePath` | `game_path` | `string?` | Збережений шлях до гри |
| `LastMode` | `last_mode` | `string?` | Останній обраний режим (slug) |

### Load

```csharp
public FileLoadResult<Config> Load()
```

Синхронне зчитування. Повертає `FileLoadResult<Config>`:
- **Missing** — файл не знайдено, повертає дефолтний `Config()`
- **Valid** — десеріалізація успішна
- **Invalid** — JSON пошкоджений або десеріалізувався в `null`

### SaveAsync

```csharp
public async Task SaveAsync(Config config, CancellationToken cancellationToken = default)
```

Атомарне збереження: запис у `.tmp` → `File.Replace` (якщо файл існує) або `File.Move` (якщо новий). При помилці tmp-файл очищується, виключення пробрасується далі.

---

## InstallationStateStore

**Файл:** `Storage/InstallationStateStore.cs`

Зчитування, збереження та очищення `installation.json`.

### Модель InstallationMetadata

**Файл:** `Storage/InstallationMetadata.cs`

| Поле | JSON-назва | Тип | Опис |
|---|---|---|---|
| `ModeSlug` | `mode_slug` | `string?` | Режим локалізації (slug) |
| `PublicId` | `public_id` | `string?` | ULID релізу |
| `Version` | `version` | `int?` | Версія релізу |
| `GamePatch` | `game_patch` | `int?` | Версія патчу гри |
| `Sha256` | `sha256` | `string?` | SHA-256 хеш файлу |
| `InstalledAt` | `installed_at` | `DateTimeOffset` | Час встановлення |
| `Source` | `source` | `string` | Джерело: `"api"` або `"official"` (default: `"api"`) |

### Load

```csharp
public FileLoadResult<InstallationMetadata> Load()
```

Синхронне зчитування. Статуси:
- **Missing** — файл не знайдено
- **Valid** — десеріалізація та валідація успішні
- **Invalid** — JSON пошкоджений, десеріалізувався в `null`, або поля не проходять валідацію

Валідація залежить від `Source`:
- **`"api"`** — обов'язкові: `ModeSlug`, `PublicId`, `Version`, `Sha256`, `GamePatch`, `InstalledAt != default`
- **`"official"`** — обов'язковий лише `InstalledAt != default`

### SaveAsync

```csharp
public async Task SaveAsync(InstallationMetadata metadata, CancellationToken cancellationToken = default)
```

Атомарне збереження (tmp → replace). Підтримує test seam через `OnSaveAsync`.

### ClearAsync

```csharp
public async Task ClearAsync(CancellationToken cancellationToken = default)
```

Видалення `installation.json`. Використовується при поверненні до офіційного файлу.

### FileLoadResult\<T\>

**Файл:** `Storage/FileLoadResult.cs`

Узагальнений результат зчитування файлу. Три статуси:
- `Valid(T value)` — файл знайдено та валідовано
- `Invalid(string error)` — файл знайдено, але пошкоджений/невалідний
- `Missing()` — файл не знайдено

---

## BackupStore

**Файл:** `Storage/BackupStore.cs`

Управління original snapshot, restore points, заміною game file та відновленням.

### Original Snapshot

Одноразова незмінна копія оригінального `languagedata_en.loc`, що існувала до першої модифікації.

#### CreateOriginalSnapshotAsync

```csharp
public virtual async Task<RestoreResult> CreateOriginalSnapshotAsync(
    string gameRoot, int? trustedGamePatch, CancellationToken cancellationToken = default)
```

Створює snapshot з `{gameRoot}\ads\languagedata_en.loc` у `OriginalBackupDir`. Алгоритм:
1. Перевіряє чи вже існує валідний snapshot — якщо так, повертає `Success` (не перезаписує)
2. Якщо snapshot існує, але пошкоджений — повертає `SnapshotCorrupted`
3. Копіює файл у temp → обчислює SHA-256 → записує metadata.json → atomic move обох файлів
4. При помилці — очищує temp-файли та щойно створені фінальні (але НЕ видаляє попередньо пошкоджений snapshot)

Metadata містить: `CreatedAt`, `GamePatch`, `Sha256`, `SizeBytes`, `Source = "original_snapshot"`.

#### CheckOriginalSnapshotAsync

```csharp
public async Task<(bool exists, bool isValid, RestoreError? error)> CheckOriginalSnapshotAsync(
    CancellationToken cancellationToken = default)
```

Перевірка цілісності snapshot. Повертає кортеж:
- `(false, false, null)` — snapshot не існує
- `(true, false, SnapshotCorrupted)` — snapshot є, але пошкоджений (відсутній файл/metadata, невідповідність розміру або SHA-256)
- `(true, true, null)` — snapshot валідний

#### LoadOriginalSnapshotAsync

```csharp
public async Task<(string? snapshotPath, BackupMetadata? metadata, RestoreError? error)> LoadOriginalSnapshotAsync(
    CancellationToken cancellationToken = default)
```

Завантажує snapshot для використання при відновленні оригіналу. Перевіряє наявність файлу + metadata, розмір, SHA-256. Повертає шлях до файлу та метадані, або помилку.

---

### Restore Points

Кожен restore point — піддиректорія у `RestorePointsDir` з назвою формату `{yyyyMMdd_HHmmss_fff}_{GUID}` (35 символів). Містить:
- `languagedata_en.loc` — копія game file на момент створення
- `metadata.json` — метадані
- `installation-state.json` (опціонально) — snapshot стану встановлення

#### CreateRestorePointAsync

```csharp
public virtual async Task<(string? restorePointDir, RestoreResult result)> CreateRestorePointAsync(
    string gameFilePath, int? gamePatch, string? operationLabel,
    byte[]? preOperationStateBytes = null, bool stateWasPresent = false,
    CancellationToken cancellationToken = default)
```

Створює restore point перед деструктивною операцією. Параметри:

| Параметр | Опис |
|---|---|
| `gameFilePath` | Повний шлях до поточного `languagedata_en.loc` |
| `gamePatch` | Версія патчу гри (nullable) |
| `operationLabel` | Мітка операції (`"pre_install"`, `"pre_restore_backup"`, тощо) |
| `preOperationStateBytes` | JSON-байти поточного `installation.json` (якщо є) |
| `stateWasPresent` | Чи існував `installation.json` перед операцією |

Валідація: `stateWasPresent=true` вимагає `preOperationStateBytes != null` і навпаки. Суперечливі комбінації повертають `BackupIo` помилку.

Алгоритм:
1. Створює директорію
2. Копіює game file → temp → SHA-256 → move
3. Записує metadata.json → move
4. Якщо `stateWasPresent` — записує `installation-state.json` → move
5. При помилці або cancellation — очищує всі temp-файли та директорію

#### ListRestorePointsAsync

```csharp
public async Task<List<RestorePointInfo>> ListRestorePointsAsync(CancellationToken cancellationToken = default)
```

Повертає каталог restore points, відсортованих за часом створення (новіші перші). Кожен запис містить:
- `Id` — назва директорії
- `CreatedAt`, `GamePatch`, `Source`, `SizeBytes`, `Sha256`
- `HasInstallationState` — чи є `installation-state.json`
- `IsRestorable` — чи можна відновити (залежить від `RestorePointStateKind`)

Пошкоджені restore points пропускаються з логуванням.

#### LoadRestorePointInfoAsync (internal)

```csharp
internal async Task<RestorePointInfo?> LoadRestorePointInfoAsync(
    string restorePointDir, CancellationToken cancellationToken = default)
```

Завантажує інформацію про один restore point. Перевіряє наявність metadata + game file, розмір, SHA-256. Класифікує стан через `ClassifyRestorePointState`.

#### ResolveRestorePointAsync

```csharp
public async Task<(string? restorePointDir, BackupMetadata? metadata, RestoreError? error)> ResolveRestorePointAsync(
    string restorePointId, CancellationToken cancellationToken = default)
```

Резолвить restore point за ID (назвою директорії). Захищає від path traversal:
- Відхиляє ID з `..`, `/`, `\` або rooted paths
- Перевіряє, що нормалізований шлях знаходиться в межах `RestorePointsDir`

Повертає повний шлях до директорії, metadata, або помилку (`RestorePointNotFound` / `RestorePointInvalid`).

---

### RestorePointStateKind

```csharp
internal enum RestorePointStateKind { Present, Absent, Invalid }
```

Класифікація стану restore point на основі маркера `installation_state` в metadata та наявності `installation-state.json`.

### ClassifyRestorePointState

```csharp
internal static RestorePointStateKind ClassifyRestorePointState(string? marker, bool hasStateFile)
```

| Маркер | hasStateFile | Результат |
|---|---|---|
| `"present"` | `true` | `Present` |
| `"present"` | `false` | `Invalid` |
| `"absent"` | `true` | `Invalid` |
| `"absent"` | `false` | `Absent` |
| `null` | `true` | `Present` (legacy сумісність) |
| `null` | `false` | `Invalid` |
| будь-яке інше | будь-яке | `Invalid` |

Restore point зі станом `Invalid` не можна використовувати для відновлення (`IsRestorable = false`).

---

### ReplaceGameFileAsync

```csharp
public virtual async Task<RestoreResult> ReplaceGameFileAsync(
    string targetPath, string sourceFilePath, string restorePointDir,
    CancellationToken cancellationToken = default)
```

Атомарна заміна game file. Три фази:

1. **Pre-replace:** копія source → temp, обчислення SHA-256
2. **Replace boundary:** `File.Replace` (якщо target існує) або `File.Move` (якщо ні)
3. **Post-replace:** перевірка SHA-256 встановленого файлу

Обробка помилок:
- **Pre-replace** (target не змінено): очищення temp, повернення `ReplaceFailed`
- **Post-replace** (target може бути змінено): автоматичний rollback через `RecoverFromRestorePointAsync`. Якщо rollback не вдався — `RecoveryFailed`
- **Post-replace SHA-256 mismatch:** rollback → `VerificationFailed`

Test seam: `OnPostReplaceHook` дозволяє ін'єктувати cancellation/failure між replace та верифікацією.

---

### RecoverFromRestorePointAsync

```csharp
public virtual async Task<RestoreResult> RecoverFromRestorePointAsync(
    string targetPath, string restorePointDir, CancellationToken cancellationToken = default)
```

Відновлення game file з restore point. Алгоритм:
1. Перевіряє наявність `languagedata_en.loc` у restore point
2. Копіює → temp recovery file → SHA-256 → `File.Replace`
3. Верифікує SHA-256 встановленого файлу

Використовується:
- Як rollback після невдалої заміни (`ReplaceGameFileAsync`)
- Як самостійна операція відновлення (`RestoreBackupService`)

---

## BackupMetadata

**Файл:** `Models/BackupMetadata.cs`

Модель metadata для original snapshot та restore points.

| Поле | JSON-назва | Тип | Опис |
|---|---|---|---|
| `CreatedAt` | `created_at` | `DateTimeOffset` | Час створення |
| `GamePatch` | `game_patch` | `int?` | Версія патчу гри (якщо відомо) |
| `Sha256` | `sha256` | `string` | SHA-256 хеш файлу |
| `SizeBytes` | `size_bytes` | `long` | Розмір файлу в байтах |
| `Source` | `source` | `string` | Джерело: `"original_snapshot"`, `"pre_install"`, `"pre_restore_backup"`, `"restore_original"`, тощо |
| `InstallationState` | `installation_state` | `string?` | Маркер стану встановлення (лише для restore points) |

### InstallationState маркер

| Значення | Опис |
|---|---|
| `"present"` | На момент створення restore point `installation.json` існував |
| `"absent"` | На момент створення restore point `installation.json` не існував |
| `null` | Original snapshot (не містить цього маркера) |

Цей маркер використовується для класифікації restore point через `ClassifyRestorePointState` та визначення, чи можна відновити стан встановлення при rollback.

## WWM-specific state and snapshots

WWM не використовує BDO `InstallationStateStore`/`BackupStore`. `WwmStateStore` зберігає окремі versioned `state/wwm-installation.json`, `state/wwm-transaction/journal.json` та `backups/pre-hub/` у `GamePersistencePaths` для stable ID `where-winds-meet`.

Installation state містить game ID, `mode slug + variant`, release version, outer archive size/SHA та manifest двох target files з relative path/size/SHA. Перед першою Hub mutation snapshot фіксує кожного target-у попереднє існування та, якщо він існував, exact bytes/size/SHA. Per-operation journal окремо зберігає безпосередній попередній стан обох файлів і raw prior installation-state bytes.

Malformed state/journal/snapshot fail closed. Restore допускається лише коли managed targets відповідають довіреному manifest, snapshot валідний, а Steam build ID не змінився; успішне restore очищує installation state і спожитий snapshot. Для target, який був відсутній до Hub, restore видаляє Hub-created file. Snapshot та journal є WWM-specific і не змінюють BDO schema.

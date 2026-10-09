# Services — сервісний шар застосунку

---

## 1. GameDetector

Визначає шлях до встановленої гри. Окремий модуль, не змішує UI та filesystem логіку.

### Порядок детекції (`DetectAsync`)

1. **SavedConfig** — збережений шлях у `ConfigStore`
2. **Registry** — реєстр Windows
3. **Steam** — `libraryfolders.vdf` → `appmanifest_582660.acf` → `installdir`
4. **ApiPattern** — `install_path_patterns` з API (hints для перебору дисків)
5. **NotFound** — гра не знайдена

### Сигнатури

```csharp
public sealed class GameDetector
{
    public GameDetector(ConfigStore configStore, ILogger logger, BdoGameDefinition? gameDefinition = null);

    public static bool ValidateGamePath(string gamePath);

    public async Task<DetectionResult> DetectAsync(
        IReadOnlyList<InstallPathPattern>? apiPatterns = null,
        CancellationToken cancellationToken = default);

    public async Task<DetectionResult> ValidateAndSaveManualPathAsync(
        string gamePath,
        CancellationToken cancellationToken = default);

    public static ManualResolveResult ResolveManualGameRoot(string selectedPath);
}
```

### `ValidateGamePath`

Перевіряє наявність `{gamePath}\ads\languagedata_en.loc`. Статичний метод, без побічних ефектів.

### `ResolveManualGameRoot`

Логіка ручного вибору користувача:

1. **Exact root** — якщо `selectedPath` вже містить `ads\languagedata_en.loc` → `Found`
2. **Unique child** — один підкаталог з `ads\languagedata_en.loc` → `Found`
3. **Ambiguous** — кілька підкаталогів з файлом → `Ambiguous`
4. **NotFound** — нічого не знайдено

### DetectionResult / ManualResolveResult

```csharp
public enum DetectionSource { SavedConfig, Registry, Steam, ApiPattern, Manual }

public sealed class DetectionResult
{
    public bool IsFound { get; }
    public string? GamePath { get; }
    public DetectionSource? Source { get; }
    public bool Persisted { get; }

    public static DetectionResult Found(string gamePath, DetectionSource source, bool persisted = true);
    public static DetectionResult NotFound();
}

public enum ManualResolveStatus { Found, NotFound, Ambiguous }

public sealed class ManualResolveResult
{
    public ManualResolveStatus Status { get; }
    public string? GamePath { get; }

    public static ManualResolveResult Found(string gamePath);
    public static ManualResolveResult NotFound();
    public static ManualResolveResult Ambiguous();
}
```

---

## 2. LocalizationInstaller

Відповідає за завантаження файлів локалізації та official source. Retry з backoff, SHA-256, progress.

### Сигнатури

```csharp
public sealed class LocalizationInstaller
{
    public LocalizationInstaller(HttpClient httpClient, AppPaths appPaths, ILogger logger,
        int timeoutSeconds = 60, int[]? retryDelaysMs = null);

    public LocalizationInstaller(AppPaths appPaths, ILogger logger,
        int timeoutSeconds = 60, int[]? retryDelaysMs = null);

    public async Task<DownloadResult> DownloadReleaseAsync(
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public async Task<DownloadResult> DownloadOfficialSourceAsync(
        string officialUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### `DownloadReleaseAsync`

Завантажує release файл за `release.DownloadUrl`. Перевіряє HTTP result, `size_bytes`, SHA-256. Retry до 4 спроб (1 + 3 retry) з затримками `[1000, 2000, 4000]` мс.

### `DownloadOfficialSourceAsync`

Завантажує оригінальний `languagedata_en.loc` за `official_source_url`. SHA-256 для official source НЕ надається сервером — перевірка лише розміру. Аналогічний retry.

### DownloadResult / DownloadProgress

```csharp
public enum DownloadError
{
    InvalidMetadata, Timeout, Network, Http,
    SizeMismatch, HashMismatch, Io, Unexpected
}

public sealed class DownloadResult
{
    public bool IsSuccess { get; }
    public string? TempFilePath { get; }
    public long? SizeBytes { get; }
    public string? Sha256 { get; }
    public DownloadError? Error { get; }
    public string? ErrorMessage { get; }
    public bool IsRetryable { get; }

    public static DownloadResult Success(string tempFilePath, long sizeBytes, string sha256);
    public static DownloadResult SuccessWithoutHash(string tempFilePath, long sizeBytes);
    public static DownloadResult Failure(DownloadError error, string? message = null, bool isRetryable = false);
}

public sealed class DownloadProgress
{
    public long BytesDownloaded { get; }
    public long? TotalBytes { get; }
    public double? Percentage { get; }
}
```

---

## 3. LocalizationInstallService

Транзакційна установка локалізації. Кожен фаза може повернути помилку; файл гри не змінюється до моменту заміни.

### Сигнатури

```csharp
public sealed class LocalizationInstallService
{
    public LocalizationInstallService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot);

    public async Task<InstallResult> InstallReleaseAsync(
        string modeSlug,
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### Фази `InstallReleaseAsync`

| # | Фаза | Опис |
|---|------|------|
| 1 | **Validate input** | Перевірка `modeSlug`, `release.PublicId`, `Version`, `Patch`, `DownloadUrl` (HTTPS), `SizeBytes`, `Sha256`, `CompatibleWithOfficialPatch` |
| 2 | **Validate pre-state** | `InstallationStateStore.Load()` — якщо Invalid → `PreOperationStateFailed` |
| 3 | **Download** | `LocalizationInstaller.DownloadReleaseAsync` → temp файл |
| 4 | **Original snapshot** | Якщо original snapshot ще не існує — створити копію поточного game file |
| 5 | **Restore point** | Створити pre-operation snapshot (restore point) поточного game file |
| 6 | **Replace** | Замінити game file на завантажений release |
| 7 | **Verify** | SHA-256 встановленого файлу == очікуваному hash |
| 8 | **Save state** | Записати `installation.json` (metadata) |
| 9 | **Rollback** | При помилці після replace — rollback до pre-operation snapshot |

### Rollback

Якщо replace успішний, але verify/state save невдалий:
- Спроба відновити game file з restore point
- Спроба відновити стан (`installation.json`)
- Якщо rollback не вдався — стан `Corrupted`, критична помилка користувачу

### InstallResult / InstallError

```csharp
public enum InstallError
{
    InvalidGamePath, InvalidRelease, Incompatible, DownloadFailed,
    OriginalSnapshotFailed, PreOperationStateFailed, BackupFailed,
    ReplaceFailed, VerificationFailed, StateSaveFailed, RollbackFailed
}

public sealed class InstallResult
{
    public bool IsSuccess { get; }
    public InstallError? Error { get; }
    public string? ErrorMessage { get; }

    public static InstallResult Success();
    public static InstallResult Failure(InstallError error, string? message = null);
}
```

---

## 4. RestoreOriginalService

Відновлює оригінальний `languagedata_en.loc`. Спочатку пробує завантажити з `official_source_url`; fallback на local original snapshot ТІЛЬКИ якщо `snapshot.game_patch == current.official_patch`.

### Сигнатури

```csharp
public sealed class RestoreOriginalService
{
    public RestoreOriginalService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot,
        string officialSourceUrl,
        int? currentOfficialPatch);

    public async Task<RestoreResult> RestoreOriginalAsync(CancellationToken cancellationToken = default);
}
```

### Логіка

1. Перевірити наявність game file
2. **Спроба 1: Download** — `DownloadOfficialSourceAsync(officialSourceUrl)`
   - Якщо `FallbackNotAllowed` (original snapshot відсутній) — помилка
   - Якщо `PatchMismatch` — помилка (snapshot не відповідає поточному official patch)
3. **Спроба 2: Local snapshot** — fallback на original snapshot з `BackupStore`
4. Replace game file → verify → save state (source = "official")

---

## 5. RestoreBackupService

Відновлює гру з обраного restore point (попередньої встановленої локалізації).

### Сигнатури

```csharp
public sealed class RestoreBackupService
{
    public RestoreBackupService(
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot);

    internal Action? OnPostGameReplaceHook { get; set; } // test seam

    public async Task<RestoreResult> RestoreAsync(
        string restorePointId,
        CancellationToken cancellationToken = default);
}
```

### Логіка `RestoreAsync`

1. Перевірити наявність game file
2. Знайти restore point за `restorePointId` (BackupStore)
3. Створити pre-operation snapshot поточного стану (для rollback)
4. Замінити game file на файл з restore point
5. Verify SHA-256
6. Оновити `installation.json` (metadata з restore point)
7. При помилці — rollback game file + rollback state

---

## 6. DynamicModePolicy

Визначає, які режими доступні для установки, та формулює UI-рядки. Статичний клас.

### Сигнатури

```csharp
internal static class DynamicModePolicy
{
    public static List<LocalizationMode> GetInstallableModes(List<LocalizationMode>? allModes);

    public static bool IsStructurallyInstallable(LocalizationMode mode);

    public static string GetDisplayName(LocalizationMode mode);

    public static string FormatReleaseLine(LocalizationMode mode);

    public static string? FormatPublishedDate(string? publishedAt);

    public static string? ResolveInitialSelection(
        string? savedLastMode,
        List<LocalizationMode> installableModes);
}
```

### `GetInstallableModes`

Фільтрує `allModes` — залишає лише режими, де `IsStructurallyInstallable == true`.

### `IsStructurallyInstallable`

Перевіряє наявність обов'язкових полів: `Slug`, `Current.PublicId`, `Current.DownloadUrl` (HTTPS), `Current.Sha256`, `Current.SizeBytes > 0`, `Current.Version > 0`, `Current.Patch > 0`.

### `GetDisplayName`

`PublicName` → `Slug` → `"Невідомий режим"`.

### `FormatReleaseLine`

Формат: `v{version} • patch {patch} • реліз {dd.MM.yyyy}`.

### `FormatPublishedDate`

Парсинг ISO datetime → `dd.MM.yyyy` (local time). `null` якщо невалідний.

### `ResolveInitialSelection`

Два перевантаження:

```csharp
ResolveInitialSelection(string? savedLastMode, List<LocalizationMode> installableModes);
ResolveInitialSelection(string? installedModeSlug, string? savedLastMode, List<LocalizationMode> installableModes);
```

Пріоритет вибору: `installedModeSlug` (exact ordinal збіг) → `savedLastMode` (ordinal збіг) → перший елемент списку. Перше перевантаження делегує друге з `installedModeSlug = null`. Повертає `null` якщо список порожній.

---

## 7. InstallActionPolicy

Визначає, які дії доступні користувачу (Install / Restore Original). Статичний клас.

### Сигнатури

```csharp
internal sealed class InstallActionPolicyResult
{
    public bool CanInstall { get; }
    public bool CanRestoreOriginal { get; }
    public bool AlreadyInstalledExactTarget { get; }
}

internal static class InstallActionPolicy
{
    public static bool IsExactInstalledTarget(
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? mode);

    public static InstallActionPolicyResult Evaluate(
        LocalizationState factualState,
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? selectedMode,
        CurrentRelease? selectedCurrent,
        CompatibilityResult compatResult,
        bool operationInProgress);
}
```

### `IsExactInstalledTarget`

Порівнює `installedModeSlug` та `installedPublicId` з `mode.Slug` та `mode.Current.PublicId` (ordinal exact). Обидва повинні збігатися.

### `Evaluate`

| Поле | Умова |
|------|-------|
| `CanInstall` | `!operationInProgress` AND `structurallyValid` AND `compatResult.IsAllowed` AND `!alreadyInstalled` |
| `CanRestoreOriginal` | `!operationInProgress` AND state ∈ {UpToDate, UpdateAvailable, WaitingForRelease, Corrupted, InstalledVersionUnknown} |
| `AlreadyInstalledExactTarget` | state == UpToDate AND installedModeSlug == selectedMode.Slug AND installedPublicId == selectedCurrent.PublicId |

---

## 8. LocalizationStateService

Визначає фактичний стан встановленої локалізації. Не приймає "selected mode" як вхідний параметр — працює лише з файловою реальністю та metadata.

### Сигнатури

```csharp
public sealed class LocalizationStateService
{
    public LocalizationStateService(InstallationStateStore stateStore, ILogger logger);

    public async Task<LocalizationStateResult> ResolveAsync(
        CurrentRelease? current,
        string gameLocFilePath,
        CancellationToken cancellationToken = default,
        string? gameRoot = null);
}
```

`gameRoot` — опціональний корінь гри; якщо переданий, сервіс читає локальний патч через BDO boundary (`BdoGameDefinition.TryReadInstalledPatch(gameRoot)`) для визначення patch transition.

### Логіка `ResolveAsync`

| Крок | Умова | Результат |
|------|-------|-----------|
| 1 | `installation.json` missing | `NotInstalled` |
| 2 | `installation.json` invalid | `InstalledVersionUnknown` |
| 3 | `metadata.Source == "official"` | `NotInstalled` |
| 4 | Game file missing | `Corrupted` |
| 5 | SHA-256 mismatch АБО локальний патч (`ads_files`) новіший за `metadata.GamePatch` | Patch transition (див. нижче), зазвичай НЕ `Corrupted` |
| 5b | SHA-256 mismatch при тому самому або невідомому патчі | `ManagedFileChanged` transition, НЕ `Corrupted` |
| 6 | `current == null` | `WaitingForRelease` |
| 7 | `current.PublicId` empty/null/whitespace | `WaitingForRelease` + Warning |
| 8 | `metadata.PublicId == current.PublicId` (ordinal) | `UpToDate` |
| 9 | Інше | `UpdateAvailable` |

### Patch transition

`LocalizationPatchTransition { None, ExistingLocalizationOutdated, GameFileReplacedAfterPatch, ManagedFileChanged }`.

- **Hash mismatch** при фактичному `ads_files` patch, новішому за `InstallationMetadata.GamePatch`, — нормальна transition-подія після оновлення гри, а не `Corrupted`. Результат: `GameFileReplacedAfterPatch`.
- **Hash mismatch** при тому самому або невідомому `ads_files` patch — файл замінено грою/лаунчером після встановлення, а не `Corrupted`. Результат: `ManagedFileChanged`.
- **Локальний patch новіший за встановлений** (`localPatch > installedPatch`, без hash mismatch) — `ExistingLocalizationOutdated`; фінальний стан зазвичай `UpdateAvailable` або `WaitingForRelease`.

### LocalizationStateResult

```csharp
public sealed class LocalizationStateResult
{
    public LocalizationState State { get; }
    public string? Error { get; }
    public int? InstalledGamePatch { get; }
    public int? LocalGamePatch { get; }
    public LocalizationPatchTransition PatchTransition { get; }

    public static LocalizationStateResult Success(LocalizationState state);
    public static LocalizationStateResult WithWarning(LocalizationState state, string error);
    public static LocalizationStateResult WithPatchTransition(..., LocalizationPatchTransition transition, ...);
}
```

---

## 9. LocalizationCompatibilityService

Перевіряє `compatible_with_official_patch` з API.

### Сигнатури

```csharp
public sealed class LocalizationCompatibilityService
{
    public CompatibilityResult Check(CurrentRelease? current);
}
```

### Логіка `Check`

| Умова | Результат |
|-------|-----------|
| `current == null` | `Blocked("Current release is not available.")` |
| `current.PublicId` empty/null/whitespace | `Blocked("Current release metadata is invalid: public_id is empty.")` |
| `!current.CompatibleWithOfficialPatch` | `Blocked("Release is not compatible with the current official game patch.")` |
| Інше | `Allowed()` |

### CompatibilityResult

```csharp
public sealed class CompatibilityResult
{
    public bool IsAllowed { get; }
    public string? Reason { get; }

    public static CompatibilityResult Allowed();
    public static CompatibilityResult Blocked(string reason);
}
```

---

## 10. HashHelper

Утиліта для обчислення SHA-256 та копіювання файлів. `internal static`.

```csharp
internal static class HashHelper
{
    public static string ComputeFileSha256(string filePath);
    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default);
    public static string ComputeSha256(byte[] data);
    public static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    public static async Task CopyFileCreateNewAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
```

SHA-256 повертається у форматі lowercase hex string.

`CopyFileCreateNewAsync` — копіювання з забороною перезапису: якщо destination існує, операція завершується помилкою. Використовується для створення backup-копій, щоб захистити existing original snapshot від перезапису модифікованим файлом (AGENTS §14.2).

---

## 11. StartupCoordinator

Координація паралельного запуску: API + local game detection + API-pattern fallback. Повертає factual final game outcome.

### Сигнатури

```csharp
internal sealed class StartupCoordinatorResult
{
    public string? FinalGamePath { get; }
    public DetectionSource? FinalGameSource { get; }
    public bool ApiSuccess { get; }
    public ReleasesResponse? ApiResponse { get; }
    public ApiErrorKind ApiErrorKind { get; }
    public string? ApiErrorMessage { get; }
}

internal sealed class StartupGameResult
{
    public bool IsFound { get; }
    public string? GamePath { get; }
    public DetectionSource? Source { get; }
}

internal sealed class StartupApiResult
{
    public bool Success { get; }
    public ReleasesResponse? Response { get; }
    public ApiErrorKind ErrorKind { get; }
    public string? ErrorMessage { get; }
}

internal sealed class StartupCoordinator
{
    public StartupCoordinator(
        Func<Task<ApiResult<ReleasesResponse>>> loadApi,
        Func<IReadOnlyList<InstallPathPattern>?, Task<DetectionResult>> detectGame,
        ILogger logger);

    public async Task<StartupCoordinatorResult> RunAsync(
        Action<StartupGameResult>? onLocalDetectionComplete = null,
        Action<StartupApiResult>? onApiComplete = null,
        Action? onFallbackStarted = null);
}
```

### Логіка `RunAsync`

1. Паралельно запускає API та local detection
2. Обробляє результати в порядку завершення (callbacks)
3. Повертає `StartupCoordinatorResult` з фінальним game outcome

### Final outcome matrix

| Local | API | Fallback | Final |
|-------|-----|----------|-------|
| Found | any | — | Found(local) |
| NotFound | failure | — | NotFound |
| NotFound | success, no patterns | — | NotFound |
| NotFound | success, patterns | Found | Found(fallback) |
| NotFound | success, patterns | NotFound | NotFound |

### Callbacks

- `onLocalDetectionComplete` — викликається при завершенні local detection
- `onApiComplete` — викликається при завершенні API
- `onFallbackStarted` — викликається перед початком API-pattern fallback

---

## 12. ApiErrorPresentation

Маппінг `ApiErrorKind` → українське UI повідомлення. Статичний клас.

### Сигнатури

```csharp
internal static class ApiErrorPresentation
{
    public static string GetUserMessage(ApiErrorKind errorKind, string? technicalMessage = null);
}
```

`technicalMessage` не впливає на текст повідомлення — усі тексти статичні; параметр залишено для сумісності сигнатур.

### Маппінг

| ApiErrorKind | Повідомлення |
|--------------|-------------|
| `Timeout` | Сервер не відповів вчасно. |
| `Network` | Не вдалося підключитися до сервера. |
| `Http` | Сервер повернув помилку. |
| `InvalidResponse` | Сервер повернув некоректні дані. |
| `Cancelled` | Запит скасовано. |
| `Unexpected` | Неочікувана помилка при зверненні до сервера. |
| `None` | Не вдалося завантажити режими локалізації. |

---

## 13. ReleaseFeedPoller

Background-полінг `GET /releases` для оновлення даних між операціями. `public sealed class`, `IDisposable`.

```csharp
public sealed class ReleaseFeedPoller : IDisposable
{
    public ReleaseFeedPoller(BdoUaApiClient apiClient, ILogger logger, TimeSpan? pollInterval = null);

    public event Action<ReleasesResponse>? OnFeedCandidate;
    public event Action<string>? OnPollFailed;

    public bool IsRunning { get; }
    public void Start(ReleasesResponse? acceptedFeed);
    public void Stop();
    public void Pause();
    public void Resume();
    public void AcceptFeed(ReleasesResponse feed);
    public ReleasesResponse? GetAcceptedFeed();
}
```

Інтервал за замовчуванням — 15 секунд. Полінг призупиняється під час операцій (`Pause`/`Resume` з боку MainForm/FeedApplicationCoordinator).

## 14. FeedChangeDetector

`public static class`. Семантичне порівняння двох feed-відповідей:

```csharp
public static bool HasSemanticChange(ReleasesResponse? oldFeed, ReleasesResponse? newFeed);
```

Порівнює: `OfficialPatch`, `OfficialSourceUrl`, порядок режимів та per-mode поля (`Slug`, `PublicName`, `Description`, `Audience`, усі поля `current`). Ігнорує `GeneratedAt` (технічне поле). Використовується, щоб не застосовувати feed без фактичних змін.

## 15. FeedApplicationCoordinator

Серіалізація застосування feed-кандидатів у UI-стан.

```csharp
public sealed class FeedApplicationCoordinator
{
    public FeedApplicationCoordinator(
        Func<ReleasesResponse, Task<bool>> applyFeed,
        ReleaseFeedPoller poller,
        ILogger logger);

    public bool IsApplying { get; }
    public bool IsBlocked { get; }
    public bool HasPendingFeed { get; }

    public void BlockUpdates();   // на час localization-операцій
    public void UnblockUpdates();

    public Task OnCandidateAsync(ReleasesResponse candidate);   // від poller event
    public Task ApplyPendingIfAnyAsync();                       // після завершення операції
}
```

Логіка:
- Кандидат, що прийшов під час `blocked` або `applying`, зберігається як **pending**.
- `ApplyPendingIfAnyAsync` застосовує останній pending кандидат після завершення операції; при невдачі кандидат повертається у чергу (requeue), щоб уникнути регресії (`ClearStalePending` проти застарілих pending).

## 16. AdsFilesPatchReader

Concrete BDO reader, composed by `BdoGameDefinition`. Читає файл `{gameRoot}\ads_files` та витягує версію патчу гри з рядка `languagedata_en.loc <patch>`. Legacy static `TryReadPatch` delegates to the default BDO definition.

```csharp
public static int? TryReadPatch(string? gameRoot);
```

Повертає `null` при: відсутньому/некореневому `gameRoot`, відсутньому `ads_files`, неоднозначному вмісті (записів `languagedata_en.loc` != 1) або помилках IO/access. Використовується `LocalizationStateService` для patch transition.

## 17. LocalizationStatePresentation

`public static class`. Перетворює `LocalizationStateResult` на український UI-текст.

```csharp
public static string GetDisplayText(LocalizationStateResult result);
```

Пріоритет: patch transition тексти ("Встановлена локалізація застаріла", "Після оновлення гри файл локалізації було замінено") → текст стану. Детальніше — docs/states.md.

## 18. Where Winds Meet services

WWM session використовує окремі bounded компоненти:

- `WwmSteamDetector` — знаходить Steam AppID `3564740` через Steam libraries/manifests; автоматичний і ручний root мають містити WWM EXE та locale directory. `buildid` — лише спостережна/snapshot-staleness metadata, не compatibility rule.
- `Winds4UaApiClient` та `WwmPackageResolver` — отримують `/api/public/v1/releases/latest`, динамічно вибирають доступний package за `slug + variant`, звіряють mode з current file; fresh response повторно перевіряється безпосередньо перед install/update.
- `WwmPackageService` — HTTPS download, outer size/SHA validation, bounded ZIP inspection і staging лише двох allow-listed locale targets. Архів не розпаковується напряму в гру та не виконує BAT/script.
- `WwmInstallService` — зберігає pre-Hub snapshot і per-operation prior state, веде game-scoped transaction journal, замінює/створює обидва файли, верифікує їх і відновлює обидва targets/state при rollback. Startup recovery розрізняє завершений commit від незавершеної транзакції.
- `WwmReleaseFeedPoller` — WWM-specific polling без перетворення feed у BDO `ReleasesResponse`.

WWM compatibility із конкретним Steam build не підтверджується API; перед кожним install/update потрібне нейтральне explicit confirmation. Restore означає повернення до точного pre-Hub стану (`Відновити попередній стан`), не до гарантованих stock bytes. При зміні Steam build stale pre-Hub restore блокується.

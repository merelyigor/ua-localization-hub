# Архітектура Хабу Українізаторів BDO - WWM

## 1. Структура каталогів

```
BDO-PROGRAM/
├── Program.cs                  — Composition root: normal mode + --apply-update helper mode
├── MainForm.cs                 — UI логіка (WinForms), обробка подій, координація сервісів
├── MainForm.Designer.cs        — WinForms designer: контрольні елементи та layout
├── BdoClient.csproj            — Проектний файл (.NET 8.0-windows, WinForms)
├── BdoUaClient.sln             — Solution файл
│
│   ── UI-компоненти (корінь) ──
├── BdoSurfacePanel.cs          — Кастомна панель поверх BDO-фону (тема)
├── BdoProgressBar.cs           — Кастомний progress bar із семантичними станами
├── LocalizationModeCard.cs     — Клікабельна картка режиму локалізації (замість RadioButton)
├── ModeCardPresentation.cs     — Політика презентації карток режимів (ModeCardPresentationPolicy)
├── UiTheme.cs                  — BDO-тема: кольори, шрифти, масштабування
├── WindowChromeHelper.cs       — Custom title bar / window chrome
├── UpdateApplyingForm.cs       — UI helper mode (--apply-update)
├── InstallButtonLabelPolicy.cs — Контекстний текст кнопки («Встановити»/«Оновити»/«✓ Встановлено»)
├── LocalizationFlagParser.cs   — Парсинг UA/GB прапорців для карток режимів
├── ThemePrototype.cs.reference.txt — Історичний референс теми (не компілюється)
│
├── Api/
│   ├── ApiResult.cs            — Result pattern: ApiResult<T> Success/Error
│   ├── BdoUaApiClient.cs       — HTTP клієнт GET /releases + WarmupConnectionAsync
│   ├── BdoUaHttpClientConfiguration.cs — Конфігурація HttpClient (User-Agent, proxy, handler)
│   ├── NetworkDiagnostics.cs   — Форматування мережевих помилок для логів
│   └── ResilientConnectionConnector.cs — Happy-eyeballs TCP connect (parallel DNS attempts)
│
├── Models/
│   ├── ReleasesResponse.cs     — Кореневий DTO відповіді API
│   ├── ReleaseData.cs          — Дані релізу (official_patch, modes, progress)
│   ├── LocalizationMode.cs     — Режим локалізації (slug, public_name, current, history)
│   ├── CurrentRelease.cs       — Поточний релізу (public_id, download_url, sha256, size_bytes)
│   ├── ReleaseHistoryItem.cs   — Елемент історії релізів
│   ├── InstallPathPattern.cs   — Патерн шляху встановлення (pattern, launcher)
│   ├── GameTestInfo.cs         — Статус тестування гри
│   ├── ProgressInfo.cs         — Прогрес перекладу (total_rows, translated_percent)
│   ├── StatsInfo.cs            — Статистика (rows_in_file)
│   ├── AnnouncementsInfo.cs    — Стан розсилок (discord, telegram)
│   ├── BackupMetadata.cs       — Метадані бекапу (original snapshot)
│   ├── RestorePointInfo.cs     — Інформація про restore point
│   └── RestoreResult.cs        — Результат операції відновлення
│
├── Services/
│   ├── GameDetector.cs         — Пошук гри: registry → Steam → patterns → ручний вибір
│   ├── LocalizationInstaller.cs — Встановлення: download → validate → backup → apply → verify
│   ├── LocalizationStateService.cs — Визначення LocalizationState (NotInstalled/UpToDate/...)
│   ├── LocalizationCompatibilityService.cs — Перевірка compatible_with_official_patch
│   ├── LocalizationInstallService.cs — Додатковий сервіс встановлення
│   ├── RestoreOriginalService.cs — Відновлення оригінального файлу (official_source_url / snapshot)
│   ├── RestoreBackupService.cs — Відновлення з restore point
│   ├── LocalizationState.cs    — Enum станів локалізації
│   ├── OperationState.cs       — Enum станів операції (Idle/Downloading/Installing/...)
│   ├── LocalizationStateResult.cs — Результат обчислення стану
│   ├── CompatibilityResult.cs  — Результат перевірки сумісності
│   ├── DetectionResult.cs      — Результат пошуку гри
│   ├── DownloadResult.cs       — Результат завантаження
│   ├── InstallResult.cs        — Результат встановлення
│   ├── InstallActionPolicy.cs  — Політика дій встановлення
│   ├── DynamicModePolicy.cs    — Політика динамічних режимів
│   └── HashHelper.cs           — SHA-256 хешування та захищене копіювання файлів
│
├── Update/                     — Self-update застосунку (Stage 13, див. docs/update.md)
│   ├── ApplicationCommandLine.cs — Парсинг --apply-update <session-id>
│   ├── AppVersion.cs / AppVersionInfo.cs — Numeric версія та детекція поточної версії EXE
│   ├── GitHubRelease.cs / GitHubResult.cs / GitHubUpdateClient.cs — Клієнт GitHub Releases (без токена)
│   ├── UpdateSelectionPolicy.cs — Вибір релізу: numeric comparison + channel policy
│   ├── UpdateManifest.cs / UpdateManifestValidator.cs — Schema-2 manifest + валідація
│   ├── ExecutableVersionValidator.cs — Перевірка версії staged EXE
│   ├── UpdatePackageService.cs / UpdatePackageResult.cs — Завантаження та розпакування ZIP
│   ├── ReplacementWorkspace.cs  — Staging-директорія для candidate EXE
│   ├── PreparedAttemptCleanup.cs — Очищення незавершених сесій оновлення
│   ├── UpdateSession.cs / UpdateSessionStore.cs — Сесійний стан у updates/<GUID>/
│   ├── SelfUpdatePreparationService.cs — Підготовка сесії (download → validate → stage)
│   ├── SelfUpdateApplier.cs     — Helper mode: заміна EXE + restart, exit codes
│   ├── StartupUpdateLifecycleCoordinator.cs — Startup maintenance (cleanup)
│   ├── UpdateLifecycleService.cs — Координація перевірки/підготовки оновлення
│   ├── UpdateButtonState.cs     — Стани кнопки «Оновити до vX.Y.Z»
│   └── ForegroundWindowHelper.cs — Допоміжний клас для фокусу вікон
│   ├── LocalizationStatePresentation.cs — UI-тексти станів локалізації
│   ├── ApiErrorPresentation.cs — ApiErrorKind → українські UI повідомлення
│   ├── BdoGameDefinition.cs    — Explicit BDO identity, target, validation, detection facts and patch ownership
│   ├── BdoGameSession.cs        — Concrete BDO runtime composition and bounded lifetime
│   ├── AdsFilesPatchReader.cs  — Concrete BDO ads_files patch reader composed by BdoGameDefinition
│   ├── ReleaseFeedPoller.cs    — Background polling /releases (15 с)
│   ├── FeedChangeDetector.cs   — Семантичне порівняння feed-кандидатів
│   ├── FeedApplicationCoordinator.cs — Застосування feed-змін (pending черга)
│   └── StartupCoordinator.cs   — Паралельний startup: API + local detection
│
├── Storage/
│   ├── AppPaths.cs             — Application-global шляхи %LocalAppData%\BDO-UA-Client\
│   ├── GamePersistencePaths.cs — Canonical game-scoped config/state/backups paths
│   ├── LegacyBdoPersistenceMigrator.cs — Bounded migration старого BDO layout
│   ├── ConfigStore.cs          — Зчитування/збереження config.json
│   ├── Config.cs               — Модель конфігурації (game_path тощо)
│   ├── ApplicationConfigStore.cs — Global application settings
│   ├── ApplicationConfig.cs     — Модель global settings
│   ├── ReleaseFeedCache.cs     — Нормалізований last-known release feed, schema-v1, atomic best-effort cache
│   ├── InstallationStateStore.cs — Зчитування/збереження state/installation.json
│   ├── InstallationMetadata.cs — Метадані встановленої локалізації (public_id, version, sha256)
│   ├── BackupStore.cs          — Управління бекапами (original snapshot + restore points)
│   └── FileLoadResult.cs       — Результат зчитування файлу
│
├── Logging/
│   ├── ILogger.cs              — Інтерфейс логера (Debug/Info/Warning/Error)
│   └── FileLogger.cs           — Реалізація: ротація логів, запис у файл
│
├── BdoClient.Tests/            — Юніт-тести (xUnit)
└── docs/                       — Документація
```

---

## Game catalog boundary

`Services/GameCatalog` містить explicit compile-time application catalog. Наразі він реєструє лише `black-desert-online` з `BdoGameDefinition`; `GameDescriptor` надає stable ID і display name. Stage 1 показує selected BDO descriptor у main shell. `Services/BdoGameSession` є concrete BDO runtime boundary, а `Services/SelectedGameSessionHost` володіє рівно однією активною session. Stage 3 додає bounded replacement lifecycle без generic game-session interface: старий poller/session work скасовується й drain-иться, handlers від'єднуються, після чого candidate session стає активною; generation guards блокують stale results.

## Technical identity boundary

`ApplicationTechnicalIdentity` централізує активні compatibility identities для поточного update/startup/storage protocol: canonical repository `merelyigor/ua-localization-hub`, legacy fallback `merelyigor/bdo-ua-client`, User-Agent `BDO-UA-Client`, фізичний EXE `BDO-UA-Client.exe`, legacy package naming, autostart value та `%LocalAppData%\\BDO-UA-Client`. Repository rename уже виконано без зміни фізичних compatibility identities.

`GitHubUpdateClient` виконує bounded ordered discovery: спочатку canonical repository, а legacy fallback — лише після canonical HTTP 404. Успішний canonical response, malformed JSON та інші HTTP failures не запускають fallback; asset URLs залишаються тими, які повернув GitHub.

## 2. Composition Root

Весь граф залежностей створюється в `Program.cs` (Manual DI). DI-контейнер не використовується.

### Normal mode (`RunNormalMode`)

```
Program.Main()
│
├─ ApplicationCommandLine.Parse(args)
│   └─ --apply-update <session-id> → RunHelperMode() (див. docs/update.md)
│
├─ AppPaths                    — application-global шляхи (%LocalAppData%\BDO-UA-Client\)
│   └─ EnsureGlobalDirectories() — logs/cache/updates
├─ FileLogger(appPaths)        — єдиний логер для всього застосунку
│
├─ ApplicationConfigStore(appPaths, logger)
├─ AppVersionInfo.Detect()     — версія поточного EXE
│
├─ BdoGameSession(appPaths, logger, appVersionInfo)
│   ├─ BdoGameDefinition.Default + GameDescriptor
│   ├─ LegacyBdoPersistenceMigrator + GamePersistencePaths
│   ├─ ConfigStore / InstallationStateStore / BackupStore
│   ├─ GameDetector / LocalizationStateService / LocalizationCompatibilityService
│   ├─ shared BDO HttpClient → BdoUaApiClient + LocalizationInstaller
│   ├─ ReleaseFeedCacheStore (game-scoped physical cache path)
│   └─ ReleaseFeedPoller (session lifetime)
│
├─ GitHub HttpClient           — ОКРЕМИЙ HttpClient (UseProxy = false):
│   └─ GitHubUpdateClient → UpdateSelectionPolicy
│
├─ SelectedGameSessionHost(initial BDO session, production factory)
└─ MainForm(applicationConfigStore, gameCatalog, sessionHost,
            logger, appVersionInfo, gitHubClient, selectionPolicy, appPaths)
```

MainForm всередині себе додатково створює application update services, `FeedApplicationCoordinator` та `StartupCoordinator`; BDO session/runtime composition і `ReleaseFeedPoller` належать `BdoGameSession`. `SelectedGameSessionHost` замінює активну session тільки після скасування/drain старої; Stage 3 ще не надає production другого game module.

---

## 3. Dependency Graph

```
MainForm
├── SelectedGameSessionHost ─── one active concrete BDO runtime boundary
│   └── BdoGameSession
│   ├── ConfigStore ─────────── GamePersistencePaths, ILogger
│   ├── InstallationStateStore ─ GamePersistencePaths, ILogger
│   ├── BackupStore ─────────── GamePersistencePaths, ILogger, BdoGameDefinition
│   ├── GameDetector ────────── ConfigStore, ILogger, BdoGameDefinition
│   ├── BdoUaApiClient ──────── shared BDO HttpClient, ILogger
│   ├── LocalizationInstaller ─ shared BDO HttpClient, AppPaths, ILogger
│   ├── LocalizationStateService / LocalizationCompatibilityService
│   └── ReleaseFeedPoller / ReleaseFeedCacheStore (GamePersistencePaths-scoped cache)
├── ApplicationConfigStore ──── AppPaths, ILogger (global settings)
├── GitHubUpdateClient ──────── GitHub HttpClient, ILogger
├── UpdateLifecycleService ──── GitHubUpdateClient, SelectionPolicy, PreparationService, ...
└── ILogger (FileLogger)

AppPaths ─── (no dependencies, reads %LocalAppData%; global + legacy compatibility paths)
GamePersistencePaths ─── AppPaths.Root + stable game id
LegacyBdoPersistenceMigrator ─── legacy AppPaths → GamePersistencePaths
FileLogger ── AppPaths.LogsDir
```

**Правило:** сервіси localization-домену не залежать один від одного напряму. Self-update утворює власну ієрархію (`UpdateLifecycleService` координує preparation/applier). Координація відбувається в MainForm.

---

## 4. Runtime Data: %LocalAppData%\BDO-UA-Client\

```
%LocalAppData%\BDO-UA-Client\
├── games/
│   └── black-desert-online/
│       ├── config.json             — BDO game_path
│       ├── cache/
│       │   └── release-feed.json   — BDO last-known feed, display-only
│       ├── state/
│       │   └── installation.json   — BDO localization state
│       └── backups/
│           ├── original/           — незмінний BDO original snapshot
│           └── restore-points/      — BDO pre-operation restore points
├── application-config.json          — application-global settings
├── logs/
│   └── bdo-client-YYYY-MM-DD.log — щоденні логи з ротацією
├── cache/
│   └── *.tmp/*.download            — application-global transient downloads
└── updates/                          — self-update сесії (Stage 13)
    └── {GUID}/                       — одна сесія оновлення
        ├── update-session.json       — стан сесії (див. docs/update.md)
        └── ...                       — staged candidate EXE та manifest
```

**Примітки:**
- Scoped `release-feed.json` записується лише після валідного live API response, має schema version і UTC timestamp. У ньому немає history, install_path_patterns або transient diagnostics.
- Cached feed використовується тільки для карток і локального read-only state resolution. Install/update/switch/restore original вимагають нового live API response; помилка кешу не робить live startup невдалим.
- `games/black-desert-online/` є canonical BDO persistence scope; шлях до гри,
  installation state та backups належать лише цьому scope.
- При першому запуску після Stage 2 старий `{root}\config.json` розкладається
  між game config (game path/last mode) та global `application-config.json`
  (autostart prompt), а `{root}\state` і `{root}\backups` переносяться через
  `LegacyBdoPersistenceMigrator`.
  Якщо canonical item уже існує, він authoritative; merge/overwrite не виконується.
- JSON formats і backup contents не змінюються: `installation.json` оновлюється
  лише після успішного встановлення, original snapshot не перезаписується,
  restore points залишаються попередніми версіями локалізації.
- application config, logs і updates залишаються application-global; global cache directory збережено для transient downloads і legacy residue, але active release-feed ownership є game-scoped.
- `updates/<GUID>/` — staged candidate нового EXE; current EXE не змінюється до повної верифікації.

---

## 5. HttpClient instances

Створюються через composition root і BDO session, два окремі екземпляри:

```
HttpClient #1 (BdoUaHttpClientConfiguration.CreateHttpClient)
│   SocketsHttpHandler + ResilientConnectionConnector (happy-eyeballs connect)
│   User-Agent: BdoUaClient/<version> (+https://bdo-ua.com.ua), UseProxy = false
├── BdoUaApiClient          — GET /releases (API запити, JSON)
└── LocalizationInstaller   — GET download_url / official_source_url

HttpClient #2 (GitHub updater)
│   HttpClientHandler, UseProxy = false, без токена
└── GitHubUpdateClient      — GitHub Releases API (self-update)
```

**Переваги спільного HttpClient #1:**
- Переиспользование TCP-з'єднань (connection pooling).
- Уникнення socket exhaustion.
- Спільний timeout та default headers.

**Примітка:** BDO session володіє першим `HttpClient` і dispose-ить його після завершення MainForm; GitHub updater має окремий application-global екземпляр із поточним process lifetime.

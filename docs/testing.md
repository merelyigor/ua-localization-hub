# Тестування

## Framework

- **xUnit** — єдиний test framework для проєкту
- **net8.0-windows** — target framework для test project (`BdoClient.Tests`)
- ProjectReference на основний `BdoClient.csproj`

## Кількість тестів

Точна кількість змінюється з кожним етапом; authoritative source — результат `dotnet test BdoUaClient.sln` та CI.

## Категорії тестів

### API тести

| Файл | Що тестує |
|------|-----------|
| `Api/ApiResultTests.cs` | `ApiResult<T>` — success/failure створення, значення, помилки |
| `Api/BdoUaApiClientTests.cs` | JSON deserialization, HTTP помилки (4xx, 5xx, timeout, DNS), null `current`, malformed JSON, порожня відповідь, cancellation |
| `Api/BdoUaHttpClientConfigurationTests.cs` | User-Agent формат, конфігурація HttpClient |
| `Api/NetworkDiagnosticsTests.cs` | Форматування мережевих помилок |
| `Api/ResilientConnectionConnectorTests.cs` | Happy-eyeballs connect: parallel attempts, stagger, ordering |

### Model тести

| Файл | Що тестує |
|------|-----------|
| `Models/ReleasesResponseTests.cs` | `ReleasesResponse` serialization/deserialization, edge cases |

### Service тести

| Файл | Що тестує |
|------|-----------|
| `Services/GameDetectorTests.cs` | Path validation, Steam parsing, manual resolution, registry fallback, Unicode/пробіли |
| `Services/BdoGameDefinitionTests.cs` | BDO identity, canonical localization target/validation та `ads_files` patch ownership |
| `Services/LocalizationInstallerTests.cs` | Download, SHA-256 перевірка, retry з backoff, progress reporting, cancellation |
| `Services/LocalizationInstallServiceTests.cs` | Transactional install, rollback при помилці, cancellation |
| `Services/LocalizationStateServiceTests.cs` | State resolution: усі значення `LocalizationState` + patch transitions |
| `Services/LocalizationStatePresentationTests.cs` | UI-тексти станів та patch transitions |
| `Services/RestoreOriginalServiceTests.cs` | Restore через `official_source_url`, fallback snapshot, patch mismatch |
| `Services/RestoreBackupServiceTests.cs` | Catalog restore points, create restore point, restore success/failure |
| `Services/DynamicModePolicyTests.cs` | Mode filtering, `ResolveInitialSelection` (обидва перевантаження) |
| `Services/InstallActionPolicyTests.cs` | Install policy, exact target resolution |
| `Services/LocalizationCompatibilityServiceTests.cs` | Compatibility checks |
| `Services/StartupOrchestrationTests.cs` | Parallel startup orchestration, fallback, callback safety |
| `Services/ApiErrorPresentationTests.cs` | `ApiErrorKind` → Ukrainian message mapping |
| `Services/AdsFilesPatchReaderTests.cs` | Читання патчу з `ads_files`, неоднозначний вміст, IO помилки |
| `Services/ReleaseFeedPollerTests.cs` | Background polling, pause/resume, events, dispose |
| `Services/FeedChangeDetectorTests.cs` | Семантичне порівняння feed'ів, ігнорування GeneratedAt |
| `Services/FeedApplicationCoordinatorTests.cs` | Pending черга, block/unblock, requeue при невдачі |

### Update тести (self-update)

| Файл | Що тестує |
|------|-----------|
| `Update/ApplicationCommandLineTests.cs` | Парсинг `--apply-update`, exit codes аргументів |
| `Update/AppVersionTests.cs` / `AppVersionInfoTests.cs` | Numeric version comparison, детекція версії EXE |
| `Update/GitHubReleaseModelTests.cs` / `GitHubUpdateClientTests.cs` | GitHub Releases API клієнт |
| `Update/UpdateSelectionPolicyTests.cs` | Вибір candidate, channel policy, numeric ordering |
| `Update/UpdateManifestValidatorTests.cs` | Schema-2 manifest валідація |
| `Update/ExecutableVersionValidatorTests.cs` | Перевірка версії staged EXE |
| `Update/UpdatePackageServiceTests.cs` / `UpdateZipValidationTests.cs` | Завантаження/розпакування ZIP, валідація вмісту |
| `Update/ReplacementWorkspaceTests.cs` | Staging директорія candidate EXE |
| `Update/PreparedAttemptCleanupTests.cs` | Cleanup незавершених сесій |
| `Update/UpdateSessionStoreTests.cs` | Сесійний стан у updates/<GUID>/ |
| `Update/SelfUpdatePreparationServiceTests.cs` | Повний цикл підготовки оновлення |
| `Update/SelfUpdateApplierTests.cs` / `SelfUpdateReplacementRetryTests.cs` | Helper mode заміна EXE, rollback, retry |
| `Update/StartupUpdateLifecycleCoordinatorTests.cs` | Startup maintenance cleanup |
| `Update/UpdateLifecycleServiceTests.cs` | Координація check → prepare → apply |
| `Update/UpdateButtonStateTests.cs` | Стани кнопки оновлення |

### Root UI-policy тести

| Файл | Що тестує |
|------|-----------|
| `InstallButtonLabelPolicyTests.cs` | Контекстний текст кнопки («Встановити»/«Оновити»/exact target) |
| `LocalizationFlagParserTests.cs` | Парсинг UA/GB прапорців |
| `ModeCardPresentationPolicyTests.cs` | Презентація карток режимів, exact installed badge |

### MainForm lifecycle integration тести

| Файл | Що тестує |
|------|-----------|
| `MainFormLifecycleIntegrationTests.cs` | STA/message-loop startup wiring, degraded startup completion, secondary activation до існуючого MainForm та clean shutdown при pending startup request |

Ці тести використовують окремий STA thread і контрольований WinForms message loop, тимчасові `AppPaths` та локальні HTTP handlers. Native tray rendering, taskbar/foreground focus і реальна multi-process shell activation залишаються OS-dependent manual validation.

### Where Winds Meet tests

| Файл | Що тестує |
|------|-----------|
| `Services/WwmStage10Tests.cs` | Winds4UA latest DTO/client та failures, dynamic `slug + variant` resolution, Steam manifest/library/root markers, ZIP allow-list/size/SHA/path/link validation, isolated two-file install/rollback/cancellation/journal recovery/pre-Hub restore та installed-state resolution; усі game roots/AppPaths/archive/HTTP handlers — тимчасові fixtures |
| `Services/GameCatalogTests.cs` | Реєстрація BDO і WWM production descriptors без зміни BDO ID |
| `Services/SelectedGameSessionHostTests.cs` | Володіння та переключення BDO ↔ WWM session |
| `MainFormLifecycleIntegrationTests.cs` | Persisted WWM startup, BDO → WWM → BDO isolation, latest mode becoming unavailable, compatibility decline/accept та session lifecycle |

WWM tests не викликають live Winds4UA API, Registry або реальну Steam/game installation. Мутаційні fixtures створюються тільки у temp directory; вони не є native install/restore smoke.

### Storage тести

| Файл | Що тестує |
|------|-----------|
| `Storage/BackupStoreTests.cs` | Original snapshot створення/захист, restore points каталог, replace, recovery, metadata |
| `Storage/ConfigStoreTests.cs` | Game-scoped config load/save, default values, corrupt file recovery, application-global autostart config roundtrip |
| `Storage/InstallationStateStoreTests.cs` | Installation state persistence, validation, migration |
| `Storage/AppPathsTests.cs` | App paths resolution, directory structure |
| `Storage/GamePersistencePathsTests.cs` | Stable game namespace, global paths and game-id safety |
| `Storage/LegacyBdoPersistenceMigratorTests.cs` | Legacy BDO migration, idempotency, conflicts and fail-closed malformed state |

### Logging тести

| Файл | Що тестує |
|------|-----------|
| `Logging/FileLoggerTests.cs` | Log levels (DEBUG/INFO/WARNING/ERROR), file format, concurrent write safety, invalid path handling |

## Тестові патерни

Storage/ReleaseFeedCacheStoreTests.cs покриває normalized cache roundtrip, UTC timestamp,
empty feed, corruption/schema/identity rejection, atomic-preserve behavior та відсутність
history/install_path_patterns у persisted snapshot.

### Temp directories

Тести створюють тимчасові директорії через `Path.GetTempPath()` + унікальний суфікс. Кожен тест прибирає за собою в `Dispose()`.

### IDisposable

Test класи реалізують `IDisposable` для cleanup temp files, відновлення стану.

### MockHttpHandler

`MockHttpHandler` (або аналогічний stub) — мок для `HttpMessageHandler`, що дозволяє:
- Повертати задані HTTP відповіді
- Імітувати timeout, DNS error, 4xx/5xx
- Перевіряти кількість та зміст запитів

### Test seam subclasses

Для тестування error paths використовуються спеціалізовані підкласи:
- `ThrowingBackupStore` — імітує помилку при backup операціях
- Інші аналогічні stubs для boundary testing

## Команди запуску

```bash
# Всі тести
dotnet test BdoUaClient.sln

# Без rebuild (якщо вже зібрано)
dotnet test BdoUaClient.sln --no-build

# З verbose output
dotnet test BdoUaClient.sln --verbosity normal

# Конкретний клас тестів
dotnet test BdoUaClient.sln --filter "FullyQualifiedName~GameDetectorTests"
```

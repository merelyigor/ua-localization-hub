# Self-update Хабу Українізаторів BDO - WWM (Stage 13)

Механізм оновлення самого застосунку. Джерело — публічні GitHub Releases канонічного репозиторію `merelyigor/ua-localization-hub`. Legacy slug `merelyigor/bdo-ua-client` зберігається лише як compatibility fallback. Без custom backend, без GitHub token, без HTTP (тільки HTTPS).

## Принципи (AGENTS §41)

- Автоматична перевірка оновлень під час startup і приблизно кожні 5 хвилин, доки процес працює, зокрема у background/tray; не блокує UI.
- Встановлення — лише після explicit натискання «Оновити до vX.Y.Z». Ніякого silent/forced update.
- Порівняння версій — тільки numeric (`AppVersion`: 0.1.9 < 0.1.10), ніколи lexicographic.
- Channel policy: якщо current release prerelease → дозволені newer prerelease + stable; інакше — тільки newer stable.
- Current EXE ніколи не змінюється, доки verified candidate не готовий.
- Backup перед replace, rollback при будь-якій помилці. Ніколи «old deleted, new not installed».
- Application update та localization операції взаємовиключні.

## Транспорт: canonical ZIP

Єдиний release asset — GitHub-generated ZIP `BDO-UA-Client-vX.Y.Z-win-x64.zip`, всередині якого чотири flat-файли:

| Файл | Призначення |
|---|---|
| `BDO-UA-Client.exe` | Застосунок |
| `release-manifest.json` | Schema-2 manifest (internal) |
| `SHA256SUMS.txt` | Суми файлів |
| `RELEASE_NOTES-vX.Y.Z.md` | Нотатки релізу |

Немає project-created nested ZIP, немає `.7z`/`.tar` тощо. GitHub asset digest валідує зовнішній ZIP; updater валідує внутрішній EXE SHA-256 та version metadata.

## Компоненти (Update/)

| Клас | Відповідальність |
|---|---|
| `ApplicationCommandLine` | Парсинг `--apply-update <session-id>`, exit codes аргументів |
| `AppVersion` / `AppVersionInfo` | Numeric semantic версія + детекція версії поточного EXE |
| `GitHubUpdateClient` / `GitHubRelease` / `GitHubResult` | Запит до GitHub Releases API (окремий HttpClient, `UseProxy=false`, без токена) |
| `UpdateSelectionPolicy` | Вибір candidate: numeric comparison + channel policy |
| `UpdateManifestValidator` | Валідація schema-2 manifest (schema_version, version, sha256, asset_name) |
| `ExecutableVersionValidator` | Перевірка FileVersion/ProductVersion staged EXE проти manifest |
| `UpdatePackageService` / `UpdatePackageResult` | Завантаження та розпакування ZIP, валідація вмісту |
| `ReplacementWorkspace` | Staging-директорія для candidate EXE |
| `PreparedAttemptCleanup` | Очищення незавершених сесій підготовки |
| `UpdateSession` / `UpdateSessionStore` | Стан сесії у `%LocalAppData%\BDO-UA-Client\updates\<GUID>\update-session.json` |
| `SelfUpdatePreparationService` | Повний цикл підготовки: download → validate → stage → session |
| `SelfUpdateApplier` | Helper mode: заміна EXE + restart + rollback, exit codes |
| `StartupUpdateLifecycleCoordinator` | Startup maintenance: cleanup незавершених сесій (`RunStartupMaintenance()`) |
| `UpdateLifecycleService` | Координація check → prepare → apply |
| `UpdateButtonState` | Обчислення стану кнопки «Оновити до vX.Y.Z» |
| `ForegroundWindowHelper` | Допоміжна робота з фокусом вікон |

## Lifecycle

```
Startup
├── StartupUpdateLifecycleCoordinator.RunStartupMaintenance()
│     └── cleanup незавершених prepared sessions
├── Application update monitoring
│     ├── негайна перевірка під час startup
│     ├── періодична перевірка приблизно кожні 5 хвилин
│     └── приховане tray-сповіщення один раз для кожного tag (RAM-only dedup)
│           └── GitHubUpdateClient → UpdateSelectionPolicy → candidate?
├── Restore / «Перевірити зараз»
│     └── негайний application-update check; «Перевірити зараз» також запускає localization poll
├── Користувач натискає «Оновити до vX.Y.Z»
│     └── SelfUpdatePreparationService:
│           download ZIP → validate manifest → extract EXE →
│           SHA-256 + version verification → stage у updates/<GUID>/ → session saved
└── Handoff
      └── запуск staged EXE з --apply-update <session-id>,
          завершення поточного процесу
```

Application-update discovery не завантажує і не встановлює оновлення автоматично. Якщо нова версія знайдена, видима MainForm показує звичайну кнопку оновлення, а прихований процес може показати одне інформаційне tray-сповіщення для цього tag. RAM-only tracker не повторює сповіщення для того самого tag; новий tag починає новий епізод. Restore з tray і пункт «Перевірити зараз» запитують свіжу application-update перевірку. Фактичне завантаження та self-update починаються лише після натискання користувачем кнопки оновлення.

### Helper mode (`--apply-update <session-id>`)

Staged EXE запускається як helper. `SelfUpdateApplier.RunAsync(sessionId)`:
1. Читає session з `updates/<GUID>/`
2. Чекає завершення батьківського процесу (timeout → rollback)
3. Backup current EXE → заміна → верифікація SHA-256/version
4. Restart нової версії; при невдачі — rollback до backup

UI — `UpdateApplyingForm`.

### Exit codes (`SelfUpdateApplier`)

| Код | Значення |
|---|---|
| `ExitCodeSuccess` | Оновлення застосовано |
| `ExitCodeRestartFailedRecovered` | Помилка, попередню версію відновлено і запущено |
| `ExitCodeParentTimeout` | Батьківський процес не завершився вчасно; версію не змінено |
| `ExitCodeVerificationFailed` | Перевірка цілісності не пройдена |
| `ExitCodeReplaceFailed` | Помилка заміни файлу |
| `ExitCodeRestartFailed` | Не вдалося перезапустити автоматично |

## Обмеження

- Ніякого UAC elevation (Stage 13).
- Ніякого permanent Updater.exe, PowerShell/BAT updater, Windows Service — тільки internal helper mode.
- TLS verification ніколи не вимикається.

## Repository discovery bridge

Поточний updater спочатку запитує canonical repository `merelyigor/ua-localization-hub`. Якщо canonical endpoint повертає HTTP 404, bounded discovery один раз пробує legacy compatibility slug `merelyigor/bdo-ua-client`. Успішна canonical-відповідь не викликає другий endpoint; 401/403/rate-limit, 5xx, network failure та malformed JSON не запускають fallback і залишаються звичайною помилкою discovery. Asset download URLs не переписуються й використовуються в тому вигляді, у якому їх повернув GitHub.

Stage 6 централізує ці compatibility identities, але фізично зберігає `BDO-UA-Client.exe`, `BDO-UA-Client-vX.Y.Z-win-x64.zip`, autostart value `BDO-UA-Client` і `%LocalAppData%\\BDO-UA-Client`. Repository вже перейменовано на canonical `merelyigor/ua-localization-hub`; старий slug не повинен повторно використовуватися.

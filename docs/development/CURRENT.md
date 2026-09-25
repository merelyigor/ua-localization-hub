# Current Engineering Context

Оновлено: 2026-09-25

## Project Purpose / Status

Хаб Українізаторів BDO - WWM — Windows .NET 8 WinForms застосунок для роботи з українськими локалізаціями ігор. Цільові проєкти: BDO UA Translate для Black Desert Online ([bdo-ua.com.ua](https://bdo-ua.com.ua/)) — інтегровано та доступно; Winds4UA (W4U) для Where Winds Meet ([winds4ua.com.ua](https://winds4ua.com.ua/)) — заплановано, але ще не інтегровано. Поточна production-підтримка охоплює лише Black Desert Online, включно з безпечним встановленням, оновленням та відновленням файлів гри.

Стабільний реліз: **v1.2.8**. Публічний stable release опубліковано з tag v1.2.8; canonical application bundle містить один ZIP-asset. Offline/degraded release-feed implementation завершено та прийнято зовнішнім Architect.

Поточна наступна дія: **OWNER DECISION REQUIRED**. Stage 8C завершено; подальшу роботу не авторизовано.

Owner approved and explicitly activated the PRIMARY roadmap `localization-hub-multigame` for the `Хаб Українізаторів BDO - WWM` multi-game transition. v1.2.7 external release acceptance is complete; Stages 0–8A and Stage 8C are **REVIEWED / ACCEPTED**. Stage 8C external Architect review found BLOCKER `0` and IMPORTANT `0`; Owner native/visual smoke accepted the branding, target projects, adaptive height, tray behavior and BDO UI. Stage 8B remains optional/not started, Stage 9 awaits Where Winds Meet / Winds4UA technical integration data, and Stage 10 has not started. No further implementation stage is authorized; next action is **OWNER DECISION REQUIRED**.

## Architecture Summary

- `Program.cs` є manual composition root без DI-контейнера.
- `MainForm` координує UI та application services; довгі HTTP/file operations виконуються async.
- `Api/BdoUaApiClient` володіє API-запитами до `/releases`.
- `Services/LocalizationInstaller` відповідає за download, retry, checksum та timeout локалізації.
- `Storage` відповідає за config, installation state, original snapshot і restore points.
- `Update` містить GitHub Release discovery, schema-2 bundle validation, staging, replacement helper, rollback та startup maintenance.

## Tray / Background Contract

- Звичайне X ховає MainForm у Windows tray і не завершує процес; `Відкрити` та подвійний клік відновлюють те саме вікно; `Вихід` є фактичним завершенням.
- Нормальний і background запуск використовують single-instance activation; повторний запуск активує існуючий клієнт.
- Видимий API polling працює приблизно кожні 15 секунд, прихований — приблизно кожні 5 хвилин; локальний файл локалізації у background перевіряється дешевим metadata fingerprint.
- Localization update notification і application version update notification — окремі інформаційні канали з RAM-only dedup; application notification показується один раз для кожного tag у hidden tray. Click-to-open не є контрактом.
- Application-update discovery виконується одразу під час startup і приблизно кожні 5 хвилин, а restore та `Перевірити зараз` запитують свіжу перевірку. Download/install application update не є автоматичними.
- Приховування вікна не перериває активну операцію; explicit Exit зберігає безпечну семантику завершення.

## Current Phase

- Stage A — accepted.
- Stage C — MainForm physical decomposition, accepted.
- Tray/background T1–T6 — accepted, released in v1.2.0, plan archived.
- Stage B — **COMPLETED / REVIEWED / ACCEPTED**. B.1/B.2/B.3 прийняті; install rollback, selected restore-point state apply та restore-backup rollback мігровані на `InstallationStateStore.RestoreRawStateAsync`; typed `SaveAsync`, `BackupStore` snapshots і transaction orchestration залишаються окремими.
- Stage D — **COMPLETED / REVIEWED / ACCEPTED**; D.1 підтвердив negligible UI-thread local IO у realistic scenarios, D.2 — **NOT REQUIRED**.
- Code-quality roadmap — **COMPLETED / REVIEWED / ACCEPTED**. Stage E.1 — **COMPLETED / REVIEWED / ACCEPTED**; E.2 — **NO ACTION REQUIRED / ALREADY SATISFIED**, бо `LocalizationModeCard` уже має hover surface/border feedback.
- `code-quality-ux-improvements` — **ARCHIVED**, roadmap COMPLETED / REVIEWED / ACCEPTED, released through stable v1.2.1; no remaining implementation action.
- `release-experience-polish` — **ARCHIVED**. R1/R2/R3 — **COMPLETED / REVIEWED / ACCEPTED**; application discovery має startup + resident ~5-minute monitoring, а schema-v1 `NEXT.json` generator є normal release contract.
- Exact v1.2.2 facts: release ID `383636389`, RC #29 / run `34038984319`, one public asset `BDO-UA-Client-v1.2.2-win-x64.zip`; outer SHA-256 `329c31987955dbb2139a061ea09bbad0e89fa403cf731343d124b917e68f120c`. Власник успішно виконав built-in update до `v1.2.2`; застосунок після оновлення працює. Hidden periodic notification не спостерігалася окремо в production smoke.
- v15.41 — **REVIEWED / ACCEPTED**: bounded test-only MainForm lifecycle integration coverage додано без production changes; testability gate пройдено через dedicated STA/message-loop fixture.
- v15.42 — **REVIEWED / ACCEPTED**: додано README та MainForm informational guidance для видалення portable-клієнта без self-uninstall механізму; localization, autostart і storage behavior не змінювалися.
- v15.43 — **REVIEWED / ACCEPTED**: Release Candidate workflow більше не створює фінальний tag; Owner native UI smoke gate успішно пройдено перед публікацією `v1.2.3`.
- v15.46 — **REVIEWED / ACCEPTED**: додано normalized last-known release-feed cache, cached read-only fallback та Live-only mutation gate; API, installer safety, GameDetector threading і self-update architecture не змінювалися.
- v15.47 — **REVIEWED / ACCEPTED**: repository AI workflow синхронізовано навколо default Combined mode, risk-based pre-commit review, evidence policy та terminal state `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`; production і release behavior не змінювалися.
- v15.49 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: portable Architect bootstrap скорочено до 3505 UTF-16 code units, додано hard limit gate `7500` і його normal CI перевірку; production та release behavior не змінювалися.
- v15.51 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: repository rules зафіксували дозволене використання локально автентифікованого GitHub CLI для task-authorized repository/CI operations із перевіркою target SHA та без persistence credentials.
- v15.52 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: game-found status розділяє локальний та останній відомий patch; застаріла гра показується як warning, а localization write-actions блокуються до оновлення гри. API, storage schema та install/restore transaction behavior не змінювалися.
- v1.2.4 — **PUBLISHED / VERIFIED**: public stable release ID `386705757` опубліковано на tag `v1.2.4`, який вказує на `e2694eb6d4288d7341d11b8bc187ce2a98b1e2de`; canonical ZIP і внутрішні hashes повторно перевірено. `NEXT.json` скинуто до порожнього schema-v1 джерела.
- v15.55 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: tray restore та live feed application повторно перечитують локальний game patch і повністю перераховують залежні localization state/actions; додано fail-closed recovery для malformed `ads_files` та global minimum usable MainForm width для one-mode layout. Production API, storage schema і release workflow не змінювалися.
- v1.2.5 — **PUBLISHED / VERIFIED**: public stable release ID `386734323` опубліковано на tag `v1.2.5`, який вказує на approved RC SHA `fd17f4346159234c8165c2b808b5c16309076e2e`; canonical ZIP і внутрішні hashes повторно перевірено, Owner native UI smoke exact RC пройдено. `NEXT.json` скинуто до порожнього schema-v1 джерела.
- `offline-degraded-reliability` — **ARCHIVED**: roadmap завершено, reviewed / accepted; archived plans залишаються історичними.
- v1.2.3 — **PUBLISHED / VERIFIED**: tag `v1.2.3` і public Release ID `384894464` опубліковано на exact approved RC SHA `ec891ea7077dd70e2aafd0c2e00674f47c45a94a`; Owner native UI smoke exact RC passed.
- `game-boundary-refactoring` — **ARCHIVED**: Stage 1 і Stage 2 **REVIEWED / ACCEPTED**; roadmap **COMPLETED / ARCHIVED**, без Stage 3.
- v1.2.6 — **RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED**: public Release ID `388677576` опубліковано на tag `v1.2.6`, який вказує на approved RC SHA `daba3b6aa861f072b382a3b1f92a24d52b94763f`; Owner native UI smoke exact RC прийнято, canonical ZIP і внутрішні hashes повторно перевірено. `NEXT.json` скинуто до порожнього schema-v1 джерела.

## Validation / Release Facts

- RC #29 succeeded; stable v1.2.2 published.
- Production self-update to `v1.2.2` was successfully exercised by the owner; the updated application works.
- Structured release-note pipeline is the normal release contract; `NEXT.json` has been reset to an empty schema-v1 source for the next cycle.
- R3 validation: Release build 0 warnings / 0 errors, 907 tests passed / 0 failed, resolver/generator tests and release preflight passed.
- v15.41 validation: Release build — 0 warnings / 0 errors; full Release suite — 911 passed / 0 failed / 0 skipped; focused MainForm lifecycle suite — 4 passed; 20/20 independent targeted invocations — 4/4 passed; related lifecycle suites passed.
- v15.46 validation: Release build — 0 warnings / 0 errors; full Release suite — 924 passed / 0 failed / 0 skipped; focused cache/policy/poller/MainForm suite — 87 passed; five independent MainForm lifecycle invocations passed; git diff --check passed.
- v1.2.3 validation: RC #30 / run `34236529921`, public Release ID `384894464`, canonical asset `BDO-UA-Client-v1.2.3-win-x64.zip` (asset ID `550865653`, 67,904,470 bytes, SHA-256 `0c740be029bfe30a2d020109a817a64af2eb927c24ab143fa868c3fe279718fe`), internal EXE SHA-256 `2abafb502de7f6b8effc8e3ee620afd813ce4ad544fe49d908739e3d4487932b`; public asset downloaded back and verified, live self-update eligibility from v1.2.2 confirmed.
- v15.55 validation: focused patch/tray/feed/layout suite — 20 passed; Release build — 0 warnings / 0 errors; full Release suite — 938 passed / 0 failed / 0 skipped; `git diff --check` passed.
- v1.2.5 validation: RC #33 / run `34550224363`, public Release ID `386734323`, canonical asset `BDO-UA-Client-v1.2.5-win-x64.zip` (asset ID `556244252`, 67,905,838 bytes, SHA-256 `e505971b3ed7946d085ce1fe169c9ab45cd73689f49c71393ab49ca65e7e8b7d`), internal EXE SHA-256 `f0a89d1348e66fa2cadae41d37a8c945282bb7753737d0593d1a8114df7cf5e3`; public asset downloaded back and verified, Owner native UI smoke passed.
- Stage 1 game-boundary validation: Release build — 0 warnings / 0 errors; full Release suite — 940 passed / 0 failed / 0 skipped; focused boundary/detection/install/restore/lifecycle suite — 216 passed; `git diff --check` passed.
- Stage 2 persistence validation: focused migration/isolation suite — 15 passed; relevant install/restore/detection/state/MainForm lifecycle suites — 170 passed; Release build — 0 warnings / 0 errors; full Release suite — 957 passed / 0 failed / 0 skipped; `git diff --check` passed. MainForm off-screen background restore regression coverage and native secondary-activation validation passed.
- Stage 2 runtime/session validation: focused session/MainForm suite — 20 passed; relevant runtime/storage/feed/install/restore/detection suites — 282 passed; Release build — 0 warnings / 0 errors; full Release suite — 971 passed / 0 failed / 0 skipped; `git diff --check` passed. External pre-commit Architect review accepted with BLOCKER 0 / IMPORTANT 0; implementation commit and CI completed successfully.
- Stage 3 corrective validation: focused session/startup/poller/MainForm lifecycle coverage passed; full Release suite — 980 passed / 0 failed / 0 skipped; Release build — 0 warnings / 0 errors; `git diff --check` passed. Coverage includes persisted known-game startup activation, deterministic switch completion without fixed sleeps, synthetic A→B→A restoration, stale old-session feed rejection and selector blocking during mutation. Stage 3 is **REVIEWED / ACCEPTED**.
- Stage 4 validation: focused GamePersistencePaths — 7 passed; ReleaseFeedCacheStore — 12 passed; LegacyBdoReleaseFeedCacheMigrator — 3 passed; LegacyBdoPersistenceMigrator — 8 passed; BdoGameSession — 4 passed; SelectedGameSessionHost — 2 passed; MainForm lifecycle — 23 passed; ReleaseFeedPoller — 30 passed; StartupOrchestration — 15 passed. Full Release suite — 988 passed / 0 failed / 0 skipped; Release build — 0 warnings / 0 errors. Stage 4 remains **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**; canonical cache path is `{root}\games\<stable-game-id>\cache\release-feed.json`, while the old global file is legacy-only.
- v1.2.6 RC validation: normal CI #204 / run `34873696880` and RC #34 / run `34873972036` succeeded; RC artifact `BDO-UA-Client-v1.2.6-win-x64` contains four flat files, ZIP size `67,914,367` bytes, ZIP SHA-256 `cf999c7059c8c1eecb2ff68053feb59410c30c3956a1a8e214bd078240c1c435`, EXE SHA-256 `65dfb3d2e76e011e256e3d6f6c7d27994a8ac54f89d6e64dbb86e9e9328fcd35`, manifest schema 2 and version 1.2.6.

## Stage 6 validation

- Technical identity and repository-bridge focused coverage — 42 passed; relevant update/package/manifest/session/autostart/AppPaths/MainForm update coverage — 225 passed; release-note generator — passed; release-version resolver — 13 passed.
- Full Release suite — 997 passed / 0 failed / 0 skipped; Release build — 0 warnings / 0 errors; `git diff --check` passed. Physical technical identities remain unchanged; Stage 6 is **REVIEWED / ACCEPTED** with external pre-commit review BLOCKER 0 / IMPORTANT 0.

## Stage 7A bridge RC validation

- RC #36 / run `35353772206` succeeded from exact accepted Stage 6 source SHA `7cb740d55bc7baeb5aa9f91365114e68bb88eff9`; artifact ID `10550809226`, `BDO-UA-Client-v1.2.8-win-x64`.
- The exact Actions artifact contains four flat files. ZIP size is `67,924,024` bytes and SHA-256 is `33d4f7baacac5b5a38a28140295663e6b1f865b01231e8845e6b46f1f87bf6f2`; EXE size is `162,458,694` bytes and SHA-256 is `bb4ce07897b4ca916e746cf41071deddcd1a9e6daa50623bfba3b5785dfc33e4`. FileVersion is `1.2.8.0`, ProductVersion is `1.2.8`, and Hub product metadata is preserved.
- Schema-2 manifest, `SHA256SUMS.txt`, and generated release notes validate against version `1.2.8`, tag `v1.2.8`, exact source SHA, run ID, legacy EXE name and current repository links. Historical v1.2.7 updater compatibility preflight passed 59 focused tests; live v1.2.7 → v1.2.8 update remains impossible until publication.

## Stage 7B public release validation

- Stable GitHub Release `v1.2.8` опубліковано як не-draft і не-prerelease: Release ID `391585430`, published `2026-09-18T15:31:08Z`, tag `v1.2.8` peeled to exact RC source SHA `7cb740d55bc7baeb5aa9f91365114e68bb88eff9`.
- Public asset ID `572906634`, `BDO-UA-Client-v1.2.8-win-x64.zip`, size `67,924,024` bytes, SHA-256 `33d4f7baacac5b5a38a28140295663e6b1f865b01231e8845e6b46f1f87bf6f2`; public re-download matches the accepted RC exactly.
- Public ZIP повторно пройшов чотирифайлову, EXE, manifest, `SHA256SUMS.txt` та release-notes validation. Lifecycle: **RELEASED / PUBLIC VERIFIED / LIVE LEGACY UPDATE PENDING**; repository rename не авторизовано.

## Stage 7 post-rename operational evidence

- Repository `merelyigor/bdo-ua-client` перейменовано в `merelyigor/ua-localization-hub`; repository ID `1332444174` збережено. Description оновлено на актуальний Hub wording, local origin переключено на `https://github.com/merelyigor/ua-localization-hub.git`, а main/історія/tags/releases/Actions збережено.
- Owner live public v1.2.7 → v1.2.8 self-update: **ACCEPTED**. Після rename exact shipping v1.2.8 успішно виконав update discovery: `GitHub update: fetched 17 releases from bdo-ua-client`, `Update: no eligible newer release found`, `Update check: no eligible update`; повторна перевірка також завершилася без помилки. Exact v1.2.7 після rename знайшов candidate v1.2.8.
- Old repository page, Releases page, v1.2.8 URL і public asset redirect/resolve коректно; old API endpoint повертає GitHub move behavior до repository ID `1332444174`; old asset зберігає exact SHA-256 `33d4f7baacac5b5a38a28140295663e6b1f865b01231e8845e6b46f1f87bf6f2`.
- Поточний стан: **POST-RENAME NORMALIZATION PRE-COMMIT REVIEWED / ACCEPTED / PENDING FINAL EXTERNAL REVIEW**. External Architect: BLOCKER `0`, IMPORTANT `0`; commit, push і CI авторизовано. Canonical repository `merelyigor/ua-localization-hub` використовується в active source/docs; legacy slug `merelyigor/bdo-ua-client` збережено лише як compatibility fallback і historical evidence.

## Important Invariants

- API contract — `GET https://bdo-ua.com.ua/api/public/v1/releases`; актуальний release визначає сервер.
- Release compatibility перевіряється до install/update; incompatible release не завантажується.
- Game file operations: download/temp → validation → snapshot/restore point → replace → verify → state commit.
- Original snapshot незмінний; restore points — окремі pre-operation recovery points.
- Self-update current EXE не змінюється до manifest, SHA-256 і version validation.
- Secrets, tokens і credentials не зберігаються в repository.
- `docs/ai-workflow/` є canonical orchestration/process documentation: repository формально розділяє Owner, Architect-Reviewer та Implementation Agent responsibilities; external conversations — coordination, а repository-owned docs/code — persistent truth.
- `docs/ai-workflow/PROJECT_CHAT_RULES.md` є Owner-maintained portable bootstrap prompt для Architect-chat sessions з hard limit `7500` UTF-16 code units; repository-specific workflow explanation залишається в інших ai-workflow docs.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — правила, контракти, security, build і commit requirements
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [docs/plans/archive/offline-degraded-reliability.md](../plans/archive/offline-degraded-reliability.md) — completed offline/degraded release-feed roadmap
- [`docs/plans/archive/release-experience-polish.md`](../plans/archive/release-experience-polish.md) — completed archived plan
- [`docs/ai-workflow/README.md`](../ai-workflow/README.md) — canonical orchestration, prompt, review та handoff contract
- [`docs/plans/archive/code-quality-ux-improvements.md`](../plans/archive/code-quality-ux-improvements.md) — completed archived roadmap
- [`docs/releases/v1.2.8.md`](../releases/v1.2.8.md) — current stable release archive
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal

## Current Task Handoff

- v1.2.3 release cycle is completed and archived.
- Offline/degraded release-feed mode is completed, reviewed and accepted; `offline-degraded-reliability` is archived.
- `game-boundary-refactoring` is archived; Stage 1 and Stage 2 are reviewed/accepted. `localization-hub-multigame` is the sole ACTIVE PRIMARY roadmap; Stage 0 through Stage 8A and Stage 8C are reviewed/accepted. Stage 8B remains optional/not started; Stage 9 awaits technical integration data; no next implementation stage is authorized.
- v1.2.5 release cycle completed; public Release verified and `NEXT.json` reset for the next cycle.
- v15.64/v15.65 — **REVIEWED / ACCEPTED; OWNER VISUAL SMOKE ACCEPTED**: для трьох і більше режимів локалізації додано компактний minimum window width і збережено його під час semantic rebuild; single-mode global status width збережено.
- v1.2.7 — **RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED**: public Release ID `391117230` опубліковано на tag `v1.2.7`, який вказує на exact approved RC SHA `b21118d1ec8e6d0342fb0e47544e911fcb875739`; canonical ZIP і внутрішні hashes повторно перевірено, Owner native smoke exact RC прийнято.
- v1.2.8 — **RELEASED / PUBLIC VERIFIED**: public Release ID `391585430` опубліковано на tag `v1.2.8`, який вказує на exact RC SHA `7cb740d55bc7baeb5aa9f91365114e68bb88eff9`; public asset повторно завантажено й перевірено. Owner live legacy update accepted.
- `localization-hub-multigame` — **ACTIVE / PRIMARY**: Stage 0 complete; Stages 1–8A and Stage 8C — **REVIEWED / ACCEPTED**. Stage 8C Architect review: BLOCKER `0`, IMPORTANT `0`; Owner native/visual smoke accepted, including the corrective empty/loading/failure mode-section sizing. Stage 8B remains **OPTIONAL / NOT STARTED**; Stage 9 remains **WAITING ON TECHNICAL INTEGRATION DATA** for Where Winds Meet / Winds4UA (W4U); Stage 10 is **NOT STARTED**.
- Next action: **OWNER DECISION REQUIRED**; Stage 8C is complete and no further implementation stage is authorized.
- Stage 8C final validation: CI #229 / run `36106040918` SUCCESS for `d726c939e0e6365ea2efd631e60cdf13528e836e`; Release build — 0 warnings / 0 errors; focused content-fit/MainForm/GameCatalog/session-host — 76 passed; full Release suite — 1008 passed / 0 failed / 0 skipped; release-note generator — 24 assertions and resolver — 13 passed; `git diff --check` passed.

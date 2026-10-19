# Current Engineering Context

Оновлено: 2026-10-19

## Persistent Windows update notifications — IMPLEMENTED / VALIDATED

Owner requested reliable notifications for Hub releases and BDO/WWM localization updates, including retention in Windows Notification Center. The v1.2.21 implementation replaces transient WinForms balloon tips with Windows toast notifications under a stable AUMID, logs Windows notification settings and lifecycle failures, and permits retry after a reported failure. Local Release build succeeded with 0 warnings/errors, full suite passed 1247/1247, focused notification tests passed 34/34, and the single-file publish workflow passed. Release facts and exact CI/artifact evidence will be recorded after the authorized publication. No real game files were touched. Windows may still suppress banners/history according to the user's notification/privacy settings.

## v1.2.20 — RELEASED / PUBLIC VERIFIED

Owner-authorized stable test release for verifying application-update tray delivery to an already-running v1.2.19 client. No production code changed from v1.2.19. Normal CI #282 / run `39165996840` passed; RC #52 / run `39173763003` passed 1237/1237 tests and produced exact source-bound bundles. Release ID `416758893` was published at `2026-10-18T22:55:20Z`; tag `v1.2.20` peels to `507fc8031c74578af3d144341e1f1bee827c7c74`. At `2026-10-19 01:59:14` local time, the running v1.2.19 tray process selected v1.2.20 and the journal recorded `Tray notification shown event received`; Windows closed it at `01:59:20`. WWM remained current at v2.6.7, so no localization-update notification was expected. Release facts are archived in [`docs/releases/v1.2.20.md`](../releases/v1.2.20.md). `NEXT.json` was already the canonical empty schema-v1 state. No real game files were changed; no external Architect acceptance is claimed.

## v1.2.19 — RELEASED / PUBLIC VERIFIED

Tray notification diagnostics and the WWM deferred-notification correction shipped in stable v1.2.19. Source `b1820d7011923ce90eb1b2f07b88652f61eaf0e0`; normal CI #280 / run `39164912753` and Release Candidate #51 / run `39165112480` passed, with 1237/1237 tests. Release ID `416710767` was published at `2026-10-18T20:59:10Z`; annotated tag `v1.2.19` peels to the exact source. Public canonical and legacy ZIPs were downloaded and matched their RC SHA-256 values. Details are archived in [`docs/releases/v1.2.19.md`](../releases/v1.2.19.md). The client initially inspected as v1.2.17 self-updated to v1.2.18 at 23:45, then while hidden detected v1.2.19 at `2026-10-19 00:00:30`; its old log confirms discovery but cannot attest Windows balloon delivery. Detailed notification events are logged after updating to v1.2.19. No real game files were changed. Owner authorized publication; no Architect acceptance is claimed.

## v1.2.18 release lifecycle — notification delivery test

Owner explicitly authorized a public stable release to test update notification delivery to an already-running v1.2.17 client. RC #50 / run `39158551643` succeeded from source `76c590c3170c18775142e33daa5c96fcd03e065a`; Release ID `416655440` was published at `2026-10-18T19:29:58Z`, and annotated tag `v1.2.18` peels to that source. The running hidden v1.2.17 process logged candidate selection and `Update available: v1.2.18` at `22:34:36` local time. This confirms the release-check/tray-notification code path was reached; no screenshot/native UI capture was available to independently attest the visible balloon. Public assets were independently downloaded and hash-verified. Facts are archived in [`docs/releases/v1.2.18.md`](../releases/v1.2.18.md). `NEXT.json` remains canonical empty schema-v1. Owner waived external Architect review; no external acceptance is claimed. No game files were changed. Lifecycle: **RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER**.

- Canonical public asset ID `654292788`, 68,006,515 bytes, SHA-256 `4caf09e9494b6748c6f71f9e64baab88a4c09aa1407ba7f4c6dab4aebd089616`.
- Legacy public asset ID `654292790`, 68,006,490 bytes, SHA-256 `69b0140cce5df587538bdadbb266c39c632916bf5df0497272d9f75a0144e7ce`.
- Internal EXE SHA-256 `c48d7d85c47a07cebbe2eea95fd7a6ad815624b9b02325fd6e10aa2345096a9d`; FileVersion `1.2.18.0`; ProductVersion `1.2.18`.

## v1.2.16 release lifecycle

Stable v1.2.16 (Release ID `416633779`) was published at `2026-10-18T18:47:32Z` from source `caaba7c11a698c47ddfdc17f7298de8d272a6e53`; annotated tag `v1.2.16` peels to that exact commit. Normal CI #275 / run `39154554322` and RC #48 / run `39155210817` succeeded; the RC passed 1231/1231 tests. Both exact RC artifacts were published unchanged and independently downloaded from the public release with matching SHA-256. Facts are archived in [`docs/releases/v1.2.16.md`](../releases/v1.2.16.md). Owner waived external Architect review for this publication; no external acceptance is claimed. `NEXT.json` is reset to canonical empty schema-v1. Lifecycle: **RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER**. No real game files were modified.

## v1.2.15 release lifecycle

Stable v1.2.15 (Release ID `416608010`) was published at `2026-10-18T17:56:20Z` from source `4cc97d616577786725a3878af4a1da46546dbf0f`; annotated tag `v1.2.15` peels to that exact commit. Normal CI #273 / run `39150722823` and RC #47 / run `39150949238` succeeded; the RC passed 1228/1228 tests. Both exact RC artifacts were published without repacking, then independently downloaded from the public release and hash-verified. Facts are archived in [`docs/releases/v1.2.15.md`](../releases/v1.2.15.md). Owner waived external Architect review; no external acceptance is claimed. `NEXT.json` is reset to canonical empty schema-v1. Lifecycle: **RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER**.

## v1.2.14 release lifecycle

Stable v1.2.14 (Release ID `416460308`) was published at `2026-10-18T13:15:50Z` from source `2156d36f08cd1fa7ac72c70b48fd94bf468fd2d3`; annotated tag `v1.2.14` peels to that exact commit. Normal CI #272 / run `39130202262` and RC #46 / run `39130386049` succeeded. Public canonical and legacy assets and exact release body are archived in [`docs/releases/v1.2.14.md`](../releases/v1.2.14.md). Lifecycle: **RELEASED / PUBLIC VERIFIED**.

## v1.2.13 release lifecycle

Stable v1.2.13 (Release ID `416446964`) was published at `2026-10-18T12:48:29Z` from source `4ca4199e2fd540459cf8e37857c9d0e6d361e623`; annotated tag `v1.2.13` peels to that exact commit. Normal CI #270 / run `39127970968` and RC #45 / run `39128159096` succeeded. Both canonical and legacy assets were independently downloaded and hash-verified; exact evidence is archived in [`docs/releases/v1.2.13.md`](../releases/v1.2.13.md). The public release is **RELEASED / PUBLIC VERIFIED / PENDING EXTERNAL RELEASE REVIEW**. `NEXT.json` is reset to the canonical empty schema-v1 state. No game files were modified.

## v1.2.12 release lifecycle

The externally reviewed implementation `f35a9457fbd7294c02ff86de781fc46952a0e887` was fast-forwarded unchanged from parent `8cbb7ccad0eb80365fe03e7c1ecb8708f217f7bf`. Exact-SHA normal CI #268 / run `39123483766` succeeded; RC #44 / run `39123687761` produced v1.2.12 and passed all bundle, manifest, source-SHA, EXE identity and release-notes checks. Stable Release ID `416416383` was published at `2026-10-18T11:42:14Z`; annotated tag `v1.2.12` peels to the implementation SHA, and both public assets were independently downloaded and hash-verified. v1.2.12 is **RELEASED / PUBLIC VERIFIED / PENDING EXTERNAL RELEASE REVIEW**. Exact evidence is archived in [`docs/releases/v1.2.12.md`](../releases/v1.2.12.md). `NEXT.json` was reset at publication; the subsequent Owner-requested WWM compatibility-dialog removal shipped in v1.2.13. No native or Owner smoke was performed for v1.2.12, as explicitly waived; no real game files were changed.

## Project Purpose / Status

Хаб Українізаторів BDO - WWM — Windows .NET 8 WinForms застосунок для українських локалізацій. BDO UA Translate для Black Desert Online та Winds4UA (W4U) для Where Winds Meet мають окремі production runtime sessions. Stage 11A detection/presentation і Stage 11B Windows multi-layout mutation **REVIEWED / ACCEPTED**.

Останній стабільний реліз: **v1.2.20** (Release ID `416758893`), source SHA `507fc8031c74578af3d144341e1f1bee827c7c74`; live latest — v1.2.20. Точні asset facts записані у [`docs/releases/v1.2.20.md`](../releases/v1.2.20.md).

v1.2.15 об'єднав узгоджену взаємодію BDO/WWM карток, обмежений повтор тимчасового GitHub asset 404 і зняття лише `ReadOnly` з наявних WWM LocalData override-файлів під час Package install; їхній вміст не змінюється. У цьому циклі реальні файли гри не змінювалися. Owner waived external Architect review; acceptance не заявляється.

Stage 11B та WWM API-driven compatibility UX/recovery corrections залишаються **REVIEWED / ACCEPTED**. RC #39/#40/#41/#42 не прийняті Owner smoke і не повинні повторно використовуватися; RC #43 прийнятий Owner smoke та опублікований як v1.2.11.

Owner-approved PRIMARY roadmap `localization-hub-multigame` remains active. Stage 8B is **OPTIONAL / NOT STARTED**. v1.2.20 is **RELEASED / PUBLIC VERIFIED** as an Owner-authorized notification test; external Architect acceptance is not claimed. **Next implementation authorization: OWNER DECISION REQUIRED.** No Stage 11C or additional feature is inferred.

## Architecture Summary

The v1.2.11 RC #39–#42 corrections preserved legacy journal outcomes, exact root/cycle ownership, Steam snapshot staleness semantics and safe reinstall after external target changes. These corrections were reviewed and validated; their RCs were not reused. RC #43 from source `758755058e3a197a59ec41452fb6a95938b54d7a` passed Owner WWM install smoke and was published as stable v1.2.11 (Release ID `416033195`). The accepted product contract and release evidence are recorded in the release archive and 2026-10 journal.

- `Program.cs` є manual composition root без DI-контейнера.
- `MainForm` координує UI та application services; довгі HTTP/file operations виконуються async.
- `Api/BdoUaApiClient` володіє агрегованим `/releases` та per-mode `/releases/latest/{slug}`; останній є обов'язковою свіжою перевіркою перед install/update.
- `Services/LocalizationInstaller` відповідає за download, retry, checksum та timeout локалізації.
- `Storage` відповідає за config, installation state, original snapshot і restore points.
- `Update` містить GitHub Release discovery, schema-2 bundle validation, staging, replacement helper, rollback та startup maintenance.
- WWM composition is isolated in `WwmGameSession`, `Winds4UaApiClient`, `WwmSteamDetector`, `WwmPackageService`, `WwmInstallService`, `WwmReleaseFeedPoller` and versioned `WwmStateStore`; BDO APIs and state schema remain separate.

Для підтверджено довіреного WWM state без активного journal зовнішня зміна target-файлів є станом повторного встановлення, а не startup recovery failure. Після свіжої API/package перевірки нова транзакція бере поточні exact bytes/existence/attributes обох targets як per-operation rollback baseline. Застарілий pre-Hub snapshot не переписується: Restore лишається окремо заблокованим, але не блокує Install. Невирішений journal і некоректні ownership/binding докази залишаються fail-closed.

## Tray / Background Contract

- Звичайне X ховає MainForm у Windows tray і не завершує процес; `Відкрити` та подвійний клік відновлюють те саме вікно; `Вихід` є фактичним завершенням.
- Нормальний і background запуск використовують single-instance activation; повторний запуск активує існуючий клієнт.
- Видимий API polling працює приблизно кожні 15 секунд, прихований — приблизно кожні 5 хвилин; локальний файл локалізації у background перевіряється дешевим metadata fingerprint.
- Localization and application version notifications are separate informational channels with RAM-only dedup. For BDO and WWM, tray localization notifications are keyed by game + installed mode + release identity and include the available release version; visible-window observations remain pending until tray display is allowed, and no automatic installation occurs. Application notifications remain pending while the window is visible. A notification is deduplicated after Windows confirms its shown/closed event; unconfirmed requests can retry after a bounded cooldown. Logs record eligibility/defer reasons, request, Windows shown/closed/clicked events, and request failures. Click-to-open is not a contract.
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
- [`docs/releases/v1.2.9.md`](../releases/v1.2.9.md) — current stable release archive
- [`history/2026-10.md`](history/2026-10.md) — current engineering journal

## v1.2.11 RC #41 — ручне підтвердження кореня та повтор recovery

Owner smoke RC #41 / run `38844562243` **FAILED / NOT ACCEPTED / DO NOT REUSE**: після успішного збереження вручну вибраного точного Steam-root MainForm зберігав startup `_wwmRecoveryBlocked`, через що `RefreshWwmStateAsync` не запускався і новий довірений detection не передавався на повторний `RecoverAsync`. Корекція виконує retry одразу після підтвердженого manual selection; recovery-блок очищується лише при успіху, а при новій помилці повідомлення оновлюється та блокування лишається. Сервісні trust rules не змінюються. Застосовано синтетичні/тимчасові fixtures; game targets не змінювалися. Зовнішній Architect review: **REVIEWED / ACCEPTED**, BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`; commit `731837eb21f45f20ab14a3d87c264bccfb1bb6b2`, exact-SHA CI #263 / run `38927511513` — **SUCCESS**, 1204/1204. RC #43 згодом пройшов Owner smoke та опублікований як v1.2.11; архів релізу містить фінальні факти.

## Current Task Handoff

- v1.2.3 release cycle is completed and archived.
- Offline/degraded release-feed mode is completed, reviewed and accepted; `offline-degraded-reliability` is archived.
- `localization-hub-multigame` remains the sole ACTIVE PRIMARY roadmap; Stages 8D.1, 8D.2, 9, 10, 11A and 11B plus WWM compatibility/legacy migration corrections are **REVIEWED / ACCEPTED**; Stage 8B is OPTIONAL / NOT STARTED. v1.2.15 (Release ID `416608010`, tag `v1.2.15`, source `4cc97d616577786725a3878af4a1da46546dbf0f`) is **RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER**. Next implementation authorization is **OWNER DECISION REQUIRED**; no Stage 11C.
- v1.2.5 release cycle completed; public Release verified and `NEXT.json` reset for the next cycle.
- v15.64/v15.65 — **REVIEWED / ACCEPTED; OWNER VISUAL SMOKE ACCEPTED**: для трьох і більше режимів локалізації додано компактний minimum window width і збережено його під час semantic rebuild; single-mode global status width збережено.
- v1.2.7 — **RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED**: public Release ID `391117230` опубліковано на tag `v1.2.7`, який вказує на exact approved RC SHA `b21118d1ec8e6d0342fb0e47544e911fcb875739`; canonical ZIP і внутрішні hashes повторно перевірено, Owner native smoke exact RC прийнято.
- v1.2.8 — **RELEASED / PUBLIC VERIFIED**: public Release ID `391585430` опубліковано на tag `v1.2.8`, який вказує на exact RC SHA `7cb740d55bc7baeb5aa9f91365114e68bb88eff9`; public asset повторно завантажено й перевірено. Owner live legacy update accepted.
- `localization-hub-multigame` — **ACTIVE / PRIMARY**: Stages 0–10, 11A and 11B, WWM compatibility UX and legacy migration corrections are reviewed/accepted; Stage 8B remains **OPTIONAL / NOT STARTED**. Stage 11B accepted implementation SHA is `0ee0d819eb6f3f6fb404eb283095944d955f2a7f`; exact-SHA CI run `38470073111` succeeded with 1180/1180 tests. Contract and current detection/mutation boundary are in [`docs/design/where-winds-meet-integration-contract.md`](../design/where-winds-meet-integration-contract.md).
- v1.2.10 lifecycle is **RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED**. v1.2.11 is **RELEASED / PUBLIC VERIFIED / PENDING EXTERNAL RELEASE REVIEW**; its archive is [`../releases/v1.2.11.md`](../releases/v1.2.11.md). Stage 8B remains OPTIONAL / NOT STARTED; no Stage 11C.
- Stage 8C final validation: CI #229 / run `36106040918` SUCCESS for `d726c939e0e6365ea2efd631e60cdf13528e836e`; Release build — 0 warnings / 0 errors; focused content-fit/MainForm/GameCatalog/session-host — 76 passed; full Release suite — 1008 passed / 0 failed / 0 skipped; release-note generator — 24 assertions and resolver — 13 passed; `git diff --check` passed.
- v1.2.9: Release ID `396873313`, published `2026-09-25T19:29:42Z`; annotated tag peels to exact RC source `5c2c3a71fac3f0b57405c3494db4327f2a02295d`. RC #37 / run `36166009527`, artifact ID `10878006746`; public asset ID `589080502`, ZIP 67,905,336 bytes / SHA-256 `c43ae054a3a349158607d5de69f923df216a8f4db9ade06c61545e178400b298`. Internal EXE SHA-256 `610329b4b59eef49092a9a721d88e4b4919aff914764c82cd2221d3584f9910d`, FileVersion `1.2.9.0`, ProductVersion `1.2.9`; Owner RC smoke accepted, public re-download and exact bundle verification passed. Release archive: [`docs/releases/v1.2.9.md`](../releases/v1.2.9.md).
- v1.2.9 lifecycle: **RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED**. Owner live self-update from public v1.2.8 to public v1.2.9: **PASS / ACCEPTED**. Final Architect release review: **ACCEPTED**, BLOCKER `0`, IMPORTANT `0`. `NEXT.json` remains canonical empty schema-v1 after archiving the released copy.
- v1.2.10: Release ID `409061408`, published `2026-10-10T16:23:04Z`; annotated tag peels to exact RC source `fc7026311f3d79562e7558311441d829283930c9`. RC #38 / run `38056345425`; canonical/legacy Actions artifact IDs `11671731160` / `11672101551`; public asset IDs `628420557` / `628420558`. ZIP sizes/SHA-256, byte-identical EXE payload, FileVersion `1.2.10.0`, ProductVersion `1.2.10`, public notes and downloads verified. Stage 10 real WWM E2E and final external Architect release review **ACCEPTED**. Archive: [`docs/releases/v1.2.10.md`](../releases/v1.2.10.md); `NEXT.json` reset to empty schema-v1.
- Stage 8D.1 readiness: per-mode `GET /api/public/v1/releases/latest/{slug}` support has endpoint-specific 200/304/404/503 outcomes and per-slug in-memory ETag caching. Stage 8D.2 uses it before every install/update while the aggregate endpoint remains the primary runtime feed; installer uses immutable `current.download_url`.

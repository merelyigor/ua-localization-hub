# Localization Hub / Multi-game Foundation

Plan ID: `localization-hub-multigame`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **Owner-authorized bounded BDO/WWM localization update tray notification parity only.**
Current phase: v1.2.16 — LOCALIZATION UPDATE NOTIFICATION PARITY
Next action: **external review of the committed implementation.** Stable v1.2.15 (Release ID `416608010`, source `4cc97d616577786725a3878af4a1da46546dbf0f`) is archived at [`../../releases/v1.2.15.md`](../../releases/v1.2.15.md). No release publication is authorized by this follow-up. Stage 8B remains OPTIONAL / NOT STARTED; no Stage 11C.
Dependencies: v1.2.7 — RELEASE REVIEWED / ACCEPTED

## Goal

Перетворити BDO-UA Client на один Windows launcher/hub для кількох незалежних ігор із локалізаціями. Поточна публічна назва застосунку — `Хаб Українізаторів BDO - WWM`. Production catalog містить Black Desert Online та Windows multi-layout Where Winds Meet із окремими game-specific API/install boundaries.

## Product targets

- `Black Desert Online` — localization project `BDO UA Translate`, [bdo-ua.com.ua](https://bdo-ua.com.ua/), **INTEGRATED / AVAILABLE**.
- `Where Winds Meet` — localization project `Winds4UA (W4U)`, [winds4ua.com.ua](https://winds4ua.com.ua/), **INTEGRATED / WINDOWS MULTI-LAYOUT MUTATION REVIEWED / ACCEPTED**.

Where Winds Meet is registered as a second runtime game with Owner-approved stable ID `where-winds-meet`. Detection separates source provenance from target layout: matching Steam metadata proves Steam; a matching Epic `.item` manifest plus WWM product identity proves Epic; otherwise a valid root is `UnknownWindows`. Steam/Epic select Package; an UnknownWindows root is mutation-supported only when exactly one known target layout is present. Official Launcher provenance is not automatically claimed. Modes are dynamic and installable only when fresh API metadata marks the selected `slug + variant` available and supplies its matching package. Before install/update, fresh latest metadata resolves and validates the package; once a valid installable package is established, no compatibility-confirmation dialog is shown regardless of `data.current.game_tested`. Steam build ID is observational/staleness metadata and does not itself trigger a warning.

## Context

Current application already має explicit BDO boundary та game-scoped persistence, але application shell, selected-game lifecycle, feed cache та legacy technical identities ще розглядаються крізь BDO-specific history. Цей план описує залежності й межі майбутніх етапів, а не авторизує їх виконання.

## Scope

Application-global scope: self-update, tray/background, autostart, logging, application config, single-instance, game catalog та selected-game state. BDO and WWM use a narrow shared session-lifetime boundary; game-specific service/DTO/storage models remain separate.

Selected-game scope: identity, detection, game path, API/feed, localization modes, patch/version state, install/update, restore, backups, per-game cache, local monitor та scoped async lifetime. Одночасно активна лише одна game session.

Explicit compile-time catalog registers these production games:

- stable id `black-desert-online`, display name `Black Desert Online`;
- stable id `where-winds-meet`, display name `Where Winds Meet` (Steam AppID `3564740`).

Synthetic second-game ids дозволені лише у тестових fixtures. Не створювати production placeholder, plugin system або dynamic module loading.

## Contracts / decisions

### Selected game and session

Application config eventually persists selected game id. Config без id означає BDO; unknown/removed id безпечно fallback-иться до BDO з warning log, без destructive migration.

Selected-game composition має перейти з `Program`/`MainForm` у bounded runtime/session boundary. Exact class/interface names залишаються відкритими до execution-time source review; один спільний interface допустимий, коли з'являться реальні modules, але interface-per-service не створюється спекулятивно.

Switching:

1. block switching during localization file mutation;
2. cancel/stop old session work, poller and monitor, detach handlers and dispose the session;
3. persist selection, create/load the new session, detect/load/render it and start its poller/monitor;
4. enforce cancellation/session-generation guards so old async results never update the new game UI.

### Isolation

Existing game-scoped config/state/backups remain isolated. Before multiple live games, current application-global `cache/release-feed.json` must become game-scoped. Game A must never read or display Game B path, installed state, backups, feed cache, mode, patch, notifications or monitor fingerprint. Logs, updater state and download temp remain global only where they are genuinely application-global.

### API boundary

BDO API DTOs and `/releases` contract remain BDO implementation detail. WWM uses dedicated `Winds4UaApiClient` and DTOs for `/api/public/v1/releases/latest`; modes are resolved dynamically by `slug + variant`. Do not invent universal DTOs or a feed framework. Restore is exact pre-Hub state, not stock; game-build compatibility is not asserted.

### Visible rebrand

Dedicated rebrand work eventually updates window title, header/subtitle, tray text/tooltips, uninstall/help text, README/docs, user-facing update/release wording, product metadata and screenshots where needed. Do not blanket-rename `BdoClient` namespaces/classes or historical docs; BDO-specific names remain where accurate.

### Technical identity compatibility

The public executable/package identity migration is completed as a bridge-compatible change: `BDO-WWM-UAClient.exe` and `BDO-WWM-UAClient-vX.Y.Z-win-x64.zip` are canonical; `BDO-UA-Client.exe` and its versioned ZIP remain explicit legacy compatibility identities. User-Agent `BDO-UA-Client`, autostart value, `%LocalAppData%\\BDO-UA-Client`, single-instance/log/update workspace identities and repository fallback remain intentionally stable.

Do not rename/migrate the LocalAppData root in the initial hub roadmap. Existing `games/<stable-game-id>/` logical isolation remains the basis. Any root migration requires a separate high-risk decision and pre-commit review.

### Repository and package rename

The repository is canonical at `merelyigor/ua-localization-hub`; `merelyigor/bdo-ua-client` remains a legacy redirected compatibility slug and must never be reused. The repository bridge retained legacy fallback. Public EXE/package identity migration now publishes canonical and legacy four-file bundles from the same binary; v1.2.9 clients can keep selecting the exact legacy bundle while new clients prefer canonical.

Autostart and local-data identity migration are out of scope and remain legacy-stable. No automatic installed-file path migration is introduced by the public artifact rename; the current process path remains the updater replacement target.

### Second-game onboarding gate

Stage 10 implements the Owner-approved stable ID `where-winds-meet`, Steam-first detection, exact pre-Hub restore semantics, and the API-driven confirmation policy recorded below. Official launcher remains deferred. Current API availability controls mode actions; unavailable or unresolvable modes are hidden. Exact-current installation shows `✓ Встановлено` without an Update action; a changed package for that same mode alone offers `Оновити`; another installable mode offers `Встановити`. The restore button is labeled `Відновити оригінал`, but restores the trusted initial pre-Hub baseline, not proven stock Steam bytes; invalid/missing baseline disables it. `english-items` and `english-terms` stay unavailable until matching current artifacts are provided. The historical v2.9 content observation is not a current blocker.

### WWM API-driven compatibility UX follow-up

Historical v1.2.11 behavior used `data.current.game_tested` to decide whether to show a neutral confirmation. That policy has since been superseded: current WWM install/update flow does not show a compatibility-confirmation dialog for any value of this field. Fresh API/package validation remains mandatory, and invalid or unavailable package metadata still blocks mutation. The Steam build ID remains informational and a snapshot-staleness guard only; neither its presence nor a `game_version` comparison creates a compatibility warning. This applies equally to proven Steam, Epic and UnknownWindows roots.

## Roadmap

### Stage 0 — Release prerequisite / activation

Close v1.2.7 and obtain external release acceptance. Owner explicitly activates this plan; move backlog → active with registry update in the same commit. No product code.

### Stage 1 — Hub shell + selected-game model

Add the visible hub shell foundations, compile-time `GameCatalog` with BDO only, selector and persisted selection/fallback while preserving BDO behavior. No fake WWM network or filesystem behavior. Owner visual smoke required.

### Stage 2 — Game runtime/session architecture

Separate application-global services from selected-game composition/lifetime in `Program`/`MainForm`; make BDO the first concrete registered module/session without speculative API generalization. High-risk architecture change: external pre-commit review.

### Stage 3 — Safe game switching lifecycle

Implement controlled teardown/setup, mutation blocking, cancellation and stale-result protection; reset poller/monitor/notifications and test switching with a synthetic second-game fixture. Depends on Stage 2.

Current state: **REVIEWED / ACCEPTED**. The concrete `SelectedGameSessionHost` owns one active session, switching is blocked during startup/mutation/closing, old poller and tracked UI work are drained before commit, and generation/cancellation guards protect the new session from stale results. The corrective coverage proves persisted known-game startup activation, deterministic switch completion, synthetic A→B→A restoration, stale old-session feed rejection and selector blocking during mutation.

### Stage 4 — Complete per-game runtime data isolation

Move feed cache and any remaining game-specific global state under game scope while preserving global logs, updater and temp data. Prove no cross-game data bleed. Depends on the session boundary.

Current state: **REVIEWED / ACCEPTED**. Release-feed cache ownership is now under `GamePersistencePaths`; the historical global BDO cache is imported best-effort only into the canonical BDO scope and retained as legacy residue.

### Stage 5 — Full visible rebrand

Make `Хаб українізаторів` the application-facing identity and update generic UI, tray, help, docs, README and product metadata while retaining selected-game BDO wording. Owner visual smoke required.

Current state: **REVIEWED / ACCEPTED**. External Architect review and Owner visual smoke are complete.

### Stage 6 — Technical application identity centralization / bridge preparation

Centralize legacy/new repository, executable/package, User-Agent and autostart identities; preserve updater compatibility and avoid persistence-root migration. Self-update compatibility change: external pre-commit review.

Current state: **REVIEWED / ACCEPTED**. External pre-commit Architect review completed with BLOCKER 0 and IMPORTANT 0. Active technical compatibility identities are centralized without changing physical executable, package, autostart or LocalAppData names. GitHub release discovery now tries the canonical repository first and uses the legacy compatibility slug only after a canonical HTTP 404.

### Stage 7 — Repository rename bridge release + rename

Ship the bridge-capable release, perform the explicit Owner-authorized rename to `merelyigor/ua-localization-hub`, then normalize active repository references. Verify old/new URLs, redirects, history, origin, Actions, releases, API and old/new client update paths.

Current state: **REVIEWED / ACCEPTED**. External Architect final review accepted the post-rename canonical source/docs normalization with BLOCKER `0` and IMPORTANT `0`. Repository `merelyigor/bdo-ua-client` was renamed to canonical `merelyigor/ua-localization-hub` without changing repository ID `1332444174`; v1.2.8 tag, release, asset, history and Actions were preserved. Active source/docs now use the canonical slug while the old slug remains the tested compatibility fallback.

### Stage 8A — Explicit supported-project UI

Make the application visibly communicate both target projects without pretending that Where Winds Meet is runnable. Show Black Desert Online — BDO UA Translate as available and Where Winds Meet — Winds4UA (W4U) as integration planned. Do not register WWM in the runtime catalog or invent an API/install contract.

Current state: **REVIEWED / ACCEPTED**. External Architect review accepted the implementation with BLOCKER `0` and IMPORTANT `0`; Owner visual smoke accepted the target-project presentation and layout. The main shell names both target games and projects, while the production runtime catalog remains BDO-only and the active-game selector remains a disabled one-item selector.

### Stage 8C — Visible brand wording + adaptive window height

Apply the Owner-approved visible identity `Хаб Українізаторів BDO - WWM` and subtitle `Українські локалізації для ігор BDO - WWM`. Make the main window fit content bidirectionally: shrink when content becomes shorter, grow when it becomes taller, and clamp to the screen working area while retaining vertical scrolling. Preserve the target-project rows and BDO-only runtime behavior.

Current state: **REVIEWED / ACCEPTED**. External Architect review: BLOCKER `0`, IMPORTANT `0`; Owner native/visual smoke accepted. Acceptance includes the corrective sizing for empty/loading/failure mode states so the localization section and form shrink after tall card layouts. No WWM runtime contract or behavior was added.

### Stage 8B — Optional physical EXE/package/autostart migration

Only if still desired: support legacy and new artifact identities, migrate autostart add-new/verify/remove-old, and test updates from a legacy EXE. Do not automatically rename LocalAppData.

Current state: **OPTIONAL / NOT STARTED**.

### Stage 8D — BDO latest-release API integration

Adopt the public per-mode latest-release API without replacing the aggregate `/releases` feed. The aggregate remains the primary application feed for startup, mode listing, progress, detection hints, restore metadata, polling and offline cache. The per-mode endpoint supplies fresh metadata for one requested mode and, after Stage 8D.2, gates each install/update transaction.

#### Stage 8D.1 — Client contract/readiness support

**REVIEWED / ACCEPTED.** External Architect review accepted (BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`). The canonical website contract in `merelyigor/bdo_ua_translate/docs/PUBLIC_RELEASES_API.md` independently matched the implementation. Added bounded client support for `GET /api/public/v1/releases/latest/{slug}`, minimal response DTOs reusing `CurrentRelease`, explicit endpoint outcomes, per-slug process/session-local ETag metadata, and deterministic contract tests. Handles `200` including valid `current: null`, conditional `304`, `404 unknown_mode` with documented `allowed` values, `503 official_patch_unconfirmed` with optional seconds-based `Retry-After`, and transport/API failures. No persistence/schema migration and no runtime install activation.

Do not create a release for readiness-only work.

#### Stage 8D.2 — Production activation

**REVIEWED / ACCEPTED.** External Architect review accepted Stage 8D.2 and its corrective iterations (BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`). CI #239 / run `37259027517` succeeded for exact SHA `23226b50d10a6c5a0c72e541f719a9bfbfccda28`. Owner final native smoke: **ACCEPTED**.

Acceptance: every Install/Update performs latest freshness validation before mutation; valid HTTP 200 metadata is fresh and authoritative, while 304 uses cached latest metadata and still compares local `public_id`. Stale aggregate fallback is prohibited. `game_test` confirmation policy and the custom neutral dark/gold dialog are accepted. While confirmation is open, the target card shows `Очікує підтвердження`; explicit install proceeds to the normal operation state, and decline returns to factual state without download or mutation. Owner smoke also observed a real conditional latest request resolving as Not Modified with cached metadata retained. The aggregate feed and poller remain unchanged.

### Stage 9 — Where Winds Meet integration contract analysis

**REVIEWED / ACCEPTED.** External Architect review: BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`. Read-only evidence and final Owner decisions are recorded in [Where Winds Meet integration contract](../../design/where-winds-meet-integration-contract.md): approved stable ID `where-winds-meet`, Steam-first AppID `3564740`, current production `/api/public/v1/releases/latest` contract, verified v2.6.2 two-file package, exact pre-Hub restore semantics, Official launcher deferred, and no hard compatibility claim. Current API exposes only `ukrainian/default` as available; `english-items` and `english-terms` are unavailable and have no current package. Their older v2.9 identical-content observation is not a Stage 10 blocker and does not imply mode interchangeability. No production WWM registration or game-file operation occurred.

### Stage 10 — Real Where Winds Meet Steam-first integration

**REVIEWED / ACCEPTED.** Stage 10 WWM implementation and its corrective iterations passed external review and the authorized real install/restore E2E. The reviewed transaction restores the exact pre-Hub files/attributes; WWM remains Steam-first. This implementation is included in stable v1.2.10. The public executable identity migration shipped in the same release without changing localization runtime behavior.

Owner UX addendum: unavailable modes are omitted, saved unavailable selection falls back to the first installable card, exact-current packages show `✓ Встановлено` without an action, and only a different package for the installed mode shows `Доступне оновлення` / `Оновити`. The restore button says `Відновити оригінал` while its validated data contract remains exact initial pre-Hub baseline—not asserted Steam stock. The action requires valid managed state, snapshot cycle ownership/integrity, unchanged targets and build-safety checks.

Stage 10 real install/restore E2E and external review are accepted and included in v1.2.10. The separate public executable identity migration is complete and released. Stage 11A detection/presentation is reviewed and accepted; Stage 11B supersedes its detection-only mutation boundary below.

### Stage 11A — WWM Windows platform/layout-aware detection

**REVIEWED / ACCEPTED.** Detection recognizes the common WWM executable marker plus either `Package/HD/oversea/locale/` or `LocalData/Patch/HD/oversea/locale/`. Steam provenance requires the matching Steam library manifest for AppID `3564740`; a saved/manual root and filesystem layout alone are not platform evidence. Manual folder selection searches recursively only inside the selected tree, tolerates inaccessible directories, skips reparse points, is bounded, and reports multiple roots as ambiguous. UI distinguishes proven Steam from unknown Windows provenance and uses a generic not-found label. At Stage 11A, non-Steam roots were read-only; Stage 11B supersedes only that mutation boundary. No Android detection/modeling.

### Stage 11B — WWM Windows multi-layout install/update/restore

**REVIEWED / ACCEPTED. HIGH-RISK FILE MUTATION.** Accepted implementation: `0ee0d819eb6f3f6fb404eb283095944d955f2a7f`. Exact-SHA CI run `38470073111` succeeded; validation completed with **1180 passed / 0 failed / 0 skipped**. Detection orchestration preserves Steam AppID/manifest association and adds Epic Launcher `.item` metadata discovery with defensive product identity/root validation. Official Launcher automatic provenance remains deferred; manual valid roots are `UnknownWindows`. Source is never inferred from folder name or layout.

Package and LocalDataPatch each map the same two logical Winds4UA files. Proven Steam/Epic select Package; proven Official would select LocalDataPatch only if reliable metadata exists; UnknownWindows selects a target only when exactly one layout exists. UnknownWindows with both layouts fails closed. Package archive validation/staging remains unchanged and the two entries are explicitly remapped to the selected target layout rather than blindly extracted.

State, snapshot and journal bind to canonical installation root, selected layout, exact target paths and a deterministic non-managed executable metadata fingerprint; source provenance is established from live detector evidence, not persisted as authorization. The fingerprint is an installation integrity/staleness guard, not a game compatibility version. Historical Steam build IDs are not root identity. A stale linked pre-Hub snapshot blocks Restore Original but does not alone block normal managed install/update; incomplete journal rollback remains blocked across a Steam build change, while committed-journal cleanup is preserved. Existing v1.2.10 Steam Package state remains readable as legacy schema and is not reinterpreted as LocalDataPatch. Synthetic/temp tests cover Steam Package, Epic Package, Official-like Standard/Lite LocalDataPatch, UnknownWindows single/dual layouts, rollback and recovery. No real Steam, Epic or Official Launcher game files were mutated during Stage 11B development; non-Steam mutation validation used synthetic/temp roots.

External Architect implementation review: **APPROVED**, BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`. The separate bounded API-driven compatibility UX follow-up is recorded above and is not a new roadmap stage.

## Acceptance criteria

- Public application identity is `Хаб Українізаторів BDO - WWM`, while BDO is one registered runtime game rather than the application identity.
- Selected-game state persists safely with BDO fallback and bounded session lifetime.
- Switching cannot leak stale async results or cross-game path/state/backups/cache/modes/patch/notifications/monitor data.
- Application-global self-update, tray and autostart remain stable.
- Game mutation retains backup, restore and rollback guarantees.
- Repository rename, if Owner proceeds, preserves redirects, releases and old-client update compatibility.
- Public EXE/package identity is canonicalized with bridge-compatible legacy bundles; LocalAppData and autostart remain unchanged.
- BDO ↔ WWM ↔ BDO lifecycle uses separate sessions and game-scoped state, with WWM Windows layout-aware detection and two-file transaction recovery.

## Non-goals

Не входять: Android detection/mutation; Official Launcher source attribution without launcher-native evidence; WWM compatibility claims unsupported by API; plugins/reflection/MEF/DLL loading; DI container; database; dynamic plugin registry; broad MVVM rewrite; invented universal API/schema; blanket namespace rename; automatic LocalAppData migration; unrelated refactoring; public release.

## Risks / dependencies

High-risk/pre-commit review is required for the runtime architecture boundary, updater/repository identity bridge, physical EXE/autostart migration, any persistence-root migration, and materially different WWM file mutation. Selector shell, visible rebrand and scoped cache isolation after the boundary may use normal Combined mode, with Owner visual smoke for UI. Repository rename is always an Owner operational gate.

Stage 9 analysis, Stage 10 implementation/real E2E, Stage 11A, Stage 11B, compatibility UX, and RC #39/#40/#41 corrections remain **REVIEWED / ACCEPTED**. RC #39 / #40 / #41 remain **FAILED OWNER SMOKE / NOT ACCEPTED / DO NOT REUSE**. RC #40 correction commit `1935678a992774a96a3e0ef9f6e531915fcf64f0` passed exact-SHA CI #261 / run `38843181893`, 1202/1202; historical Steam builds are not root identity, stale snapshots block Restore, and incomplete journals cannot rollback into a newer build. RC #41 correction is also REVIEWED / ACCEPTED at `731837eb21f45f20ab14a3d87c264bccfb1bb6b2`, exact-SHA CI #263 / run `38927511513` SUCCESS (1204/1204). Its manual-root recovery retry clears the recovery latch only after successful `RecoverAsync`; failed retry remains blocked and reports the new error.

## Current progress

v1.2.11 RC correction v4 preserves the exact pre-migration committed outcome of an active legacy journal (state SHA plus target manifests): committed installs remain cleanup-only after binding, and committed restores retain absent-state semantics. That correction was accepted externally and passed exact-SHA CI; RC #39 nevertheless remains unaccepted.

### RC #40 Owner-smoke failure and bounded correction

Owner smoke of RC #40 / run `38825506167` **FAILED / NOT ACCEPTED / DO NOT REUSE**. The smoke encountered current Steam build `25906190` with a valid v1.2.10 pre-Hub snapshot recorded on build `25281753`; legacy migration incorrectly treated historical build mismatch as invalid binding and blocked managed state/Restore.

The correction separates root ownership from Restore staleness. Migration relies on unique manifest-proven Steam root, Package layout, cycle linkage, backup/hash integrity, exact managed-target or journal-known state, and no conflicting schema-v2 binding—not equality of historical build IDs. The recorded IDs are preserved. A stale linked snapshot disables Restore Original but does not alone block normal managed install/update; an incomplete journal with changed Steam build remains blocked before rollback, while a fully committed journal keeps cleanup-only handling. Managed-target mismatch, ambiguous/cross-root detection, corrupt snapshots and orphan snapshots remain fail-closed. This work used synthetic/temp fixtures and metadata only; real game files were not changed. External Architect review: **REVIEWED / ACCEPTED**, BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`; correction commit `1935678a992774a96a3e0ef9f6e531915fcf64f0`, parent `a421fa3ab35ab1bd7cc1fe0380d015be504428b7`; exact-SHA CI #261 / run `38843181893` — SUCCESS, 1202 passed / 0 failed / 0 skipped. RC #39/#40 remain failed Owner smoke and must not be reused.

### RC #41 — manual-root recovery retry correction

Owner smoke RC #41 / run `38844562243` **FAILED / NOT ACCEPTED / DO NOT REUSE**. MainForm retained the startup recovery-block latch after the exact Steam root was manually selected and persisted, so no recovery retry used the newly trusted detection. The accepted correction retries `RecoverAsync` immediately after successful path persistence, clears the block only on recovery success, and retains/updates the blocked presentation on failure. Service trust policy is unchanged; synthetic/temp fixtures only, no real game mutation. External Architect review: **REVIEWED / ACCEPTED**, BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`; commit `731837eb21f45f20ab14a3d87c264bccfb1bb6b2`, parent `cc7574e7a6c1827416e8136107a9b1f0962229b8`; exact-SHA CI #263 / run `38927511513` — SUCCESS, 1204 passed / 0 failed / 0 skipped. RC #43 subsequently passed Owner WWM install smoke and was published as v1.2.11; RC #39/#40/#41 artifacts were not reused.

Roadmap approved by Owner and activated after v1.2.7 external release acceptance. Stage 0 is complete. Stages 1–8A, 8C, 8D.1, 8D.2, 9, 10, 11A and 11B, compatibility UX, and RC #39/#40/#41/#42 corrections are **REVIEWED / ACCEPTED**. Stage 8B remains **OPTIONAL / NOT STARTED**. RC #39/#40/#41/#42 failed Owner smoke and were not reused. v1.2.11 remains RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.12 and v1.2.13 remain archived; v1.2.14 (Release ID `416460308`) is the current stable release. The Owner has authorized the bounded v1.2.15 release preparation described above and waived external Architect review for this release. No Stage 11C.

### v1.2.10 release cycle

**v1.2.10 — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED.** RC #38 / run `38056345425` succeeded from exact source SHA `fc7026311f3d79562e7558311441d829283930c9`. Annotated tag `v1.2.10` peels to that SHA. Public Release ID `409061408` was published at `2026-10-10T16:23:04Z`; it is neither draft nor prerelease. Canonical Actions artifact ID `11671731160` and public asset ID `628420557` (`BDO-WWM-UAClient-v1.2.10-win-x64.zip`, `67,976,196` bytes, SHA-256 `dbb03c6af7ee495eb8df863c7a9d5e2f6f158eea2958a375c0be3d1bb98775f1`); legacy Actions artifact ID `11672101551` and public asset ID `628420558` (`BDO-UA-Client-v1.2.10-win-x64.zip`, `67,976,176` bytes, SHA-256 `15d8e15a72bfdfe5fc9490faf8561fefd8fd0c2066c7c7df25becf0e499b1e4d`). Both bundles contain byte-identical EXE payloads (SHA-256 `de05993111ec6257a5e5fbecc1a2438af2060306f05972895f7256ec3b589490`), FileVersion `1.2.10.0`, ProductVersion `1.2.10`. Exact-SHA CI, RC validation/native smoke, public assets/body/download verification, Stage 10 real WWM E2E and final external Architect release review all passed. Evidence is archived in [`docs/releases/v1.2.10.md`](../../releases/v1.2.10.md). `NEXT.json` has been reset to empty schema-v1. No next implementation step is authorized; Owner decision required.

### v1.2.9 release cycle

**v1.2.9 — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED.** Release Candidate #37 / run `36166009527` succeeded from exact source SHA `5c2c3a71fac3f0b57405c3494db4327f2a02295d`; Owner native smoke was accepted and Architect authorized publication. Stable Release ID `396873313` was published at `2026-09-25T19:29:42Z` using annotated tag `v1.2.9`, which peels to that exact RC source. Actions artifact ID `10878006746`; public asset ID `589080502`, name `BDO-UA-Client-v1.2.9-win-x64.zip`, size `67,905,336` bytes, SHA-256 `c43ae054a3a349158607d5de69f923df216a8f4db9ade06c61545e178400b298`. Fresh public download and four-file bundle verification passed; manifest, EXE hash/metadata, SHA256SUMS and exact RC release body were verified. Public latest/release discovery resolves to v1.2.9. Owner live self-update from public v1.2.8 to public v1.2.9: **PASS / ACCEPTED**. Final Architect release review: **ACCEPTED**, BLOCKER `0`, IMPORTANT `0`. Release evidence is archived in [`docs/releases/v1.2.9.md`](../../releases/v1.2.9.md). `NEXT.json` remains canonical empty schema-v1.

### v1.2.11 RC #42 — external target changes must remain reinstallable

RC #42 / run `38930894752` Owner smoke **FAILED / NOT ACCEPTED / DO NOT REUSE**. Read-only diagnosis found trusted schema-v2 root/layout/cycle state, an integrity-valid linked snapshot from historical Steam build `25281753`, no active journal, current build `25906190`, and both live targets differing from the previous installed hashes. The bug treated this expected external/game-update change as recovery failure and disabled the fresh API Install action.

The accepted correction commit `e87e2688e2c7fb144ced1adde84745abf3369684` (parent `aab84800fb49025f43e5ef9b4fdd2eb6c80088e7`) uses a distinct `NeedsReinstall` presentation only for structurally valid trusted state with no active journal; fresh API install remains available, while Restore can remain disabled for a stale snapshot. The transaction captures both exact current targets as its rollback baseline and journals them before replacement. The historical pre-Hub snapshot remains unchanged. Synthetic startup/install/new-session regression passed; real WWM files were not mutated during correction. External Architect: **REVIEWED / ACCEPTED**, BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`; exact-SHA CI #265 / run `38957407339` — SUCCESS, 1209/1209. RC #42 remains failed and must not be reused. RC #43 passed Owner install smoke and was published as v1.2.11; no RC artifacts from #39–#42 were reused.

### v1.2.11 — published release

**RELEASED / PUBLIC VERIFIED / PENDING EXTERNAL RELEASE REVIEW.** Release ID `416033195`, annotated tag `v1.2.11` peeling to `758755058e3a197a59ec41452fb6a95938b54d7a`, published `2026-10-17T20:09:52Z`. RC #43 / run `39061711136` passed and Owner accepted the real WWM end-to-end install smoke. Public canonical asset ID `651477020` (68,003,521 bytes, SHA-256 `81f14ca86d6149a5fb10fc91b67433019361bf7558bd626332bcd47c594e2ee8`); legacy asset ID `651477025` (68,003,496 bytes, SHA-256 `f814ec691459a706b3fc8c455c162e21c0c5869f91203954fe08e5b66e50e6ed`). Internal EXE SHA-256 `5a8cc86c8daac93692b952e5df1da8d91ab2d418710f120b339e94c8fc80b47e`, FileVersion `1.2.11.0`, ProductVersion `1.2.11`. Exact artifact and publication evidence: [`docs/releases/v1.2.11.md`](../../releases/v1.2.11.md).

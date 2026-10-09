# Localization Hub / Multi-game Foundation

Plan ID: `localization-hub-multigame`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **BOUNDED EXECUTABLE IDENTITY MIGRATION AUTHORIZED**
Current phase: public executable/release artifact identity migration — IMPLEMENTED / VALIDATED / PENDING EXTERNAL ARCHITECT REVIEW; Stage 10 — REVIEWED / ACCEPTED
Next action: external review of the identity migration; no Release Candidate is started by this task.
Dependencies: v1.2.7 — RELEASE REVIEWED / ACCEPTED

## Goal

Перетворити BDO-UA Client на один Windows launcher/hub для кількох незалежних ігор із локалізаціями. Поточна публічна назва застосунку — `Хаб Українізаторів BDO - WWM`. Production catalog містить Black Desert Online та Steam-first Where Winds Meet із окремими game-specific API/install boundaries.

## Product targets

- `Black Desert Online` — localization project `BDO UA Translate`, [bdo-ua.com.ua](https://bdo-ua.com.ua/), **INTEGRATED / AVAILABLE**.
- `Where Winds Meet` — localization project `Winds4UA (W4U)`, [winds4ua.com.ua](https://winds4ua.com.ua/), **INTEGRATED / STEAM AVAILABLE**; Official launcher deferred.

Where Winds Meet is registered as a second runtime game with Owner-approved stable ID `where-winds-meet`; initial support is Steam-only. Modes are dynamic and installable only when fresh API metadata marks the selected `slug + variant` available and supplies its matching package. Compatibility with a specific Steam build remains unconfirmed by API and requires neutral confirmation.

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

Stage 10 implements the Owner-approved stable ID `where-winds-meet`, Steam-first detection, exact pre-Hub restore semantics, and neutral confirmation because compatibility is not confirmed by API. Official launcher remains deferred. Current API availability controls mode actions; unavailable or unresolvable modes are hidden. Exact-current installation shows `✓ Встановлено` without an Update action; a changed package for that same mode alone offers `Оновити`; another installable mode offers `Встановити`. The restore button is labeled `Відновити оригінал`, but restores the trusted initial pre-Hub baseline, not proven stock Steam bytes; invalid/missing baseline disables it. `english-items` and `english-terms` stay unavailable until matching current artifacts are provided. The historical v2.9 content observation is not a current blocker.

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

**REVIEWED / ACCEPTED.** Stage 10 WWM implementation and its corrective iterations passed external review and the authorized real install/restore E2E. The reviewed transaction restores the exact pre-Hub files/attributes; WWM remains Steam-first and unreleased in stable v1.2.9. Public executable identity migration is a separate bounded pre-v1.2.10 release/update-contract task and does not change localization runtime behavior.

Owner UX addendum: unavailable modes are omitted, saved unavailable selection falls back to the first installable card, exact-current packages show `✓ Встановлено` without an action, and only a different package for the installed mode shows `Доступне оновлення` / `Оновити`. The restore button says `Відновити оригінал` while its validated data contract remains exact initial pre-Hub baseline—not asserted Steam stock. The action requires valid managed state, snapshot cycle ownership/integrity, unchanged targets and build-safety checks.

Stage 10 real install/restore E2E and external review are accepted. Stage 10 remains unreleased in v1.2.9; the separate public executable identity migration is not a new localization stage and does not authorize an RC or release.

## Acceptance criteria

- Public application identity is `Хаб Українізаторів BDO - WWM`, while BDO is one registered runtime game rather than the application identity.
- Selected-game state persists safely with BDO fallback and bounded session lifetime.
- Switching cannot leak stale async results or cross-game path/state/backups/cache/modes/patch/notifications/monitor data.
- Application-global self-update, tray and autostart remain stable.
- Game mutation retains backup, restore and rollback guarantees.
- Repository rename, if Owner proceeds, preserves redirects, releases and old-client update compatibility.
- Public EXE/package identity is canonicalized with bridge-compatible legacy bundles; LocalAppData and autostart remain unchanged.
- BDO ↔ WWM ↔ BDO lifecycle uses separate sessions and game-scoped state, with WWM Steam-only detection and two-file transaction recovery.

## Non-goals

Не входять: Official-launcher WWM support; WWM compatibility claims unsupported by API; plugins/reflection/MEF/DLL loading; DI container; database; dynamic plugin registry; broad MVVM rewrite; invented universal API/schema; blanket namespace rename; automatic LocalAppData migration; unrelated refactoring; public release.

## Risks / dependencies

High-risk/pre-commit review is required for the runtime architecture boundary, updater/repository identity bridge, physical EXE/autostart migration, any persistence-root migration, and materially different WWM file mutation. Selector shell, visible rebrand and scoped cache isolation after the boundary may use normal Combined mode, with Owner visual smoke for UI. Repository rename is always an Owner operational gate.

Stage 9 established the current production API, Steam-first detection, v2.6.2 package, pre-Hub restore, and unconfirmed-compatibility contracts. The bounded Stage 10 runtime has a real install/rollback blocker under correction. Current corrective patch remains uncommitted and requires external pre-commit Architect review. After review, the real failed transaction recovery and install/verify/restore E2E scenario still requires a separate explicit Owner authorization.

## Current progress

Roadmap approved by Owner and activated after v1.2.7 external release acceptance. Stage 0 is complete. Stages 1–8A, 8C, 8D.1, 8D.2, 9 and 10 are **REVIEWED / ACCEPTED**. Stage 8B remains **OPTIONAL / NOT STARTED**. Stage 9 confirms `where-winds-meet`, Steam-first, exact pre-Hub restore, Official launcher deferred, and no hard compatibility claim. Stage 10 implementation and real-game E2E are accepted but unreleased in stable v1.2.9. The executable identity migration is a separate pre-v1.2.10 release-contract task.

### v1.2.9 release cycle

**v1.2.9 — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED.** Release Candidate #37 / run `36166009527` succeeded from exact source SHA `5c2c3a71fac3f0b57405c3494db4327f2a02295d`; Owner native smoke was accepted and Architect authorized publication. Stable Release ID `396873313` was published at `2026-09-25T19:29:42Z` using annotated tag `v1.2.9`, which peels to that exact RC source. Actions artifact ID `10878006746`; public asset ID `589080502`, name `BDO-UA-Client-v1.2.9-win-x64.zip`, size `67,905,336` bytes, SHA-256 `c43ae054a3a349158607d5de69f923df216a8f4db9ade06c61545e178400b298`. Fresh public download and four-file bundle verification passed; manifest, EXE hash/metadata, SHA256SUMS and exact RC release body were verified. Public latest/release discovery resolves to v1.2.9. Owner live self-update from public v1.2.8 to public v1.2.9: **PASS / ACCEPTED**. Final Architect release review: **ACCEPTED**, BLOCKER `0`, IMPORTANT `0`. Release evidence is archived in [`docs/releases/v1.2.9.md`](../../releases/v1.2.9.md). `NEXT.json` remains canonical empty schema-v1.

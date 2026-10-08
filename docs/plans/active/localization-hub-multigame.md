# Localization Hub / Multi-game Foundation

Plan ID: `localization-hub-multigame`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **NO — STAGE 9 REVIEWED / ACCEPTED; STAGE 10 REQUIRES A SEPARATE BOUNDED TASK AUTHORIZATION**
Current phase: Stage 9 — REVIEWED / ACCEPTED
Next action: prepare a separate Stage 10 Steam-first implementation task using HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED.
Dependencies: v1.2.7 — RELEASE REVIEWED / ACCEPTED

## Goal

Перетворити BDO-UA Client на один Windows launcher/hub для кількох незалежних ігор із локалізаціями. Поточна публічна назва застосунку — `Хаб Українізаторів BDO - WWM`. Production catalog містить лише Black Desert Online; Where Winds Meet не реєструється без реального продуктового та API-контракту.

## Product targets

- `Black Desert Online` — localization project `BDO UA Translate`, [bdo-ua.com.ua](https://bdo-ua.com.ua/), **INTEGRATED / AVAILABLE**.
- `Where Winds Meet` — localization project `Winds4UA (W4U)`, [winds4ua.com.ua](https://winds4ua.com.ua/), **PLANNED / INTEGRATION PENDING**.

Where Winds Meet is a known product target, not a registered runtime game. Stage 9 is reviewed and accepted with current production API, Steam detection, package, and pre-Hub restore evidence. Owner-approved stable ID is `where-winds-meet`; initial future scope is Steam-first. Modes are dynamic and installable only when the API marks them available and supplies the selected package. Compatibility remains unconfirmed by API and must not be invented.

## Context

Current application already має explicit BDO boundary та game-scoped persistence, але application shell, selected-game lifecycle, feed cache та legacy technical identities ще розглядаються крізь BDO-specific history. Цей план описує залежності й межі майбутніх етапів, а не авторизує їх виконання.

## Scope

Application-global scope: self-update, tray/background, autostart, logging, application config, single-instance, game catalog та selected-game state.

Selected-game scope: identity, detection, game path, API/feed, localization modes, patch/version state, install/update, restore, backups, per-game cache, local monitor та scoped async lifetime. Одночасно активна лише одна game session.

Explicit compile-time catalog спочатку реєструє тільки:

- stable id: `black-desert-online`;
- display name: `Black Desert Online`.

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

BDO API DTOs and `/releases` contract remain BDO implementation detail. The observed Where Winds Meet / Winds4UA API is documented in Stage 9, but restore-original and game-build compatibility are unresolved; compare concrete contracts only after Stage 9 is resolved, and extract only proven common application-facing semantics. Do not invent universal DTOs or a feed framework.

### Visible rebrand

Dedicated rebrand work eventually updates window title, header/subtitle, tray text/tooltips, uninstall/help text, README/docs, user-facing update/release wording, product metadata and screenshots where needed. Do not blanket-rename `BdoClient` namespaces/classes or historical docs; BDO-specific names remain where accurate.

### Technical identity compatibility

Centralize compatibility metadata before physical renames for the legacy identities: repository `merelyigor/bdo-ua-client`, updater User-Agent/BDO-UA-Client identity, `BDO-UA-Client.exe`, `BDO-UA-Client-vX.Y.Z-win-x64.zip`, autostart value `BDO-UA-Client`, and `%LocalAppData%\\BDO-UA-Client`.

Do not rename/migrate the LocalAppData root in the initial hub roadmap. Existing `games/<stable-game-id>/` logical isolation remains the basis. Any root migration requires a separate high-risk decision and pre-commit review.

### Repository and package rename

The repository is now canonical at `merelyigor/ua-localization-hub`; `merelyigor/bdo-ua-client` remains a legacy redirected compatibility slug and must never be reused. The bridge release retained legacy fallback so old clients could update; after rename history/identity, local `origin`, Actions, releases, redirects, API behavior and update discovery were verified.

Visible rebrand and technical EXE/package/autostart rename are separate. First use the new display identity while retaining legacy physical names. Optional later rename requires updater support for both identities, add-new/verify/remove-old autostart migration, and an old-client update test. It must not be required for the first hub release.

### Second-game onboarding gate

For Stage 10, retain the Owner-approved stable ID `where-winds-meet`, Steam-first scope, exact pre-Hub restore semantics, and neutral confirmation because compatibility is not confirmed by API. Official launcher remains deferred. The current API marks `english-items` and `english-terms` unavailable and provides no installable artifacts for them; do not infer equivalence or expose them as installable. Stage 9's historical v2.9 content observation is not a current blocker. No architecture decision for WWM is made from assumptions.

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

### Stage 10 — Real Where Winds Meet implementation

**NOT STARTED.** The obsolete identical-mode blocker is removed: the authoritative current API marks the two English modes unavailable, so only API-available packages are install candidates. Stage 10 still requires a separate bounded implementation task and explicit authorization. Initial scope is Steam-first; Official launcher is deferred. Any multi-file install/backup/rollback design requires **HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED** before commit.

Current state: **NOT STARTED**.

## Acceptance criteria

- Public application identity is `Хаб Українізаторів BDO - WWM`, while BDO is one registered runtime game rather than the application identity.
- Selected-game state persists safely with BDO fallback and bounded session lifetime.
- Switching cannot leak stale async results or cross-game path/state/backups/cache/modes/patch/notifications/monitor data.
- Application-global self-update, tray and autostart remain stable.
- Game mutation retains backup, restore and rollback guarantees.
- Repository rename, if Owner proceeds, preserves redirects, releases and old-client update compatibility.
- Physical EXE rename, if performed, is bridge-compatible; LocalAppData is not destructively migrated without a separate decision.
- Where Winds Meet can be onboarded without a global rewrite once its actual contract is known, and the BDO ↔ WWM ↔ BDO lifecycle is tested.

## Non-goals

Не входять: Where Winds Meet до появи реального технічного контракту; plugins/reflection/MEF/DLL loading; DI container; database; dynamic registry; broad MVVM rewrite; invented universal API/schema; blanket namespace rename; automatic LocalAppData migration; unrelated refactoring; зміни v1.2.7 release scope.

## Risks / dependencies

High-risk/pre-commit review is required for the runtime architecture boundary, updater/repository identity bridge, physical EXE/autostart migration, any persistence-root migration, and materially different WWM file mutation. Selector shell, visible rebrand and scoped cache isolation after the boundary may use normal Combined mode, with Owner visual smoke for UI. Repository rename is always an Owner operational gate.

The roadmap depends on v1.2.7 release completion and explicit Owner activation. Stage 9 has established and externally accepted the current production API, Steam-first detection, v2.6.2 package, pre-Hub restore, and unconfirmed-compatibility contracts. Stage 10 remains a separate high-risk implementation task; do not infer stock restore or game-build compatibility.

## Current progress

Roadmap approved by Owner and activated after v1.2.7 external release acceptance. Stage 0 is complete and its dependency is satisfied. Stages 1–8A, 8C, 8D.1, 8D.2 and 9 are **REVIEWED / ACCEPTED**. Stage 8D.2 Owner final native smoke is accepted. Stage 8B remains **OPTIONAL / NOT STARTED**. Stage 9 confirms `where-winds-meet`, Steam-first, exact pre-Hub restore, Official launcher deferred, and no hard compatibility claim. The current API exposes only the Ukrainian mode as installable; the old v2.9 English-mode payload observation no longer blocks Stage 10. Stage 10 remains **NOT STARTED** and requires a separate bounded task authorization. Its multi-file install/backup/rollback requires **HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED**. Implementation authorization: **NO — awaiting a separate Stage 10 task prompt**.

### v1.2.9 release cycle

**v1.2.9 — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED.** Release Candidate #37 / run `36166009527` succeeded from exact source SHA `5c2c3a71fac3f0b57405c3494db4327f2a02295d`; Owner native smoke was accepted and Architect authorized publication. Stable Release ID `396873313` was published at `2026-09-25T19:29:42Z` using annotated tag `v1.2.9`, which peels to that exact RC source. Actions artifact ID `10878006746`; public asset ID `589080502`, name `BDO-UA-Client-v1.2.9-win-x64.zip`, size `67,905,336` bytes, SHA-256 `c43ae054a3a349158607d5de69f923df216a8f4db9ade06c61545e178400b298`. Fresh public download and four-file bundle verification passed; manifest, EXE hash/metadata, SHA256SUMS and exact RC release body were verified. Public latest/release discovery resolves to v1.2.9. Owner live self-update from public v1.2.8 to public v1.2.9: **PASS / ACCEPTED**. Final Architect release review: **ACCEPTED**, BLOCKER `0`, IMPORTANT `0`. Release evidence is archived in [`docs/releases/v1.2.9.md`](../../releases/v1.2.9.md). `NEXT.json` remains canonical empty schema-v1.

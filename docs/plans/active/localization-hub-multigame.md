# Localization Hub / Multi-game Foundation

Plan ID: `localization-hub-multigame`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **YES**
Current phase: Stage 8C — Visible brand wording + adaptive window height
Next action: external Architect review + Owner visual smoke
Dependencies: v1.2.7 — RELEASE REVIEWED / ACCEPTED

## Goal

Перетворити BDO-UA Client на один Windows launcher/hub для кількох незалежних ігор із локалізаціями. Поточна публічна назва застосунку — `Хаб Українізаторів BDO - WWM`. Production catalog містить лише Black Desert Online; Where Winds Meet не реєструється без реального продуктового та API-контракту.

## Product targets

- `Black Desert Online` — localization project `BDO UA Translate`, [bdo-ua.com.ua](https://bdo-ua.com.ua/), **INTEGRATED / AVAILABLE**.
- `Where Winds Meet` — localization project `Winds4UA (W4U)`, [winds4ua.com.ua](https://winds4ua.com.ua/), **PLANNED / INTEGRATION PENDING**.

Where Winds Meet is a known product target, not a registered runtime game. Its API, detection, install, patch, restore, backup and compatibility contract are not yet available and must not be invented.

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

BDO API DTOs and `/releases` contract remain BDO implementation detail until the real Where Winds Meet / Winds4UA contract exists. At that point compare the concrete APIs and extract only proven common application-facing semantics; do not invent universal DTOs or a feed framework.

### Visible rebrand

Dedicated rebrand work eventually updates window title, header/subtitle, tray text/tooltips, uninstall/help text, README/docs, user-facing update/release wording, product metadata and screenshots where needed. Do not blanket-rename `BdoClient` namespaces/classes or historical docs; BDO-specific names remain where accurate.

### Technical identity compatibility

Centralize compatibility metadata before physical renames for the legacy identities: repository `merelyigor/bdo-ua-client`, updater User-Agent/BDO-UA-Client identity, `BDO-UA-Client.exe`, `BDO-UA-Client-vX.Y.Z-win-x64.zip`, autostart value `BDO-UA-Client`, and `%LocalAppData%\\BDO-UA-Client`.

Do not rename/migrate the LocalAppData root in the initial hub roadmap. Existing `games/<stable-game-id>/` logical isolation remains the basis. Any root migration requires a separate high-risk decision and pre-commit review.

### Repository and package rename

The repository is now canonical at `merelyigor/ua-localization-hub`; `merelyigor/bdo-ua-client` remains a legacy redirected compatibility slug and must never be reused. The bridge release retained legacy fallback so old clients could update; after rename history/identity, local `origin`, Actions, releases, redirects, API behavior and update discovery were verified.

Visible rebrand and technical EXE/package/autostart rename are separate. First use the new display identity while retaining legacy physical names. Optional later rename requires updater support for both identities, add-new/verify/remove-old autostart migration, and an old-client update test. It must not be required for the first hub release.

### Second-game onboarding gate

Before Where Winds Meet implementation, Owner must supply actual stable game id, launchers and Steam App ID if applicable, registry/detection facts, validation markers, target files, patch/version detection, API endpoints and JSON schema, modes/releases, public ids/version/hash/size/download metadata, compatibility semantics, restore-original strategy, backup/rollback requirements and selector assets. No architecture decision for WWM is made from assumptions.

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

Current state: **IMPLEMENTED / VALIDATED / PENDING EXTERNAL ARCHITECT REVIEW / OWNER VISUAL SMOKE PENDING**. This stage changes visible branding and bounded content-height fitting only; no WWM runtime contract or behavior was added.

### Stage 8B — Optional physical EXE/package/autostart migration

Only if still desired: support legacy and new artifact identities, migrate autostart add-new/verify/remove-old, and test updates from a legacy EXE. Do not automatically rename LocalAppData.

Current state: **OPTIONAL / NOT STARTED**.

### Stage 9 — Where Winds Meet integration contract analysis

WAITING ON TECHNICAL INTEGRATION DATA. Perform read-only analysis of the actual Where Winds Meet / Winds4UA (W4U) detection, API, install, patch, restore and backup contract. No assumptions or implementation.

### Stage 10 — Real Where Winds Meet implementation

Register and implement Where Winds Meet / Winds4UA (W4U) through the proven runtime boundary only after Stage 9 supplies its real contract, then validate selector behavior, BDO ↔ WWM ↔ BDO lifecycle, cross-game isolation and game-file rollback safety.

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

The roadmap depends on v1.2.7 release completion and explicit Owner activation. Stage 9 is blocked on real Where Winds Meet / Winds4UA technical integration data.

## Current progress

Roadmap approved by Owner and activated after v1.2.7 external release acceptance. Stage 0 is complete and its dependency is satisfied. Stage 1 is **REVIEWED / ACCEPTED** after external Architect review and Owner visual smoke. Stage 2 is **REVIEWED / ACCEPTED** after external pre-commit Architect review, commit and CI success. Stage 3 is **REVIEWED / ACCEPTED**; Stage 4 is **REVIEWED / ACCEPTED**; Stage 5 is **REVIEWED / ACCEPTED** after external Architect review and Owner visual smoke. Stage 6 is **REVIEWED / ACCEPTED** after external pre-commit Architect review. Stage 7 is **REVIEWED / ACCEPTED** after final external Architect review of the post-rename normalization. Stage 8A is **REVIEWED / ACCEPTED** after external Architect review and Owner visual smoke. Stage 8C is **IMPLEMENTED / VALIDATED / PENDING EXTERNAL ARCHITECT REVIEW / OWNER VISUAL SMOKE PENDING**. Stage 8B is **OPTIONAL / NOT STARTED**; Stage 9 is **WAITING ON TECHNICAL INTEGRATION DATA** for Where Winds Meet / Winds4UA (W4U); Stage 10 is **NOT STARTED**.

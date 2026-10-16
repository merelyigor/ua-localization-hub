# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** RC #41 manual-root recovery retry — REVIEWED / ACCEPTED
**Next:** Create a completely new v1.2.11 Release Candidate; RC #39/#40/#41 remain FAILED / NOT ACCEPTED / DO NOT REUSE
**Implementation authorization:** No new implementation; RC preparation only. Final tag/publication remains an Owner gate.

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | RC #41 recovery correction — REVIEWED / ACCEPTED | Prepare a new v1.2.11 RC; do not reuse RC #39/#40/#41 |

Release cycles: `v1.2.9` and `v1.2.10` — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.10 exact RC assets, public tag, release body and downloads were verified.

Stage 9/10/11A/11B and prior accepted compatibility/migration corrections remain REVIEWED / ACCEPTED. RC #39 / #40 / #41 are FAILED OWNER SMOKE / NOT ACCEPTED / DO NOT REUSE. RC #41 / run `38844562243` failed because MainForm did not retry recovery after successful manual root confirmation. The bounded UI correction keeps the service trust rules unchanged, retries with the newly persisted detection, and clears the block only on successful recovery; failure preserves the block and updates its message. Correction commit `731837eb21f45f20ab14a3d87c264bccfb1bb6b2` is REVIEWED / ACCEPTED; exact-SHA CI #263 / run `38927511513` succeeded (1204/1204). Next action: prepare a completely new v1.2.11 RC. Stage 8B remains OPTIONAL / NOT STARTED; no Stage 11C or final publication authorization.

## Backlog

| Order | ID | Plan | Depends on |
|---|---|---|---|
| — | — | — | — |

## Archive

| ID | Plan | Coverage |
|---|---|---|
| `initial-implementation` | [archive/initial-implementation-plan.md](archive/initial-implementation-plan.md) | Stage 1–12.2 |
| `client-self-update` | [archive/client-self-update.md](archive/client-self-update.md) | v13.1.2–v14.0.1; ZIP self-update E2E completed |
| `client-ui-redesign` | [archive/client-ui-redesign.md](archive/client-ui-redesign.md) | Completed native WinForms launcher redesign through Stage 4; stable v1.1.2 validation |
| `background-tray-notifications` | [archive/background-tray-notifications.md](archive/background-tray-notifications.md) | Tray/background T1–T6 completed, reviewed, accepted and released in stable v1.2.0 |
| `code-quality-ux-improvements` | [archive/code-quality-ux-improvements.md](archive/code-quality-ux-improvements.md) | Roadmap completed, reviewed, accepted and released in stable v1.2.1 |
| `release-experience-polish` | [archive/release-experience-polish.md](archive/release-experience-polish.md) | R1–R3 completed, reviewed, accepted and released in stable v1.2.2 |
| `offline-degraded-reliability` | [archive/offline-degraded-reliability.md](archive/offline-degraded-reliability.md) | Offline/degraded release-feed roadmap completed, reviewed, accepted; v15.46 released in stable v1.2.3 |
| `game-boundary-refactoring` | [archive/game-boundary-refactoring.md](archive/game-boundary-refactoring.md) | Stage 1–2 completed, reviewed, accepted; roadmap completed and archived; stable release remains v1.2.5 |

## Rules / lifecycle

**Lifecycle transitions:**
```
BACKLOG → (explicit owner decision) → ACTIVE → (completed/superseded) → ARCHIVE
```

**Key rules:**
- `docs/plans/README.md` = canonical plans registry
- Implementation plans live only in `active/`, `backlog/`, `archive/`
- No canonical `/plan.md` in repository root
- ACTIVE plans are executable roadmaps only after explicit task command
- BACKLOG plans must NOT be implemented automatically
- ARCHIVED plans are historical references only
- When ACTIVE plans exist, exactly one plan should be marked PRIMARY
- Zero ACTIVE plans is valid when no executable roadmap is approved; never create a placeholder plan solely for bookkeeping
- Moving lifecycle state requires file move + registry update in same commit
- Folder status and registry status must always match
- Meaningful implementation commits must synchronize the active plan and this registry with factual validation/review status and the exact next action
- Implementation agents must not claim external review/acceptance that did not happen
- Detailed plan rules live in the plan file, not duplicated into AGENTS.md

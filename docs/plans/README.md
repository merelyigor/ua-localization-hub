# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** Stage 11B — REVIEWED / ACCEPTED; WWM API-driven compatibility UX — REVIEWED / ACCEPTED
**Next:** v1.2.11 RELEASE PREPARATION
**Implementation authorization:** Release Candidate preparation only; final publication remains an Owner gate

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | Stage 11B and WWM API-driven compatibility UX — REVIEWED / ACCEPTED | v1.2.11 RELEASE PREPARATION |

Release cycles: `v1.2.9` and `v1.2.10` — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.10 exact RC assets, public tag, release body and downloads were verified.

Stage 9 analysis, Stage 10 WWM implementation/real E2E, Stage 11A, Stage 11B, and the WWM API-driven compatibility UX are REVIEWED / ACCEPTED. Compatibility UX implementation SHA `e9834bf2cd80fee3137b2349ac547752e7af20e3` passed exact-SHA CI run `38531685694` (1188/1188 tests), with external review BLOCKER 0 / IMPORTANT 0 / OPTIONAL 0. Stage 11B implementation SHA `0ee0d819eb6f3f6fb404eb283095944d955f2a7f` passed exact-SHA CI run `38470073111` (1180/1180 tests). v1.2.10 is RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. Stage 8B remains OPTIONAL / NOT STARTED. Current dependent work is v1.2.11 release preparation; final publication remains an Owner gate. Do not invent Stage 11C or start unrelated work.

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

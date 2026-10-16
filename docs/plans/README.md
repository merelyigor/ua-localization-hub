# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** v1.2.11 RC #40 legacy Steam-build staleness correction — IMPLEMENTED / VALIDATED / PENDING PRE-COMMIT EXTERNAL ARCHITECT REVIEW
**Next:** External Architect review of the RC #40 correction; RC #39 and #40 remain FAILED / NOT ACCEPTED / DO NOT REUSE
**Implementation authorization:** Pre-commit corrective validation only; no commit/push, new RC, final tag, GitHub Release, asset publication, or NEXT reset

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | v1.2.11 RC #40 correction — PENDING PRE-COMMIT EXTERNAL REVIEW | Architect review; do not reuse RC #39/#40 |

Release cycles: `v1.2.9` and `v1.2.10` — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.10 exact RC assets, public tag, release body and downloads were verified.

Stage 9 analysis, Stage 10 WWM implementation/real E2E, Stage 11A, Stage 11B, WWM API-driven compatibility UX, and the v1.2.11 RC #39 migration correction are REVIEWED / ACCEPTED. RC #39 / run `38552297818` and RC #40 / run `38825506167` both remain FAILED OWNER SMOKE / NOT ACCEPTED / DO NOT REUSE. RC #40 exposed that historical Steam build IDs were incorrectly used as legacy root-binding evidence. Its bounded correction is implemented/validated and awaits pre-commit external Architect review. No commit, push or new RC is authorized. Stage 8B remains OPTIONAL / NOT STARTED. Do not invent Stage 11C or start unrelated work.

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

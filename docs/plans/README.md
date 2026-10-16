# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** RC #42 external-target reinstall correction — PRE-COMMIT REVIEW
**Next:** Complete bounded correction, automated validation and external pre-commit review; RC #39/#40/#41/#42 remain FAILED / NOT ACCEPTED / DO NOT REUSE
**Implementation authorization:** Only the explicitly requested RC #42 correction; no commit/push/new RC. Publication remains an Owner gate.

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | RC #42 external-target reinstall correction — PRE-COMMIT REVIEW | Validate and export patch for external review; do not commit/push or create RC #43 |

Release cycles: `v1.2.9` and `v1.2.10` — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.10 exact RC assets, public tag, release body and downloads were verified.

Stage 9/10/11A/11B and prior accepted compatibility/migration corrections remain REVIEWED / ACCEPTED. RC #39/#40/#41/#42 are FAILED OWNER SMOKE / NOT ACCEPTED / DO NOT REUSE. RC #42 / run `38930894752` failed because externally changed WWM targets with a trusted bound state and no active journal were treated as recovery failure, blocking fresh API Install as well as stale Restore. The current bounded pre-commit correction separates `NeedsReinstall` from unsafe/corrupt `Modified`, captures exact current targets for the next operation's durable rollback baseline, and keeps the historical pre-Hub snapshot unchanged with Restore disabled when stale. Read-only Owner-state diagnosis only; no real game files mutated. This correction remains PRE-COMMIT REVIEW; commit/push/RC #43 are not authorized. Stage 8B remains OPTIONAL / NOT STARTED; no Stage 11C.

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

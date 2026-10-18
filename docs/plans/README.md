# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** v1.2.18 — RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER
**Next:** OWNER DECISION REQUIRED.
**Implementation authorization:** NONE. No Stage 11C; Stage 8B is OPTIONAL / NOT STARTED.

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | v1.2.18 — released | OWNER DECISION REQUIRED |

Release cycles through `v1.2.18` are published and archived. Owner explicitly waived Architect review for v1.2.15, v1.2.16, v1.2.17 and v1.2.18; no external acceptance is claimed for these waivers.

Stage 9/10/11A/11B and prior accepted compatibility/migration corrections remain REVIEWED / ACCEPTED. v1.2.18 is published stable as Release ID `416655440`, tag peeling to `76c590c3170c18775142e33daa5c96fcd03e065a`; lifecycle is RELEASED / PUBLIC VERIFIED / ARCHITECT REVIEW WAIVED BY OWNER. Stage 8B remains OPTIONAL / NOT STARTED. No implementation work is authorized; no Stage 11C is introduced.

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

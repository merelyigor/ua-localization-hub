# Plans Registry

## Current focus

**Primary:** `localization-hub-multigame`
**Current phase:** v1.2.12 — IMPLEMENTED / VALIDATED / PENDING PRE-COMMIT EXTERNAL ARCHITECT REVIEW
**Next:** External Architect review of the corrected implementation on `review/v1.2.12-authoritative-latest`; do not merge, start RC, or publish.
**Implementation authorization:** Owner-authorized bounded v1.2.12 work is in progress under pre-commit review. No Stage 11C; Stage 8B is OPTIONAL / NOT STARTED.

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| PRIMARY | `localization-hub-multigame` | [active/localization-hub-multigame.md](active/localization-hub-multigame.md) | v1.2.12 — AUTHORITATIVE LOCALIZATION FRESHNESS | External pre-commit review of complete patch |

Release cycles: `v1.2.9`, `v1.2.10`, and `v1.2.11` — RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. v1.2.11 exact RC assets, annotated tag, release body and public downloads were verified and archived in [`../releases/v1.2.11.md`](../releases/v1.2.11.md).

Stage 9/10/11A/11B and prior accepted compatibility/migration corrections remain REVIEWED / ACCEPTED. RC #39/#40/#41/#42 are FAILED OWNER SMOKE / NOT ACCEPTED / DO NOT REUSE. RC #42 correction `e87e2688e2c7fb144ced1adde84745abf3369684` passed Architect review, CI #265 / run `38957407339` (1209/1209); RC #43 then passed Owner WWM end-to-end installation smoke and v1.2.11 was published stable as Release ID `416033195`, tag peeling to `758755058e3a197a59ec41452fb6a95938b54d7a`. Lifecycle: RELEASED / PUBLIC VERIFIED / RELEASE REVIEWED / ACCEPTED. Current authorized follow-up is bounded v1.2.12 authoritative latest localization consistency and WWM release-version display. Stage 8B remains OPTIONAL / NOT STARTED; no Stage 11C.

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

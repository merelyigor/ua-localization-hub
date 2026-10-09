# Executable workflow

## Default Combined mode

```text
ARCHITECT PROMPT
→ IMPLEMENT + TESTS + DOCS / PLAN SYNC
→ VALIDATE
→ COMMIT / PUSH / CI / REPORT
→ ONE EXTERNAL ARCHITECT REVIEW
→ ACCEPT or CORRECT ONLY IF NEEDED
```

Це default для bounded low/medium-risk задач із достатньо визначеним scope. Target — one implementation prompt + one external review.

## Pre-commit review mode for high-risk work

```text
UNDERSTAND
→ READ-ONLY INSPECTION
→ ANALYZE
→ ARCHITECTURE
→ DECOMPOSE
→ IMPLEMENTATION PROMPT
→ IMPLEMENT
→ TEST / VALIDATE
→ PRE-COMMIT ARCHITECT REVIEW
→ COMMIT / PUSH / CI
→ FINAL EXTERNAL ACCEPTANCE REVIEW WHEN REQUIRED
```

Read-only investigation потрібна, коли current flow, architecture або risk недостатньо відомі. Вона не змінює файли, описує relevant files, data/control flow, dependencies, implementation points, risks і open questions; передчасний redesign не робиться.

Pre-commit mode застосовується лише для destructive/data-loss, security-critical, schema/data migration, public API redesign, architecture/framework change або іншого high-risk випадку, де commit до review створює суттєвий ризик. Для bounded low/medium-risk задач не створюються штучні pre-commit чи finalization iterations.

Окремий finalization prompt не потрібен, якщо agent уже має право commit/push і всі required gates пройшли.

```text
TASK LIFECYCLE:
IMPLEMENTED → VALIDATED → PENDING EXTERNAL REVIEW → REVIEWED / ACCEPTED

RELEASE LIFECYCLE:
RC READY → OWNER SMOKE ACCEPTED → RELEASED → PUBLIC VERIFIED → RELEASE REVIEWED / ACCEPTED
```

## Task types and states

Task types: read-only investigation, implementation, corrective implementation, release/validation task, documentation/process task.

Factual states:

- до external review: `IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW`;
- після успішного external review: `COMPLETED / REVIEWED / ACCEPTED`;
- при material issue: `NEEDS CORRECTION`.

Corrective iteration створюється лише для `BLOCKER`, `IMPORTANT`, failed required validation або material baseline mismatch. `OPTIONAL` сам по собі не створює новий prompt. Approved dependent work не починається, поки unresolved BLOCKER або IMPORTANT не закриті.

Після завершення approved work, коли немає unresolved BLOCKER/IMPORTANT, approved dependent next step і активного незавершеного roadmap, стан циклу: `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`. Architect STOP-ить і не генерує автоматично audit, roadmap, refactoring, feature, release cycle або наступний task. Наступний cycle починає Owner.

## Session policy and changes

Новий major feature, coherent roadmap stage або substantially different subsystem зазвичай починається у fresh Implementation Agent session. Focused corrective prompt для безпосереднього попереднього task і мала continuation можуть залишатися в тій самій session. Correctness не залежить від opaque memory; нова session відновлює repository context і explicit prompt.

Якщо Owner змінює material requirements mid-task, Implementation Agent не зливає їх мовчки: Architect переглядає architecture/scope і надає оновлений explicit contract. Unrelated defect лише звітується, якщо він не потрібен для task. Unrelated failing tests не приховуються; baseline failure відокремлюється від regression, а unavailable validation звітується як така.

Для Windows-native validation Implementation Agent за наявності native UI control може локально зібрати й запустити застосунок, пройти об'єктивні smoke-сценарії у WinForms UI, перевірити перемикання ігрових екранів та tray/window lifecycle, виконати automated tests, оглянути logs/output і надати screenshots. Evidence має прив'язуватися до exact source/commit SHA та фактично запущеного EXE і містити сценарії, PASS/FAIL та шляхи знімків. Owner не повинен повторювати суто механічні перевірки, які агент може безпечно виконати й підтвердити; Architect review та Owner-specific суб'єктивна візуальна/product acceptance або явно заданий Owner gate залишаються окремими. Без прямої task-авторизації агент не змінює реальні game files, user data, зовнішні сервіси чи release/publication state; перевага надається read-only перевіркам і synthetic/temp fixtures, а destructive сценарій зупиняється до підтвердження. Screenshots мають уникати зайвого розкриття приватних даних. Якщо native UI control недоступний, це обмеження прямо зазначається; smoke не можна заявляти виконаним.

Нуль ACTIVE планів валідний. Bounded owner-authorized task може виконуватися без roadmap; placeholder ACTIVE/PRIMARY не створюється. Plan створюється лише для справжнього multi-step roadmap або роботи, яку потрібно переносити між сесіями. Broad product development зазвичай потребує approved roadmap/PRIMARY.

## Plans and persistent context

`docs/plans/README.md` є canonical registry. Meaningful task, що просуває ACTIVE plan, синхронізує plan і registry у тому самому commit. Implementation Agent не може external-accept себе. За [AGENTS §42](../../AGENTS.md) material state зберігається в CURRENT і append-only journal.

## Release flow

```text
release preparation
→ local validation
→ normal CI
→ Release Candidate
→ artifact/hash/version/release-note validation
→ Architect GO / NO-GO
→ Owner publication where manual
→ live release verification
→ post-release archive / NEXT reset / plan closure
```

Точний release contract визначають `AGENTS.md` і `docs/releases/`. Tag сам по собі не означає complete stable release. Manual owner action не вигадується й не вважається виконаною без evidence.

## Definition of Done

Окрім [AGENTS §33](../../AGENTS.md), task має мати satisfied acceptance criteria, validation evidence, scoped diff, синхронізовані docs/plan/context, якщо потрібно, і не мати unresolved BLOCKER/IMPORTANT. External review виконується, коли workflow вимагає acceptance.

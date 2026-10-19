# Виконання задачі

## План і ризик

Перед редагуванням зафіксувати проблему, baseline, scope, ризик, інваріанти, етапи та критерії успіху. Для малої задачі достатньо плану в сесії; багатокрокову/ризикову задачу вести через DevStandard у `docs/plans/active/execution/`. Продуктова roadmap лишається окремою в `docs/plans/README.md`.

| Ризик | Приклади | Перевірки |
|---|---|---|
| Низький | docs, статичний текст | relevant checks, self-review diff |
| Середній | UI lifecycle, polling, notification, retry | відтворення, focused regressions, Code gates, focused review |
| Високий | game mutation, recovery/migration, updater, package selection, security | письмові інваріанти, synthetic E2E, failure/rollback/cancellation/restart за застосовністю, Code gates, окремий pre-commit review у Codex |

Мінімальні технічні рішення в межах мети агент приймає сам. Матеріальне розширення мети, несумісний контракт поза задачею або невідновну дію з реальними даними повернути Owner.

DevStandard створює план зі статусом `draft`; лише пряме схвалення Owner саме цього плану дозволяє статус `approved`. Після схвалення режим `autonomous` дозволяє працювати самостійно тільки всередині його Scope. Перед стартом `plan.py status` вимагає чистого дерева поза планом; ніколи не stash/clean/commit чужі чи review-файли, а відмову пояснити Owner.

Для фаз плану або суттєвих незалежних пакетів застосовувати Brygada, коли поточна Codex-сесія надає сумісний native subagent tool. Головна сесія володіє планом, приймає diff/Verify, переглядає findings і лишається відповідальною. Якщо сумісного інструмента немає, виконати фазу самому й записати fallback.

## Етапи

1. Перевірити контекст, baseline і status; зберегти чужі зміни.
2. Дослідити повний affected flow, додати regression для дефекту.
   Для ще не доведеної причини використати локальний systematic-debugging за [SKILLS.md](SKILLS.md); reproduction має відтворювати справжній lifecycle gate.
3. Реалізувати мінімальну зміну; синхронізувати contracts/CURRENT/journal/plans. NEXT змінювати лише для public-facing змін.
4. Запустити focused tests і harness; виправити failures, не маскувати їх skip або слабшими assertions.
5. Перевірити фінальний diff за [REVIEW.md](REVIEW.md); після correction знову перевірити affected behavior.
   Перед твердженням про завершення застосувати verification-before-completion; зберегти actual command/exit/output evidence, а не лише звіт іншого агента.
6. Git hooks локально запускають staged safety/diff checks перед commit та Docs/Code profile перед push. У Windows Codex запускай Git-команди `commit`/`push` через Git Bash: PowerShell runner не може створити дочірній hook-процес (`Function not implemented`); hook-wrapper передає перевірки Windows PowerShell. Hosted CI лишається незалежним обов'язковим exact-SHA gate; ніколи не обходити hook через `--no-verify`.
7. Якщо task не local-only/pre-commit: stage тільки intended files, повторити `scripts/Check-RepositorySafety.ps1` та `git diff --cached --check`, оглянути staged diff, commit/push і дочекатися normal CI на точному SHA.
8. Звітувати completed або конкретний blocker із доказами. Продовжувати вже дозволені залежні кроки без нового prompt.

## Команди

```powershell
pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Docs
pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Code
dotnet test BdoUaClient.sln -c Release --no-build --filter "<affected tests>"
```

Docs — лише документація. Scripts/CI/config зміни потребують Code та власних негативних regression tests. Code включає Release build (0 warnings/errors), full tests, resolver/release-note tests, NEXT schema/render, workflow/portable rules, hygiene scan і diff-check. Обидва профілі локально виконують повний доречний набір. GitHub CI пропускає .NET restore/build/full tests лише коли всі зміни — Markdown/JSON/PNG усередині `docs/` або точно визначені root/agent-doc files; policy, safety, harness і release tooling gates лишаються. Будь-який app/test/build/workflow/script/unknown path, інший file type під `docs/`, невизначений diff чи manual run вимагає повного віддаленого .NET validation. RC завжди повний. Scanner не замінює ручний secret/private огляд. Focused tests, exact-SHA CI й RC integrity не замінюються локальним harness.

## Safety і відмови

Тести використовують synthetic/temp roots. Для транзакцій перевіряти partial failure, cancellation, rollback failure, crash/restart, wrong root/layout, corrupt metadata й racing targets. Baseline mismatch, unresolved journal або failed hash/size validation не обходити заради успіху.

Недоступний інструмент/тест позначати UNAVAILABLE/NOT RUN; не вигадувати PASS. CI failure блокує RC/publication. Native smoke/external review є додатковими gates лише за прямою вимогою Owner.

Невиправлені BLOCKER/IMPORTANT блокують commit і залежні кроки; OPTIONAL не створює обов'язкового corrective циклу.

## Завершення

Task: `IMPLEMENTED → VALIDATED → INTERNAL REVIEW COMPLETE → COMPLETED`.

Release: `CI PASS → RC READY / ARTIFACTS VERIFIED → RELEASED → PUBLIC VERIFIED → FINALIZED`. Повний контракт — [releases/README.md](../releases/README.md).

Дозвіл «реліз і публікуй» охоплює механічну публікацію після hard gates та docs finalization. Звичайна implementation задача не створює release автоматично. ACTIVE roadmap сама не авторизує новий етап. Після дозволеної мети: `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`.

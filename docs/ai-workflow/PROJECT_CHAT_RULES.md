# Codex project bootstrap

Чинний процес: **CODEX-OWNED**. Owner визначає мету й publication intent. Агент сам планує, реалізує, тестує, перевіряє diff, виконує дозволені commit/push/CI й звітує. Зовнішній архітектор не є обов'язковим gate.

Прочитай `AGENTS.md`, `docs/ai-workflow/README.md`, `docs/development/CURRENT.md`; перевір baseline/branch/status/diff і relevant plan/contracts/source/tests. Архівні pending-review записи не є новим gate.

Scoped technical contracts — `docs/ai-workflow/DOMAIN_RULES.md`; перед зміною області прочитай relevant § за root routing. Selected skills/triggers та privacy/fallback — `docs/ai-workflow/SKILLS.md`; imports не є залежністю CI й не дозволяють змінювати safety або release gates.

Визнач scope, ризик, інваріанти, план і критерії успіху. Низький ризик — self-review; середній — focused regressions/review; високий — synthetic failure/rollback/restart tests та окремий внутрішній pre-commit review агентом Codex. Якщо delegation недоступне, виконай adversarial pass і назви обмеження. BLOCKER/IMPORTANT або failed mandatory gate блокують залежні кроки.

`pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Docs` — суто docs.
`pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Code` — код/tests/scripts/CI/config, включно з Release build/full tests. Додай focused regressions; не послаблюй safety checks.

Local hooks: `pwsh -File scripts/Install-ProjectHooks.ps1`; pre-commit перевіряє staged safety/diff, pre-push запускає fail-safe Docs/Code profile. У Windows Codex запускай `git commit`/`git push` через Git Bash: PowerShell runner не може запустити дочірній hook (`Function not implemented`); wrappers використовують Windows PowerShell для виклику перевірок. Hosted exact-SHA CI та release workflow не замінюються. Не використовуй `--no-verify`.

Task: `IMPLEMENTED → VALIDATED → INTERNAL REVIEW COMPLETE → COMPLETED`.
Release: `CI PASS → RC READY / ARTIFACTS VERIFIED → RELEASED → PUBLIC VERIFIED → FINALIZED`.

Default implementation включає scoped commit/push і exact-SHA CI, якщо Owner не заборонив. Release потребує intent; «реліз і публікуй» дозволяє artifact verification, publication, live verification і docs finalization. Native smoke/external review додаються лише за прямою вимогою Owner. Не вигадуй PASS або external acceptance.

Чинний дозвіл не перепитуй. Не змінюй реальні game files у тестах, не коміть secrets/user config/review artifacts, не рухай existing tags і не repack RC ZIPs. Зберігай decisions у CURRENT/journal/plans; іншу roadmap/feature без Owner intent не починай.

Деталі: `WORKFLOW.md`, `REVIEW.md`, `HANDOFF.md` в цьому каталозі та `docs/releases/README.md`. Фактичний diff/tests/CI/artifacts мають пріоритет над звітом. Після дозволеної мети: `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`.

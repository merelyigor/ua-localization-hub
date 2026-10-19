# Звіт і передача контексту

Повідомити результат, зміни/причину, focused/full validation із counts/exit codes, internal review findings/limitations, commit/parent/branch/push та exact-SHA CI. Невиконані перевірки й scope назвати прямо. Для release додати source/run IDs, tag/artifact/public hashes і finalization.

## Persistence

`AGENTS.md` — правила; `docs/ai-workflow/` — процес; `docs/plans/` — roadmap; `CURRENT.md` — handoff; append-only `history/YYYY-MM.md` — рішення; subsystem docs — contracts; `docs/releases/` — release facts.

Meaningful task синхронізує контекст у своєму implementation commit. Post-release archive/NEXT reset — окремий дозволений крок після live verification. Історичні acceptance записи не переписуються.

Наступна сесія відновлює git baseline/status/diff, Owner goal, current step, contracts/tests і evidence. Старий transcript або зовнішній архітектор не є prerequisite.

Harness report у ignored `artifacts/validation/` містить SHA/profile/commands/exit codes; для handoff потрібні journal і Git/CI evidence, а не тільки локальний файл.

Не публікувати raw user logs, machine paths, credentials або персональні подробиці. Чужі untracked/modified файли не чіпати й назвати у звіті. Завершивши мету, повернути `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`; іншу feature/stage/release не починати автоматично.

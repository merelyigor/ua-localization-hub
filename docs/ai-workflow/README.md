# Робочий процес Codex

Чинний режим: **CODEX-OWNED**. Owner задає мету; агент у Codex планує, реалізує, перевіряє й завершує дозволену роботу. Зовнішній архітектор не є обов'язковим учасником. Це замінює попередній Combined/external-review процес; історичні записи залишаються історією.

## Початок сесії

1. Прочитати `AGENTS.md`, цей файл і `docs/development/CURRENT.md`.
2. Перевірити branch/status/diff, потрібний baseline, relevant ACTIVE plan і subsystem contracts.
3. Визначити scope, ризик, критерії успіху та чинні дозволи Owner.
4. Дослідити affected source/tests; для дефекту спочатку встановити причину.
5. Для малої задачі вести план у сесії; для багатосесійної/ризикової роботи зберегти його у `docs/plans/`.

Поточний запит Owner має пріоритет над старими task prompts. Архівні pending-review статуси не блокують нову дозволену роботу. Не перепитувати вже наданий дозвіл.

## Один цикл

`Owner goal → inspection/risk/plan → implementation/tests/docs → local gates → internal review/correction → commit/push → exact-SHA CI → completed`

Ризик визначає глибину перевірки. Невиправлені BLOCKER/IMPORTANT і failed mandatory gates блокують залежні кроки.

## Документи

- [WORKFLOW.md](WORKFLOW.md) — етапи, ризик, команди та завершення.
- [ROLES.md](ROLES.md) — відповідальність і автономія.
- [REVIEW.md](REVIEW.md) — внутрішня перевірка й findings.
- [PROMPTS.md](PROMPTS.md) — контракт задачі.
- [HANDOFF.md](HANDOFF.md) — звіт і передача контексту.
- [PROJECT_CHAT_RULES.md](PROJECT_CHAT_RULES.md) — переносний bootstrap до 7500 UTF-16 code units.
- [Процедура релізу](../releases/README.md) — publication authority, RC і finalization.

## Harness

```powershell
pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Docs
pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Code
```

Docs — суто документація. Code — код/tests/scripts/CI/config, включно з Release build/full tests. Команда не commit/push-ить, не запускає застосунок і не публікує. Focused regressions обираються за affected flow. Звіт із SHA/exit codes зберігається в ignored `artifacts/validation/`; він не є автоматичним acceptance.

Проєктний `.codex/config.toml` збільшує instruction budget до 128 KiB, бо `AGENTS.md` перевищує default 32 KiB. Налаштування завантажується підтримуваними клієнтами для trusted project з наступної сесії. Не змінює глобальні permissions, модель, credentials чи sandbox. Якщо клієнт його не підтримує, mandatory docs читаються явно. [Офіційна документація](https://developers.openai.com/codex/guides/agents-md).

Реальні game files не є тестовою fixture. Публікація потребує Owner intent. Історичний external acceptance не приписується внутрішньому review.

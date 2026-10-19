# Контракт задачі

Owner може описати мету звичайною мовою; агент сам робить її конкретною. Окремий Architect prompt не потрібний.

Для складної задачі достатньо: мета/поведінка, baseline/scope, allowed/non-goals, ризик/інваріанти, етапи, acceptance/validation, commit/push/publication intent, очікувані докази. Не копіювати handbook у кожен prompt.

План між сесіями містить current step, completed/pending, decisions, validation і наступну дію; зберігається у `docs/plans/`.

Correction описує відтворення, результат і safety constraints. Агент сам перевіряє причину й виправляє її в межах мети без нового prompt після кожного finding.

Read-only/diagnosis task не авторизує implementation. Pre-commit/local-only task не авторизує commit/push. «Реліз і публікуй» дозволяє повний цикл за [процедурою](../releases/README.md).

Для migration додати ownership, unmatched records, interruption/restart та кількісні результати. Для game mutation — exact rollback baseline, durable journal, failure/cancellation/recovery і synthetic fixtures.

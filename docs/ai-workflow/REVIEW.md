# Внутрішня перевірка в Codex

## Докази

Перевіряти actual diff/source, тести й результати команд із exit codes, потім CI/artifacts/runtime. Звіт автора допомагає навігації, але не доводить коректність. Green tests не доводять відсутності дефектів.

## За ризиком

- Низький: self-review фінального diff і відповідності меті.
- Середній: focused review lifecycle/concurrency, error paths і regressions.
- Високий: окремий read-only агент-рецензент у Codex до commit. Передати baseline/diff, інваріанти й тести; просити шукати defects. Якщо delegation недоступне — окремий adversarial pass, перевірка failure paths і явне повідомлення про відсутність незалежного агента. Не вигадувати review PASS.

## Checklist

- Відтворено повний affected flow, включно зі startup/restart.
- Ownership/root/layout/package identity збережено на mutation boundary.
- Помилка/скасування/гонка не лишають partial result або неправдивий success.
- Fixtures існують ДО lifecycle gate, який перевіряють.
- Compatibility, TLS/path/hash validation не послаблено.
- Логи діагностичні, без secrets/private data.
- Docs/context правдиві; untracked artifacts не stage-яться автоматично.

## Findings

`BLOCKER` — incorrect behavior, crash, data loss/security/safety violation, broken contract або unmet mandatory requirement.

`IMPORTANT` — суттєвий defect/evidence gap до залежного кроку.

`OPTIONAL` — polish; сам по собі не запускає corrective loop.

Невиправлені BLOCKER/IMPORTANT блокують commit/release. Кожну correction перевірити й закрити доказом. Review стосується останнього diff.

Результат: `INTERNAL REVIEW COMPLETE`, `NEEDS CORRECTION` або `BLOCKED / INSUFFICIENT EVIDENCE`. Назвати спосіб перевірки, findings і limitations. Не приписувати external acceptance.

## Release

Перед publication перевірити exact-SHA CI, RC source/run, versions/hashes/manifests і notes. Після — live Release/tag/latest/assets/downloads. Внутрішній GO означає hard gates PASS і чинний Owner publication intent; зовнішній GO не потрібний за замовчуванням.

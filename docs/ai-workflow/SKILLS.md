# Підключені навички

Owner обрав дев'ять skills зі свого Harness. Вони встановлюються тільки в ignored `.agents/skills/`; плагін, його hooks, глобальні правила та model guards не встановлюються. Exact revision і hashes resources — [skills.lock.json](skills.lock.json); це metadata без приватного source. Не комітити самі imports.

## Коли читати

| Сценарій | Локальний entrypoint | Застосування |
|---|---|---|
| Завершення відповіді | `.agents/skills/report-format/assets/report-format.md` | Єдине джерело форми відповіді; читає головний агент |
| Зміна або очищення форми | `.agents/skills/report-format/SKILL.md` | Головний агент прибирає старі дублікати, не делегує цю роботу |
| Відомий симптом, причина ще не доведена | `.agents/skills/systematic-debugging/SKILL.md` | Facts → reproduction → earliest divergence → verified cause → regression |
| Перевірка готового diff | `.agents/skills/adversarial-review/SKILL.md` | Read-only review незалежним агентом; actual callers/state/error paths, без редагування reviewer-ом |
| Перед твердженням про завершення/commit/push | `.agents/skills/verification-before-completion/SKILL.md` | Свіжі команди й decisive output, не лише чужий звіт |
| Запит перевірити якість тестів | `.agents/skills/test-quality-audit/SKILL.md` | Targeted read-only audit false confidence/fixtures/isolation; не автоматичний audit кожної задачі |
| Оптимізація інструкцій | `.agents/skills/agent-rules-optimizer/SKILL.md` | Inventory, coverage ledger, scope routing і preservation; не послаблювати safety |
| План багатокрокової/ризикової задачі | `.agents/skills/dev-standard/SKILL.md` | План у `docs/plans/active/execution/`; точне схвалення Owner перед стартом |
| Виконання фази плану/суттєвий паралельний пакет | `.agents/skills/brygada/SKILL.md` і `.agents/skills/implement-work-package/SKILL.md` | Делегувати лише через доступний native Codex subagent tool; головний агент приймає diff і перевірки |

## Узгодження з проєктом

- User task, game safety, exact-SHA CI та release contract мають пріоритет над загальними припущеннями imported skills. Ці skills не дають дозволу на game mutation, publication або глобальні зміни.
- `report-format` припускає глобальне hook injection; тут його немає. Головний агент явно читає локальний asset на завершенні. Не запускати всю cleanup-процедуру для звичайної відповіді й не копіювати asset у public docs.
- `dev-standard` веде task execution plans окремо від продуктового roadmap у `docs/plans/README.md`; plan status draft → approved потребує прямої згоди Owner саме на цей план. `autonomous` дозволяє вирішувати лише дрібні розбіжності всередині approved scope, не розширює дозвіл на реліз чи гру.
- `brygada` Codex route адаптується до фактичної схеми `multi_agent_v1`: не задавати модель, якщо Owner її прямо не вибрав; передавати `fork_context: false`, якщо параметр доступний. Якщо runtime не надає запитаний маршрут/перевірку моделі, не вигадувати доказ; виконати самостійно або повідомити обмеження.
- Harness macOS Seatbelt `sandboxed.sh` і глобальний Codex plugin/hook bundle не встановлюються. Windows використовує repo-local `.githooks` через локальний `core.hooksPath`; hosted CI та release workflows лишаються незалежними обов'язковими gates.
- Проєктні факти evidence залишаються в AGENTS §34.5; старі вимоги друкувати повний commit body/всі файли й довгий приклад вилучено. Plan, PR, subagent reports і release archive — окремі артефакти, не форма відповіді Owner.
- Review severity узгоджується за фактичним впливом: safety/data-loss/contract failures → BLOCKER, інший material defect → IMPORTANT, необов'язковий polish → OPTIONAL. Imported labels самі по собі не замінюють project gates.
- На Windows запускати Python helpers з `python -X utf8 -B`, щоб Unicode не ламав output. При повторному збої змінити підхід; не обходити failing checks.
- `dev-standard`, `brygada` та `implement-work-package` імпортовані з тим самим pinned Harness revision, що й базові skills. `ci-to-local-hooks`, global baseline, launcher scripts і macOS Seatbelt не імпортовані. Product roadmap лишається в `docs/plans/README.md`; task plans живуть у `docs/plans/active/execution/`. Hooks запускають локальні перевірки, але не замінюють hosted CI.

## Відновлення й перевірка

```powershell
pwsh -File scripts/Restore-ProjectSkills.ps1 -Repository '<owner>/<private-harness>'
pwsh -File scripts/Check-ProjectSkills.ps1 -RequireInstalled
pwsh -File scripts/Install-ProjectHooks.ps1
```

Використовується штатний Codex Skill Installer і наявна Git authentication; secrets не передаються в arguments або repository files. Завантаження відбувається в ignored `artifacts/skill-stage-*`; лише перевірені навички переносяться в `.agents/skills`. Failed staging лишається поза discovery для діагностики; неперевірені instructions не активуються. Існуючі/змінені imports та сторонні файли не перезаписуються. Для оновлення revision потрібні явна задача, review resources та оновлення lock; автоматичного moving-main sync немає.

GitHub CI не має доступу до приватного source. Коли imports відсутні, діє публічний CODEX-OWNED workflow/REVIEW/WORKFLOW і звичайні safety/build/test gates; checker явно повідомляє про fallback. Частковий набір допускається лише коли кожен присутній import проходить повну inventory/hash перевірку; `-RequireInstalled` вимагає весь набір. Restore додає лише відсутні навички й не перезаписує наявні. Встановлені локально skills перевіряються за повним file inventory/hash. Після відновлення нова сесія бачить project-local metadata; поточна може читати paths явно.

Project-local discovery та progressive loading описані в [офіційній документації Codex](https://developers.openai.com/codex/skills). Локальне встановлення не доводить, що всі runtime сценарії застосунку перевірені.

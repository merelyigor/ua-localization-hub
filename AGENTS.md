# AGENTS.md

<!-- dev-standard: plans=docs/plans/active/execution autonomous -->

CODEX-OWNED — агент у Codex веде дозволену задачу від аналізу до internal review, commit/push та exact-SHA CI. Зовнішній архітектор не є обов'язковим gate.

## Область і джерела

Owner задає goal/scope і publication intent. Звичайна implementation задача не дозволяє release або mutation реальних файлів гри. Чужі зміни й review artifacts не чіпати. Тести — synthetic/temp; secrets/user config/private skill source не комітити. TLS/path/hash/rollback safety не послаблювати заради green output.

Повні умовні контракти — [DOMAIN_RULES.md](docs/ai-workflow/DOMAIN_RULES.md). Перед роботою в області прочитай відповідний повний §; номера й важливі safety rules збережені для старих посилань. Усі code paths відносні до git root. Повторені core safety paragraphs звіряються preservation gate, щоб не розійтися.

Обов'язкове читання до зміни потоку:
- Game detection/state/install/restore/recovery/migration — DOMAIN_RULES §11, §13–19, §27–29; власність root/layout, journal, rollback і LocalData ReadOnly не послаблювати.
- API/package/update identity — DOMAIN_RULES §10–11, §15, §18, §24; immutable URL, свіжий latest, size/SHA залишаються safety boundary.
- Self-update, tray або notifications — DOMAIN_RULES §41; для publication також `docs/releases/README.md`.
- UI lifecycle/config — DOMAIN_RULES §7–9, §18, §20–23 і affected game contract; presentation не змінює service trust rules.

## Чинне уточнення власника — WWM (2026-10-21)

Цільова layout WWM обирається за фактичним клієнтом: доведені Steam/Epic → `Package/HD/oversea/locale/`; відомі каталоги офіційного клієнта (`wwm_standard`, `wwm_standart`, `wwm_lite`) з наявною LocalData layout → `LocalData/Patch/HD/oversea/locale/`; невідомі корені без Package layout → єдина наявна LocalData layout. Канонічний текст і джерела — DOMAIN_RULES §13.7, §13.10.

## §1 🌍 Мова

§1.1 Спілкування, коментарі, звіти — **тільки українською**.

§1.2 Англійська дозволена для: назв технологій, API, бібліотек, ідентифікаторів, системних термінів.

§1.3 Код — англійськими назвами змінних/функцій.

§1.4 Не змішувати українську з російською. Російську не використовувати.

---

## §2 🎯 Призначення

[повний §2](docs/ai-workflow/DOMAIN_RULES.md#section-2)

## §3 🧠 Принцип роботи

[повний §3](docs/ai-workflow/DOMAIN_RULES.md#section-3)

## §4 ⚖️ Пріоритети

[повний §4](docs/ai-workflow/DOMAIN_RULES.md#section-4)

## §5 💻 Код

[повний §5](docs/ai-workflow/DOMAIN_RULES.md#section-5)

## §6 🧩 Не ускладнювати

[повний §6](docs/ai-workflow/DOMAIN_RULES.md#section-6)

## §7 🏗️ Архітектура

[повний §7](docs/ai-workflow/DOMAIN_RULES.md#section-7)

## §8 🖥️ UI

[повний §8](docs/ai-workflow/DOMAIN_RULES.md#section-8)

## §9 ⚡ Не блокувати UI

[повний §9](docs/ai-workflow/DOMAIN_RULES.md#section-9)

## §10 🔌 API

[повний §10](docs/ai-workflow/DOMAIN_RULES.md#section-10)

## §11 📋 API Contract: bdo-ua.com.ua

[повний §11](docs/ai-workflow/DOMAIN_RULES.md#section-11)

## §12 🔐 Secrets

§12.1 Ніколи не додавати в репозиторій: API keys, tokens, passwords, signing secrets, credentials.

§12.2 Desktop `.exe` не може надійно приховати секрет.

---

## §13 🔍 Пошук гри

[повний §13](docs/ai-workflow/DOMAIN_RULES.md#section-13)

## §14 💾 Файли та backup

§14.2 Бекап створювати один раз, не перезаписувати good backup модифікованим файлом.
Обов'язкові підпункти перед будь-якою файлу mutation: §14.1 (знати, як відновити), §14.3 (атомарна установка), §14.4 (temp), §14.5 (SHA-256), §14.6–§14.7 (original snapshot vs restore points), §14.8 (чотири операції), §14.9 (не видаляти `.loc`), §14.10 (WWM targets, exact root/layout, journal, rollback).

Деталі: [повний §14](docs/ai-workflow/DOMAIN_RULES.md#section-14).

## §15 📦 API Release Metadata

Обов'язкові підпункти перед install/update: §15.1 (12-крокова safety workflow, fresh latest без aggregate fallback), §15.2 (помилка до replace), §15.3 (rollback після replace, інакше `Corrupted`), §15.4 (WWM окремий latest endpoint, два-file transaction, bounded ZIP), §15.5 (BDO aggregate vs authoritative per-mode latest, `public_id` як release identity).

Деталі: [повний §15](docs/ai-workflow/DOMAIN_RULES.md#section-15).

## §16 🛡️ Захист шляхів

§16.1 Path traversal protection: нормалізація + перевірка, що файл в межах дозволеної директорії.

§16.2 Не довіряти filenames від API: не писати в system directories, не запускати executable.

---

## §17 ⬇️ Download

[повний §17](docs/ai-workflow/DOMAIN_RULES.md#section-17)

## §18 🔄 Стани та оновлення

[повний §18](docs/ai-workflow/DOMAIN_RULES.md#section-18)

## §19 🗑️ Відновлення та повернення до оригіналу

[повний §19](docs/ai-workflow/DOMAIN_RULES.md#section-19)

## §20 🪟 Windows

[повний §20](docs/ai-workflow/DOMAIN_RULES.md#section-20)

## §21 🎨 UX

[повний §21](docs/ai-workflow/DOMAIN_RULES.md#section-21)

## §22 📝 Логування та exceptions

[повний §22](docs/ai-workflow/DOMAIN_RULES.md#section-22)

## §23 ⚙️ Конфігурація та cache

[повний §23](docs/ai-workflow/DOMAIN_RULES.md#section-23)

## §24 🌐 Мережа

[повний §24](docs/ai-workflow/DOMAIN_RULES.md#section-24)

## §25 📚 Залежності

[повний §25](docs/ai-workflow/DOMAIN_RULES.md#section-25)

## §26 📦 Build та .exe

§26.1 Gates: `pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Code`; лише суто docs — `-Profile Docs`. Scripts/CI/config потребують Code.
§26.3 CI є незалежною exact-SHA перевіркою; failure блокує залежні кроки й publication. Native preview/gh/auth — у [повному контракті §26](docs/ai-workflow/DOMAIN_RULES.md#section-26).

Деталі: [повний §26](docs/ai-workflow/DOMAIN_RULES.md#section-26).

## §27 🧪 Тести

[повний §27](docs/ai-workflow/DOMAIN_RULES.md#section-27)

## §28 🔒 Security

§28.1 Усе зовнішнє — недовірене: API responses, filenames, URLs, API release metadata.

§28.2 TLS verification ніколи не вимикати.

§28.3 Не запускати executable отриманий з сервера (окрім whitelist операцій: copy/replace localization file/create directory/restore backup/delete temporary/cache files). Основний `languagedata_en.loc` не видаляється фізично як uninstall. Повернення до стану без української виконується через `Restore Original`.

§28.4 Невідомі operation відхиляти.

---

## §29 🔗 Сумісність

[повний §29](docs/ai-workflow/DOMAIN_RULES.md#section-29)

## §30 ✏️ Правила змін

§30.1 Мінімальний patch. Не змішувати feature/refactor/formatting.

§30.2 Не чіпати непов'язаний код.

§30.3 Refactor не змінює поведінку ненавмисно.

§30.4 Не залишати TODO замість реалізації.

§30.5 Не підміняти реалізацію placeholder/mock.

§30.6 Не hardcode: шляхи, usernames, tokens, credentials.

---

## §31 📄 Коментарі та документація

[повний §31](docs/ai-workflow/DOMAIN_RULES.md#section-31)

## §32 🚫 Не вигадувати

§32.1 Використовувати існуючий API contract.

§32.2 Не вигадувати API бібліотек.

§32.3 Складні задачі — маленькими кроками.

---

## §33 ✅ Definition of Done

Задача виконана, коли:

§33.1 Реалізована функціональність без placeholder.

§33.2 Оброблені error cases.

§33.3 UI responsive.

§33.4 Файлові операції захищені.

§33.5 Код відповідає архітектурі.

§33.6 Required profile за §26.1 проходить; Code включає Release build без warnings/errors, Docs не вимагає перебудови незміненого application code.

§33.7 Для Code повний Release suite та affected regressions проходять. Internal review завершено, BLOCKER/IMPORTANT закриті, exact-SHA CI перевірено після дозволеного push; пропущені перевірки чесно позначені.

---

## §34 Git і коміти

§34.2 **Правила комітів:**
- Формат першого рядка: `v{ЕТАП}.{ПІДЕТАП} — {короткий опис}`
- Після першого рядка — порожній рядок, потім **детальний опис** того, що зроблено
- Опис містить: що реалізовано, що змінено, що оновлено, чому саме так (якщо неочевидно)
- Кожен завершений етап/підетап — окремий коміт

§34.4 Чужі untracked/modified файли не включати у commit; зберігати їхній статус у handoff.

§34.5 Evidence цього проєкту: exact commit/parent/branch, command exit codes і test counts, exact-SHA CI run/result; для release також RC run/source, tag target, artifact/public hashes та finalization. Приватні локальні навички й чужі review artifacts не публікувати.

---

## §35 🔐 Публічний репозиторій

§35.1 Репозиторій **публічний**. Все, що потрапляє в git, стає доступним для всіх.

§35.2 **Перед кожним комітом агент зобов'язаний перевірити `git status` та `git diff` на наявність:**
- API keys, tokens, secrets
- Паролі, credentials
- Приватні ключі (SSH, TLS)
- Access tokens (GitHub, AWS, інші)
- Будь-які дані, які не повинні бути публічними

§35.3 **ЗАБОРОНЕНО** додавати в коміт:
- `.env` файли
- Файли з секретами навіть для "тимчасового" зберігання
- Будь-які credentials, отримані від користувача
- Token-и, створені для тестування

§35.4 Якщо випадково секрет потрапив в коміт:
1. Не пушити (якщо ще не запушено)
2. Видалити секрет з коду
3. Перезаписати git history (якщо вже запушено) — повідомити користувача
4. Попередити про необхідність ротації скомпрометованого секрету

§35.5 `.gitignore` повинен містити типові виключення для .NET проекту та секретів.

---

## §36 🚫 Заборони

Без прямої необхідності не:

§36.1 Видаляти великі частини проєкту.

§36.2 Змінювати framework/architecture/API contract.

§36.3 Оновлювати всі dependencies.

§36.4 Форматувати весь repository.

§36.5 Відключати security/TLS.

§36.6 Приховувати помилки.

---

## §37 🤔 Правило сумнівів

§37.1 Між швидким-небезпечним та складнішим-надійним — обирати надійне. Особливо для game files, backup, rollback, download validation, path handling.

---

## §38 💎 Головний принцип

§38.1 Користувач запускає → знаходить гру → вибирає локалізацію → встановлює без розуміння internals.

§38.2 **Ніколи не залишати гру у пошкодженому стані заради "успішної" операції.**

---

## §39 📋 Плани

`docs/plans/README.md` лишається продуктовим roadmap registry. Виконавчі плани окремих задач ведуться DevStandard у `docs/plans/active/execution/`; план стає executable лише після прямого схвалення Owner. BACKLOG автоматично не виконується; за наявності ACTIVE рівно один PRIMARY.

Деталі: [повний §39](docs/ai-workflow/DOMAIN_RULES.md#section-39).

## §40 📦 Архів реліз-нотаток

[повний §40](docs/ai-workflow/DOMAIN_RULES.md#section-40)

## §41 🔄 Оновлення клієнта

§41.9 Application update та localization operations (Install, Restore Original) взаємовиключні.

Деталі: [повний §41](docs/ai-workflow/DOMAIN_RULES.md#section-41).

## §42 🧭 Persistent development context

CURRENT і append-only monthly journal синхронізуються в implementation commit. Не публікувати приватні logs, user paths або raw source локальних skills.

Деталі: [повний §42](docs/ai-workflow/DOMAIN_RULES.md#section-42).

## §43 🤖 AI Development Workflow

§43.1 Режим **CODEX-OWNED**. Перед meaningful task прочитай `docs/ai-workflow/README.md` і `WORKFLOW.md`; для review — `REVIEW.md`.
§43.4 High-risk mutation/recovery/migration/updater потребують synthetic failure/rollback/restart evidence та окремого внутрішнього pre-commit review у Codex. BLOCKER/IMPORTANT блокують commit/release.
§43.9 Scoped commit/push дозволений після gates, якщо task не local-only/pre-commit. Потім перевірити CI точного SHA; не перепитувати чинний дозвіл.
§43.13 Task: `IMPLEMENTED → VALIDATED → INTERNAL REVIEW COMPLETE → COMPLETED`. Release — лише за Owner intent після exact-SHA CI, нового RC/integrity та live verification, з finalization. Зовнішній review/native smoke потрібні лише за explicit task requirement.
§43.15 Навички й triggers — `docs/ai-workflow/SKILLS.md`. Перед фінальною відповіддю прочитай `.agents/skills/report-format/assets/report-format.md`, якщо встановлено: це єдине джерело форми, не копіюй його в project docs.

Деталі: [повний §43](docs/ai-workflow/DOMAIN_RULES.md#section-43).

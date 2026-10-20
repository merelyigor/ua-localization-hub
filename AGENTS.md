# AGENTS.md

<!-- dev-standard: plans=docs/plans/active/execution autonomous -->

CODEX-OWNED — агент у Codex веде дозволену задачу від аналізу до internal review, commit/push та exact-SHA CI. Зовнішній архітектор не є обов'язковим gate.

## Чинне уточнення власника — WWM (2026-10-20)

За актуальною прямою вимогою власника всі нові встановлення/оновлення українізатора Where Winds Meet записують лише в `Package/HD/oversea/locale/`. `LocalData/Patch/HD/oversea/locale/` не є ціллю звичайних операцій; якщо Package відсутній, гра може бути знайдена, але встановлення недоступне. Це уточнення має пріоритет над історичним формулюванням §14.10 нижче. Виняток: старий незавершений transaction journal може виконати лише точний записаний rollback.

## Область і джерела

Owner задає goal/scope і publication intent. Звичайна implementation задача не дозволяє release або mutation реальних файлів гри. Чужі зміни й review artifacts не чіпати. Тести — synthetic/temp; secrets/user config/private skill source не комітити. TLS/path/hash/rollback safety не послаблювати заради green output.

Повні умовні контракти — [DOMAIN_RULES.md](docs/ai-workflow/DOMAIN_RULES.md). Перед роботою в області прочитай відповідний повний §; номера й важливі safety rules збережені для старих посилань. Усі code paths відносні до git root. Повторені core safety paragraphs звіряються preservation gate, щоб не розійтися.

Обов'язкове читання до зміни потоку:
- Game detection/state/install/restore/recovery/migration — DOMAIN_RULES §11, §13–19, §27–29; власність root/layout, journal, rollback і LocalData ReadOnly не послаблювати.
- API/package/update identity — DOMAIN_RULES §10–11, §15, §18, §24; immutable URL, свіжий latest, size/SHA залишаються safety boundary.
- Self-update, tray або notifications — DOMAIN_RULES §41; для publication також `docs/releases/README.md`.
- UI lifecycle/config — DOMAIN_RULES §7–9, §18, §20–23 і affected game contract; presentation не змінює service trust rules.

## §1 🌍 Мова

§1.1 Спілкування, коментарі, звіти — **тільки українською**.

§1.2 Англійська дозволена для: назв технологій, API, бібліотек, ідентифікаторів, системних термінів.

§1.3 Код — англійськими назвами змінних/функцій.

§1.4 Не змішувати українську з російською. Російську не використовувати.

---

## §2 🎯 Призначення

Windows .NET 8 WinForms Hub для BDO і WWM. UI/API/detection/install/storage мають окремі boundaries.

Деталі: [§2](docs/ai-workflow/DOMAIN_RULES.md#section-2).

---

## §3 🧠 Принцип роботи

Перед змінами цієї області прочитай повний §3; зберігай наявні contracts та мінімальний scope.

Деталі: [§3](docs/ai-workflow/DOMAIN_RULES.md#section-3).

---

## §4 ⚖️ Пріоритети

Перед змінами цієї області прочитай повний §4; зберігай наявні contracts та мінімальний scope.

Деталі: [§4](docs/ai-workflow/DOMAIN_RULES.md#section-4).

---

## §5 💻 Код

Перед змінами цієї області прочитай повний §5; зберігай наявні contracts та мінімальний scope.

Деталі: [§5](docs/ai-workflow/DOMAIN_RULES.md#section-5).

---

## §6 🧩 Не ускладнювати

Перед змінами цієї області прочитай повний §6; зберігай наявні contracts та мінімальний scope.

Деталі: [§6](docs/ai-workflow/DOMAIN_RULES.md#section-6).

---

## §7 🏗️ Архітектура

Перед змінами цієї області прочитай повний §7; зберігай наявні contracts та мінімальний scope.

Деталі: [§7](docs/ai-workflow/DOMAIN_RULES.md#section-7).

---

## §8 🖥️ UI

Перед змінами цієї області прочитай повний §8; зберігай наявні contracts та мінімальний scope.

Деталі: [§8](docs/ai-workflow/DOMAIN_RULES.md#section-8).

---

## §9 ⚡ Не блокувати UI

Перед змінами цієї області прочитай повний §9; зберігай наявні contracts та мінімальний scope.

Деталі: [§9](docs/ai-workflow/DOMAIN_RULES.md#section-9).

---

## §10 🔌 API

Перед змінами цієї області прочитай повний §10; зберігай наявні contracts та мінімальний scope.

Деталі: [§10](docs/ai-workflow/DOMAIN_RULES.md#section-10).

---

## §11 📋 API Contract: bdo-ua.com.ua

Перед змінами цієї області прочитай повний §11; зберігай наявні contracts та мінімальний scope.

Деталі: [§11](docs/ai-workflow/DOMAIN_RULES.md#section-11).

---

## §12 🔐 Secrets

§12.1 Ніколи не додавати в репозиторій: API keys, tokens, passwords, signing secrets, credentials.

§12.2 Desktop `.exe` не може надійно приховати секрет.

---

## §13 🔍 Пошук гри

Перед змінами цієї області прочитай повний §13; зберігай наявні contracts та мінімальний scope.

Деталі: [§13](docs/ai-workflow/DOMAIN_RULES.md#section-13).

---

## §14 💾 Файли та backup

§14.1 Перед зміною/видаленням файлу знати, як його відновити.

§14.2 Бекап створювати один раз, не перезаписувати good backup модифікованим файлом.

§14.3 Атомарна установка: download у temp → validation → backup → apply → verify → cleanup.

§14.4 Temporary files: `.tmp`/`.download`, переміщати після перевірки.

§14.5 Перевірка hash (SHA-256) при наявності від сервера.

§14.6 **Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

§14.7 **Metadata original snapshot:** `created_at`, `game_patch` (якщо достовірно), `sha256` (локально, не з API), `size_bytes`.

§14.8 **Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback
4. `Відновити backup` — повернення до попереднього restore point

§14.9 Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

§14.10 Для WWM два managed targets містяться або в `Package/HD/oversea/locale/`, або в `LocalData/Patch/HD/oversea/locale/`. Перед першою Hub mutation зберігаються попереднє існування, exact bytes, size і SHA-256 кожного файла; state/snapshot/journal зв'язуються з exact installation root, вибраною layout і target paths. Кожна наступна mutation також має окрему rollback точку та durable journal. Rollback/restore повертає точні pre-operation/pre-Hub bytes або видаляє файл, якого раніше не існувало. Не називати це відновленням stock/original.

---

## §15 📦 API Release Metadata

API надає release metadata через `GET /releases`. Клієнт виконує валідовані операції.

§15.1 **Installation Safety Workflow:**
1. Для install/update отримати свіжий latest release вибраного режиму; не використовувати aggregate `current` як fallback при помилці перевірки.
2. Завантажити immutable `current.download_url` у cache/temp
3. Перевірити HTTP result
4. Перевірити `size_bytes` (якщо доступний)
5. Перевірити SHA-256 (для release files; для official source — ні)
6. Створити pre-operation snapshot/restore point
7. Підготувати заміну
8. Замінити game file
9. Перевірити встановлений файл
10. Записати installation state ТІЛЬКИ після успіху
11. Видалити temporary file
12. Commit success

§15.2 Якщо помилка на будь-якому кроці до replace — файл гри НЕ змінено, metadata НЕ стверджує, що release встановлено.

§15.3 Якщо помилка сталася після replace — клієнт повинен спробувати rollback до pre-operation snapshot. Якщо rollback не вдався:
- не заявляти успішну установку;
- стан вважати пошкодженим (`Corrupted`);
- показати користувачу критичну помилку;
- записати деталі в log.

§15.4 WWM install/update використовує окремий latest endpoint/client і два-file transaction; BDO aggregate/poller/installer schemas не змінюються. Fresh `data.modes[]` package за `slug + variant` проходить HTTPS, outer size/SHA і bounded ZIP allow-list перевірки у staging до mutation. Кожен staged target повторно перевіряється за size/SHA після запису. WWM target set змінився — операція fail-closed до окремого contract review.

§15.5 Для BDO `/releases` залишається агрегованим джерелом списку режимів, описів і progress, але не авторитетною заміною per-mode latest. Останній успішно перевірений `GET /releases/latest/{slug}` зберігається окремо та строго за точним slug; state встановленого режиму й install/update мають використовувати саме ці свіжі metadata. Після успішного latest `200/304` його `current.public_id` є авторитетною release identity; aggregate із застарілим `public_id` не може регресувати стан або пропонувати downgrade. Для встановленого API-режиму latest перевіряється при startup/state refresh та з обмеженою cadence при live feed refresh (не частіше одного разу на три хвилини для незмінного feed), а перед і після install/update. Якщо latest перевірка неуспішна, aggregate fallback заборонений і write action блокується. Якщо `304`-кешований latest суперечить aggregate `public_id` того самого slug, зробити одну безумовну `no-cache` revalidation без старого `If-None-Match`; при її помилці mutation блокується. Перед mutation `LocalizationInstallService` отримує один точний `CurrentRelease` із authoritative latest; download size/SHA та persisted mode/public_id/version/patch/SHA мають відповідати йому. `public_id` лишається release identity, numeric version — metadata. Логування selection/download/state має дозволяти зіставити mode, public_id, version, patch, filename, size, SHA та latest outcome без секретів.

---

## §16 🛡️ Захист шляхів

§16.1 Path traversal protection: нормалізація + перевірка, що файл в межах дозволеної директорії.

§16.2 Не довіряти filenames від API: не писати в system directories, не запускати executable.

---

## §17 ⬇️ Download

Перед змінами цієї області прочитай повний §17; зберігай наявні contracts та мінімальний scope.

Деталі: [§17](docs/ai-workflow/DOMAIN_RULES.md#section-17).

---

## §18 🔄 Стани та оновлення

Перед змінами цієї області прочитай повний §18; зберігай наявні contracts та мінімальний scope.

Деталі: [§18](docs/ai-workflow/DOMAIN_RULES.md#section-18).

---

## §19 🗑️ Відновлення та повернення до оригіналу

Перед змінами цієї області прочитай повний §19; зберігай наявні contracts та мінімальний scope.

Деталі: [§19](docs/ai-workflow/DOMAIN_RULES.md#section-19).

---

## §20 🪟 Windows

Перед змінами цієї області прочитай повний §20; зберігай наявні contracts та мінімальний scope.

Деталі: [§20](docs/ai-workflow/DOMAIN_RULES.md#section-20).

---

## §21 🎨 UX

Перед змінами цієї області прочитай повний §21; зберігай наявні contracts та мінімальний scope.

Деталі: [§21](docs/ai-workflow/DOMAIN_RULES.md#section-21).

---

## §22 📝 Логування та exceptions

Перед змінами цієї області прочитай повний §22; зберігай наявні contracts та мінімальний scope.

Деталі: [§22](docs/ai-workflow/DOMAIN_RULES.md#section-22).

---

## §23 ⚙️ Конфігурація та cache

Перед змінами цієї області прочитай повний §23; зберігай наявні contracts та мінімальний scope.

Деталі: [§23](docs/ai-workflow/DOMAIN_RULES.md#section-23).

---

## §24 🌐 Мережа

Перед змінами цієї області прочитай повний §24; зберігай наявні contracts та мінімальний scope.

Деталі: [§24](docs/ai-workflow/DOMAIN_RULES.md#section-24).

---

## §25 📚 Залежності

Перед змінами цієї області прочитай повний §25; зберігай наявні contracts та мінімальний scope.

Деталі: [§25](docs/ai-workflow/DOMAIN_RULES.md#section-25).

---

## §26 📦 Build та .exe

§26.1 Gates: `pwsh -File scripts/Invoke-ProjectValidation.ps1 -Profile Code`; лише суто docs — `-Profile Docs`. Scripts/CI/config потребують Code.

§26.3 CI є незалежною exact-SHA перевіркою; failure блокує залежні кроки й publication. Native preview/gh/auth — у повному контракті.

Деталі: [§26](docs/ai-workflow/DOMAIN_RULES.md#section-26).

---

## §27 🧪 Тести

Перед змінами цієї області прочитай повний §27; зберігай наявні contracts та мінімальний scope.

Деталі: [§27](docs/ai-workflow/DOMAIN_RULES.md#section-27).

---

## §28 🔒 Security

§28.1 Усе зовнішнє — недовірене: API responses, filenames, URLs, API release metadata.

§28.2 TLS verification ніколи не вимикати.

§28.3 Не запускати executable отриманий з сервера (окрім whitelist операцій: copy/replace localization file/create directory/restore backup/delete temporary/cache files). Основний `languagedata_en.loc` не видаляється фізично як uninstall. Повернення до стану без української виконується через `Restore Original`.

§28.4 Невідомі operation відхиляти.

---

## §29 🔗 Сумісність

Перед змінами цієї області прочитай повний §29; зберігай наявні contracts та мінімальний scope.

Деталі: [§29](docs/ai-workflow/DOMAIN_RULES.md#section-29).

---

## §30 ✏️ Правила змін

§30.1 Мінімальний patch. Не змішувати feature/refactor/formatting.

§30.2 Не чіпати непов'язаний код.

§30.3 Refactor не змінює поведінку ненавмисно.

§30.4 Не залишати TODO замість реалізації.

§30.5 Не підміняти реалізацію placeholder/mock.

§30.6 Не hardcode: шляхи, usernames, tokens, credentials.

---

## §31 📄 Коментарі та документація

Перед змінами цієї області прочитай повний §31; зберігай наявні contracts та мінімальний scope.

Деталі: [§31](docs/ai-workflow/DOMAIN_RULES.md#section-31).

---

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

`docs/plans/README.md` лишається продуктовим roadmap registry. Виконавчі плани окремих задач ведуться DevStandard у `docs/plans/active/execution/`; конкретний план стає executable лише після прямого схвалення Owner. §39.3 BACKLOG roadmap автоматично не виконується; PRIMARY рівно один за наявності ACTIVE.

Деталі: [§39](docs/ai-workflow/DOMAIN_RULES.md#section-39).

---

## §40 📦 Архів реліз-нотаток

Перед змінами цієї області прочитай повний §40; зберігай наявні contracts та мінімальний scope.

Деталі: [§40](docs/ai-workflow/DOMAIN_RULES.md#section-40).

---

## §41 🔄 Оновлення клієнта

До self-update/tray/notification/package змін прочитай повний контракт.

§41.9 Application update та localization operations (Install, Restore Original) взаємовиключні.

Деталі: [§41](docs/ai-workflow/DOMAIN_RULES.md#section-41).

---

## §42 🧭 Persistent development context

CURRENT і append-only monthly journal синхронізуються в implementation commit. Не публікувати приватні logs, user paths або raw source локальних skills.

Деталі: [§42](docs/ai-workflow/DOMAIN_RULES.md#section-42).

---

## §43 🤖 AI Development Workflow

Чинний режим **CODEX-OWNED**. Перед meaningful task прочитай `docs/ai-workflow/README.md` і `WORKFLOW.md`; для review — `REVIEW.md`.

§43.4 High-risk mutation/recovery/migration/updater потребують synthetic failure/rollback/restart evidence та окремого внутрішнього pre-commit review у Codex. BLOCKER/IMPORTANT блокують commit/release.

§43.9 Scoped commit/push дозволений після gates, якщо task не local-only/pre-commit. Потім перевірити CI точного SHA; не перепитувати чинний дозвіл.

§43.13 Task: `IMPLEMENTED → VALIDATED → INTERNAL REVIEW COMPLETE → COMPLETED`. Release — лише за Owner intent після exact-SHA CI, нового RC/integrity та live verification, з finalization. Зовнішній review/native smoke потрібні лише за explicit task requirement.

§43.15 Навички й triggers — `docs/ai-workflow/SKILLS.md`. Перед фінальною відповіддю прочитай `.agents/skills/report-format/assets/report-format.md`, якщо встановлено: це єдине джерело форми, не копіюй його в project docs. Приватні imports не є залежністю CI; mandatory project gates діють і без них.

Деталі: [§43](docs/ai-workflow/DOMAIN_RULES.md#section-43).

---

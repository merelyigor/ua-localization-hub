# UI — MainForm та BDO-тема

Один головний вікно (`MainForm`) з кастомною BDO-темою.

## Вікно

- **ClientSize:** 1040×720; minimum usable client width is dynamic: 960px for up to two modes and 820px for three or more modes, so three cards fit at their compact minimum width without weakening the single-mode global status layout.
- Стартова позиція — центр екрана
- Контент-driven висота: форма підганяє висоту в обидва боки до фактичного вмісту; висота обмежена мінімальним розміром і робочою областю екрана. Якщо вміст вищий за доступну область — вертикальний скрол (`rootScrollPanel`, `AutoScroll = true`). Підгонка запускається після зміни контенту та ширини, а зміна висоти користувачем не спричиняє повторної підгонки.
- Custom window chrome через `WindowChromeHelper` (внутрішній static class)

## Тема (UiTheme)

`UiTheme` (internal static) — централізовані кольори (`PrimaryText`, `SecondaryText`, `Accent`, `Background`, `SurfaceElevated` тощо), шрифти та масштабування (`UiTheme.Scale`). Дизайн задокументований у [docs/design/](design/BDO_THEME_PLAN.md).

Ключові кастомні компоненти:

| Компонент | Призначення |
|---|---|
| `BdoSurfacePanel` | Панель-«картка» з піднятою поверхнею (`SurfaceColor`) |
| `BdoProgressBar` | Кастомний progress bar із семантичними станами |
| `LocalizationModeCard` | Клікабельна картка режиму локалізації (мінімум 240×220) |

---

## Структура форми

Вертикальний стек через `TableLayoutPanel` (`mainLayoutPanel`), усередині `rootScrollPanel`:

```
┌──────────────────────────────────────────┐
│  Header: назва + utility panel           │
├──────────────────────────────────────────┤
│  Блок «Гра» (gameGroupBox)                │
├──────────────────────────────────────────┤
│  Блок «Локалізація» (modeGroupBox)       │
├──────────────────────────────────────────┤
│  Operation strip (operationStrip)        │  Visible лише під час операції
└──────────────────────────────────────────┘
```

## Header

- **headerTitleLabel** — «Хаб Українізаторів BDO - WWM», Segoe UI 20pt Bold.
- **headerSubtitleLabel** — «Українські локалізації для ігор BDO - WWM».
- Блок **«Цільові проєкти»** показує Black Desert Online / BDO UA Translate зі статусом «Доступно» та Where Winds Meet / Winds4UA (W4U) зі статусом «Інтеграція готується».
- **gameSelectorComboBox** — native `DropDownList` selector «Активна гра»; production catalog містить лише «Black Desert Online», тому selector вимкнений, доки не з'явиться друга реальна runtime session.
- **headerAccentLine** — акцентна лінія 2px.
- **rightUtilityPanel** (праворуч):
  - **updateButton** — «Оновити до vX.Y.Z». Прихований за замовчуванням; з'являється, коли знайдено candidate оновлення застосунку. Стани обчислюються через `UpdateButtonState.Compute(...)`; під час localization-операцій оновлення заблоковане (взаємовиключність, AGENTS §41.9).
  - **versionLabel** — поточна версія застосунку.
  - **uninstallHelpLink** — secondary link «Як видалити застосунок?», відкриває лише інформаційну підказку без змін EXE, AppData, реєстру чи файлів гри.
  - **logsButton** — іконка 32×32, tooltip «Відкрити папку журналів».

---

## 1. Блок «Гра» (`gameGroupBox`, BdoSurfacePanel)

Секційний заголовок **"BLACK DESERT"** (`gameSectionCaptionLabel`).

Елементи:
- **`gameStatusLabel`** — Segoe UI 11pt Bold. Зелений «✓ Гру знайдено» при успіху, сірий статус у інших випадках. Не зникає після успіху.
- **`gamePathLabel`** — шлях до гри, `AutoEllipsis`.
- **`detectGameButton`** — автоматичний пошук. Динамічний текст: «Знайти автоматично» → «Пошук...» під час пошуку → «Перевірити» після успіху.
- **`browseGameButton`** — **«Обрати папку»**, відкриває `FolderBrowserDialog`.
- **`restoreOriginalButton`** — **«Відновити оригінал»**, розташований у блоці гри. Активний лише коли локалізація встановлена.

### Детекція гри: UX-поведінка

**Стартовий пошук (StartupCoordinator):**

API запит та локальна детекція запускаються паралельно:
1. `gameStatusLabel` одразу показує «Пошук гри...»
2. Результати обробляються в порядку завершення (`Task.WhenAny`)
3. Локальна детекція (SavedConfig/Registry/Steam) показує результат одразу
4. Modes будуються незалежно від detection
5. Якщо локально не знайдено — «Локально гру не знайдено. Очікування даних сервера...»
6. API fallback виконується тільки коли обидва факти відомі: local NotFound + API success with patterns

### Degraded release feed

Якщо /releases недоступний, застосунок використовує валідний last-known feed з
%LocalAppData%\BDO-UA-Client\games\black-desert-online\cache\release-feed.json, якщо він є. Картки режимів і
локальний стан залишаються доступними для перегляду, а під operation strip показується
час збереження та повідомлення, що встановлення й оновлення очікують відновлення
з'єднання. Cached feed не використовується для API-assisted detection і не дозволяє
встановлення, оновлення, перемикання локалізації або Відновити оригінал.

Після успішного live poll кешовані дані замінюються authoritative live feed, попередження
зникає, а дозволи на mutation повертаються. Empty live feed також є authoritative і
замінює stale cards.

**Ручний вибір:**

- Невалідна/неоднозначна папка при наявності валідного `_gameRoot` — статус не змінюється, лише повідомлення.
- Manual parent→child resolution: `GameDetector.ResolveManualGameRoot` шукає гру в самій папці або серед дітей першого рівня; кілька збігів → «Знайдено кілька папок з грою. Оберіть точну папку гри.» Глибоке рекурсивне сканування не виконується.

---

## 2. Блок «Локалізація» (`modeGroupBox`)

Секційний заголовок **"Локалізація"** (`modeSectionCaptionLabel`).

### Картки режимів (`LocalizationModeCard`)

Режими **не захардкоджені** — будуються з `_apiResponse.Data.Modes` через `DynamicModePolicy.GetInstallableModes()`. Для кожного режиму створюється картка `LocalizationModeCard(mode)` у `modesFlowPanel`.

Картка містить:
- Назву режиму + інформацію про реліз (`DynamicModePolicy.FormatReleaseLine`)
- Графічні прапорці UA/GB через `LocalizationFlagParser`
- Exact installed badge «✓ Встановлено» — лише при exact match (ModeSlug + PublicId), див. `ModeCardPresentationPolicy`
- Всю площу картку клікабельна; вибір картки зберігає `LastMode` у `Config`

Презентація та вибір карток централізовані в `ModeCardPresentationPolicy` (internal static).

### Стан локалізації

Текст стану формується через `LocalizationStatePresentation.GetDisplayText(LocalizationStateResult)` — включно з patch-transition текстами (див. docs/states.md).

---

## 3. Operation strip (`operationStrip`)

`BdoSurfacePanel`, **прихований за замовчуванням**, з'являється лише під час активної операції. Елементи (рядок):

- **operationMessageLabel** — опис операції (wrap до 720px).
- **progressBar** (`BdoProgressBar`) — прогрес 0–100.
- **progressLabel** — відсоток або текстовий статус операції.
- **cancelButton** — «Скасувати»; активна лише під час операції, викликає `_operationCts.Cancel()`.

### OperationState → progressLabel

Див. таблицю в [states.md](states.md). Під час `Downloading` progressLabel оновлюється відсотком через `OnDownloadProgress`.

---

## 4. Операційний flow

### Guard: `_operationInProgress`

Блокує паралельні операції. Install / Restore Original / Update client — взаємовиключні.

### Під час операції

1. `BlockUpdates()` на feed-координаторі — feed-оновлення стають pending
2. Кнопки блоків гри та картки режимів вимикаються
3. `operationStrip` стає видимим, `cancelButton` активується

### Після завершення операції

1. `cancelButton.Enabled = false`, CTS dispose
2. `UnblockUpdates()` + `ApplyPendingIfAnyAsync()`
3. Оновлення всього UI (стан, картки, кнопки)
4. Фінальне повідомлення

### FormClosing safety

Якщо операція виконується — закриття скасовується (`e.Cancel = true`), запускається cancellation; повторне закриття можливе після завершення операції.

---

## 5. Helper mode UI (`UpdateApplyingForm`)

Малий діалог 480×178, який показується під час застосування self-update (`--apply-update <session-id>`): індикатор прогресу + текст. Після завершення показує результат (успіх — без повідомлення, помилки — MessageBox з українським описом, див. docs/update.md).

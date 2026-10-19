# Технічна документація Хабу Українізаторів BDO - WWM

## Навігація

| Документ | Опис |
|---|---|
| [architecture.md](architecture.md) | Архітектура проєкту, структура каталогів |
| [api.md](api.md) | API застосунку та моделі даних |
| [services.md](services.md) | Сервісний шар: детекція, встановлення, відновлення |
| [storage.md](storage.md) | Конфігурація, стан, бекапи |
| [ui.md](ui.md) | Користувацький інтерфейс |
| [states.md](states.md) | Моделі станів: LocalizationState, OperationState |
| [testing.md](testing.md) | Тестування |
| [update.md](update.md) | Self-update застосунку (GitHub Releases, Stage 13) |
| [build.md](build.md) | Збірка, CI та реліз |
| [Релізи](releases/README.md) | Процедура релізу та архів реліз-нотаток |
| [Дизайн](design/BDO_THEME_PLAN.md) | BDO-тема UI (план та кольори) |
| [Плани](plans/README.md) | Плани реалізації |
| [Development context](development/README.md) | Поточний engineering handoff та monthly journal |
| [AI workflow](ai-workflow/README.md) | CODEX-OWNED planning, implementation, internal review, automated gates та handoff |

## Поточний стан

- **Платформа:** Windows x64, .NET 8, WinForms
- **Пакування:** self-contained single-file (BDO-WWM-UAClient.exe); legacy bundle identity remains available for updates from published clients
- **Оновлення застосунку:** GitHub Releases `merelyigor/ua-localization-hub`, canonical ZIP transport (schema-2 manifest)
- **Тести та поточний engineering status:** [CURRENT.md](development/CURRENT.md), exact-SHA CI та результати harness
- **Релізні факти:** [архів релізів](releases/README.md); public state перевіряється live перед publication
- **Плани:** [canonical registry](plans/README.md); зовнішній архітектор не є обов'язковим gate

## Пов'язані документи

| Документ | Призначення |
|---|---|
| [AGENTS.md](../AGENTS.md) | Постійні правила та контракти проєкту |
| [Plans Registry](plans/README.md) | Реєстр активних, backlog та архівних планів |
| [Development context](development/README.md) | Persistent engineering context та журнал завершених задач |
| [Плани](plans/README.md) | Історичні та активні плани |

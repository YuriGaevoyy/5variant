# DeliveryService — УП.02, вариант 5 «Служба доставки»

Учебный сквозной проект: учёт отправлений службы доставки с жизненным циклом
**Создано → Принято на склад → В пути → Доставлено** и отдельным конечным статусом **Возврат**.
Решение на .NET 10 содержит шесть проектов: `Core` (сущности и правила), `Data` (EF Core + SQLite),
`Api` (ASP.NET Core REST, JWT, HTTPS), `Desktop` (WPF), `Tests` (xUnit) и `UiTests` (xUnit + FlaUI).

## Быстрый запуск

1. Установите .NET 10 SDK и Visual Studio 2026 с рабочими нагрузками ASP.NET и классических приложений .NET.
2. Откройте `DeliveryService.slnx`, выберите профиль **«Служба доставки»** и нажмите **F5**.
   Если профиль отсутствует, назначьте `DeliveryService.Desktop` запускаемым проектом.
   WPF-клиент сам поднимет локальный HTTPS API на loopback,
   применит миграции и создаст демоданные. Откроется окно входа.
3. Для отдельного запуска API: `dotnet dev-certs https --trust`, затем
   `dotnet user-secrets set "Jwt:Key" "<строка не короче 32 символов>" --project DeliveryService.Api`
   и `dotnet run --project DeliveryService.Api`. Swagger доступен в Development по `/swagger`.

Поиск обновляется по мере ввода, а выбранный этап применяется сразу. CSV, JSON и XML выгружают
текущую выборку отправлений. Для ручной перезагрузки списка нажмите «Обновить».

### Демо-аккаунты

| Логин | Пароль | Роль | Что видно |
|---|---|---|---|
| `admin` | `Admin@2026!` | администратор | все отправления, пользователи, статистика |
| `operator` | `Operator@2026!` | оператор | все отправления, получатели, курьеры, выгрузки |
| `courier1` | `Courier1@2026!` | курьер | 3 закреплённых отправления |
| `courier2` | `Courier2@2026!` | курьер | 2 закреплённых отправления |

Кнопка «Выйти» доступна всем ролям и возвращает к форме входа.

## Проверки

```bash
dotnet build DeliveryService.slnx -c Release
dotnet test DeliveryService.Tests/DeliveryService.Tests.csproj -c Release
dotnet test DeliveryService.UiTests/DeliveryService.UiTests.csproj -c Release
dotnet run -c Release --project tools/DeliveryService.Benchmarks/DeliveryService.Benchmarks.csproj
```

Фактические результаты: Release-сборка — 0 ошибок и 0 предупреждений; 64/64 модульных и интеграционных
теста; 8/8 UI-тестов; покрытие строк Core — 94,8 % (238/251); 26/26 проверок живого HTTPS API на чистой SQLite.
Подробности — в `docs/quality.md`, `docs/performance.md`, `docs/compatibility.md` и итоговом отчёте Word.

## Документация и материалы

- `docs/requirements.md` — требования, роли, статусы и бизнес-правила.
- `docs/architecture.md`, `docs/classes.drawio`, `docs/modules.drawio`, `docs/lifecycle.drawio` — проектирование.
- `docs/artifacts.md`, `docs/api-spec.md`, `requests.http` — протоколы и API.
- `docs/test-cases.md`, `docs/api-tests.md`, `docs/ui-checklist.md`, `docs/ui-scenarios.md` — тестирование.
- `docs/works-log.md` — список работ 1–29 с подтверждениями.
- `docs/user-guide.md` — руководство пользователя.

## Правила работы с ветками

Упрощённый Git Flow: `main` — выпуски, `develop` — интеграция, `feature/<N>-*` и `fix/<N>-*` — разработка, `release/<версия>` — подготовка выпуска. PR в `develop` содержит `Closes #N`.

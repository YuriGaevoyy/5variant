# Проверки API и прав доступа

УП.02, вариант 5, работы 3 и 4.

Проверки выполняются на отдельной чистой SQLite с демоданными. Файл `artifacts/requests.http` создаёт и изменяет записи, поэтому перед прогоном создаётся новая база.

```powershell
$env:DeliveryService__Database = Join-Path ([IO.Path]::GetTempPath()) ("delivery-http-" + [Guid]::NewGuid() + ".db")
$env:DeliveryService__SeedDemo = 'true'
$env:Security__LoginAttemptsPerMinute = '30'
dotnet dev-certs https --trust
dotnet run --project DeliveryService.Api --launch-profile Local
```

Для проверки лимита входов значение `Security__LoginAttemptsPerMinute` временно устанавливается в `5`. Запросы выполняются по порядку: ответы входа сохраняют JWT, созданные отправления — идентификатор и версию для следующих шагов. Swagger UI доступен по адресу `https://localhost:7130/swagger`.

Демо-учётные записи: `admin / Admin@2026!`, `operator / Operator@2026!`, `courier1 / Courier1@2026!`, `courier2 / Courier2@2026!`.

## Таблица проверок

| Код | Метод и endpoint | Роль | Ожидаемый код | Результат и наблюдение |
|---|---|---|---:|---|
| AT-01 | `GET /health` | без токена | 200 | `{"status":"ok"}`, пройдено |
| AT-02 | `POST /api/auth/login` (admin) | — | 200 | JWT с ролью `Admin`, пройдено |
| AT-03 | `POST /api/auth/login` (courier1) | — | 200 | JWT с ролью `Courier`, пройдено |
| AT-04 | `POST /api/auth/login` с неверным паролем | — | 401 | Сообщение о неверных данных, пройдено |
| AT-05 | `POST /api/auth/login` с пустым телом | — | 400 | `ValidationProblemDetails` для `login` и `password`, пройдено |
| AT-06 | Шестой вход подряд за минуту | — | 429 | Повтор разрешён через минуту, пройдено |
| AT-07 | `GET /api/shipments` без заголовка авторизации | — | 401 | Требуется вход, пройдено |
| AT-08 | `GET /api/shipments` | Admin | 200 | Возвращены все демо-отправления, пройдено |
| AT-09 | `GET /api/shipments` | Operator | 200 | Возвращены все демо-отправления, пройдено |
| AT-10 | `GET /api/shipments` | Courier (courier1) | 200 | Только отправления courier1, пройдено |
| AT-11 | `GET /api/shipments?status=InTransit` | Operator | 200 | Только статус `InTransit`, пройдено |
| AT-12 | `GET /api/shipments?status=Unknown` | Operator | 400 | Недопустимое значение фильтра, пройдено |
| AT-13 | `GET /api/shipments?search=Орлова` | Admin | 200 | Найдены отправления получателя, пройдено |
| AT-14 | `GET /api/shipments/1` | Courier (courier1) | 200 | Своя карточка открыта, пройдено |
| AT-15 | `GET /api/shipments/2` | Courier (courier1) | 404 | Чужое отправление не раскрывается, пройдено |
| AT-16 | `POST /api/shipments` с пустым `recipientId` | Operator | 400 | Обязательное поле не заполнено, пройдено |
| AT-17 | `POST /api/shipments` с корректными данными | Operator | 201 | Статус `Created`, номер `DS-0010`, пройдено |
| AT-18 | `POST /api/shipments` | Courier | 403 | Недостаточная роль, пройдено |
| AT-19 | `PUT /api/shipments/1` с устаревшей `version` | Operator | 409 | Предложено обновить карточку, пройдено |
| AT-20 | `PUT /api/shipments/1` | Courier | 403 | Редактирование запрещено, пройдено |
| AT-21 | `POST /api/shipments/3/transition` в `InTransit` без курьера | Operator | 400 | Статус сохранён, пройдено |
| AT-22 | `POST /api/shipments/4/transition` в `Delivered` без ФИО | Operator | 400 | Требуется подтверждение получения, пройдено |
| AT-23 | `POST /api/shipments/4/transition` в `Delivered` с суммой 0 | Operator | 400 | Сумма должна быть положительной, пройдено |
| AT-24 | `POST /api/shipments/5/transition` в `Returned` с причиной из 4 символов | Operator | 400 | Причина короче 5 символов, пройдено |
| AT-25 | `POST /api/shipments/5/transition` в `Returned` с причиной из 12 символов | Operator | 204 | Статус `Returned`, время в UTC, пройдено |
| AT-26 | `POST /api/shipments/2/transition` | Courier (courier1) | 404 | Чужое отправление недоступно, пройдено |
| AT-27 | `POST /api/shipments/1/transition` в `Created` из `Delivered` | Operator | 400 | Недопустимый переход, пройдено |
| AT-28 | `GET /api/recipients` | Courier | 403 | Справочник недоступен, пройдено |
| AT-29 | `POST /api/recipients` с корректными данными | Operator | 201 | Получатель добавлен, пройдено |
| AT-30 | `PUT /api/recipients/1` с устаревшей `version` | Admin | 409 | Конфликт версии, пройдено |
| AT-31 | `POST /api/recipients/import` с ошибками в строках 3 и 5 | Operator | 400 | `errors` с номерами строк, импорта нет, пройдено |
| AT-32 | `POST /api/recipients/import` файлом 2 МиБ | Operator | 413 | Превышен лимит размера, пройдено |
| AT-33 | `GET /api/couriers` | Operator | 200 | Два курьера для назначения, пройдено |
| AT-34 | `GET /api/couriers` | Courier | 403 | Недостаточная роль, пройдено |
| AT-35 | `GET /api/users` | Admin | 200 | Четыре учётные записи, пройдено |
| AT-36 | `GET /api/users` | Operator | 403 | Только администратор, пройдено |
| AT-37 | `GET /api/statistics` | Courier (courier2) | 200 | Показатели по отправлениям courier2, пройдено |
| AT-38 | `GET /api/statistics` | Operator | 200 | Показатели по всем отправлениям, пройдено |
| AT-39 | `GET /api/shipments/export?format=json` | Operator | 200 | Массив `ShipmentInfo`, пройдено |
| AT-40 | `GET /api/shipments/export?format=pdf` | Operator | 400 | Неподдерживаемый формат, пройдено |
| AT-41 | `GET /api/shipments/export?format=csv` | Courier | 403 | Экспорт недоступен, пройдено |
| AT-42 | `POST /api/auth/logout`, затем `GET /api/statistics` со старым токеном | Admin | 401 | Токен отозван, пройдено |
| AT-43 | `GET /health` после удаления таблицы отправлений | — | 503 | Сообщение о недоступности базы, пройдено |
| AT-44 | `GET /api/debug/requests` в Development | Admin | 200 | Счётчик запросов, пройдено |

## Группы проверок

Проверки AT-01–AT-06 подтверждают работу сеанса и ограничения входа. AT-07–AT-15 показывают, что аутентификация обязательна и что объектный уровень доступа для курьера реализован через 404. AT-16–AT-27 проверяют бизнес-правила: обязательные поля, версию, курьера для «В пути», сумму и ФИО для «Доставлено», причину для «Возврат» и запрет возврата к предыдущему этапу. AT-28–AT-36 покрывают справочники и роли. AT-37–AT-41 проверяют статистику и экспорт. AT-42–AT-44 завершают проверку отзыва токена, доступности базы и журнала запросов.

Код 400 в таблице означает как ошибку валидации, так и нарушение правила перехода; различие видно в поле `detail`. Код 403 означает запрет роли, код 404 — отсутствие объекта или чужое отправление курьера. Для всех изменений отправлений и получателей клиент передаёт `version`; расхождение даёт 409.

Дополнительные сценарии, включая граничные значения полей и импорт, приведены в [test-cases.md](test-cases.md).

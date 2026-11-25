# Спецификация REST API службы доставки

УП.02, вариант 5, работа 3, занятие 6.

## Общие соглашения

Базовый адрес при отдельном запуске — `https://localhost:7130` (профиль `Local`). При запуске из WPF сервер поднимается на свободном локальном порту. Все тела запросов и ответов используют JSON с именами полей в camelCase. Защищённые методы принимают заголовок `Authorization: Bearer <token>`.

Денежные поля — `decimal`, в JSON записываются числом с точкой (`12500.00`). Временные метки передаются в UTC по ISO 8601 (`2026-11-20T09:15:00Z`). Статусы в протоколе: `Created`, `Accepted`, `InTransit`, `Delivered`, `Returned`; им соответствуют русские подписи «Создано», «Принято на склад», «В пути», «Доставлено», «Возврат». Роли: `Admin`, `Operator`, `Courier`.

Ошибки возвращаются как `application/problem+json` с полями `title`, `status`, `detail`. Ошибки валидации дополняются словарём `errors` по именам полей, ошибки импорта — массивом строк с номерами `line`.Каждый ответ об ошибке безопасен для клиента: текст внутреннего исключения не передаётся.

Обозначения в матрицах: **А** — администратор, **О** — оператор, **К** — курьер.

## Сводная таблица методов

| Метод и маршрут | Доступ | Тело запроса | Успех | Основные отказы |
|---|---|---|---|---|
| `GET /health` | Все | — | 200, `{"status":"ok"}` | 503 |
| `POST /api/auth/login` | Без токена | `login`, `password` | 200, `token`, `expiresAt`, `user` | 400, 401, 429 |
| `POST /api/auth/logout` | А, О, К | — | 204 | 401 |
| `GET /api/shipments` | А, О, К | Параметры `status`, `search` | 200, массив отправлений | 400, 401 |
| `GET /api/shipments/{id}` | А, О, К | `id` в пути | 200, карточка | 401, 404 |
| `POST /api/shipments` | А, О | `ShipmentInput` | 201, `id`, `number` | 400, 401, 403 |
| `PUT /api/shipments/{id}` | А, О | `ShipmentUpdate` с `version` | 204 | 400, 401, 403, 404, 409 |
| `POST /api/shipments/{id}/transition` | А, О, К | `TransitionInput` | 204 | 400, 401, 404, 409 |
| `GET /api/recipients` | А, О | — | 200, массив получателей | 401, 403 |
| `POST /api/recipients` | А, О | `RecipientInput` | 201, `id` | 400, 401, 403 |
| `PUT /api/recipients/{id}` | А, О | `RecipientInput`, `version` | 204 | 400, 401, 403, 404, 409 |
| `POST /api/recipients/import` | А, О | `text/csv` | 200, `ImportResult` | 400, 401, 403, 413 |
| `GET /api/couriers` | А, О | — | 200, массив курьеров | 401, 403 |
| `GET /api/users` | А | — | 200, массив учётных записей | 401, 403 |
| `GET /api/statistics` | А, О, К | — | 200, `StatisticsInfo` | 401 |
| `GET /api/shipments/export?format=csv\|json\|xml` | А, О | `format`, `status`, `search` | 200, файл | 400, 401, 403 |
| `GET /api/debug/requests` | А (только Development) | — | 200, счётчик | 401, 403, 404 |

## Матрица прав по ролям

| Метод | Администратор | Оператор | Курьер |
|---|---:|---:|---:|
| `GET /health` | 200 | 200 | 200 |
| `POST /api/auth/login` | 200 | 200 | 200 |
| `POST /api/auth/logout` | 204 | 204 | 204 |
| `GET /api/shipments` | 200, все | 200, все | 200, только свои |
| `GET /api/shipments/{id}` | 200 | 200 | 200 своё, 404 чужое |
| `POST /api/shipments` | 201 | 201 | 403 |
| `PUT /api/shipments/{id}` | 204 | 204 | 403 |
| `POST /api/shipments/{id}/transition` | 204 | 204 | 204 своё, 404 чужое |
| `GET /api/recipients` | 200 | 200 | 403 |
| `POST /api/recipients` | 201 | 201 | 403 |
| `PUT /api/recipients/{id}` | 204 | 204 | 403 |
| `POST /api/recipients/import` | 200 | 200 | 403 |
| `GET /api/couriers` | 200 | 200 | 403 |
| `GET /api/users` | 200 | 403 | 403 |
| `GET /api/statistics` | 200, по всем | 200, по всем | 200, по своим |
| `GET /api/shipments/export` | 200 | 200 | 403 |
| `GET /api/debug/requests` | 200 | 403 | 403 |

Объектный уровень доступа для курьера проверяется в API после проверки роли, но до обращения к хранилищу. Курьер получает 404, а не 403, для чужой карточки и чужого перехода: это не раскрывает существование других отправлений. Список и статистика фильтруются по `courierId` текущего пользователя. Отсутствие заголовка `Authorization` всегда даёт 401, недостаточная роль — 403.

## Проверка здоровья и сеанс

### `GET /health`

Доступен без авторизации. Успешный ответ:

```json
{ "status": "ok", "checkedAt": "2026-11-23T10:00:00Z" }
```

Если таблицы базы недоступны, сервер возвращает 503 с `detail`: «База данных недоступна».

### `POST /api/auth/login`

```json
{ "login": "operator", "password": "Operator@2026!" }
```

Успех — 200:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-11-24T10:00:00Z",
  "user": { "id": 2, "login": "operator", "displayName": "Ольга Оператор", "role": "Operator" }
}
```

Коды: 200 — успех; 400 — не заполнены `login` или `password`; 401 — неверная пара; 429 — превышен лимит в пять попыток в минуту с одного адреса.

### `POST /api/auth/logout`

Тело не требуется. Успех — 204. Токен заносится в список отозванных на сервере. Повторный запрос с тем же токеном даёт 401 с сообщением «Сеанс завершён, выполните вход».

## Отправления

### `GET /api/shipments`

Необязательные параметры строки запроса: `status` (одно из `Created`, `Accepted`, `InTransit`, `Delivered`, `Returned`) и `search` (подстрока для поиска по номеру, получателю, телефону, адресу и курьеру). Параметры можно сочетать. Недопустимое значение `status` даёт 400.

Ответ 200 — массив `ShipmentInfo`:

```json
[
  {
    "id": 8,
    "number": "DS-0008",
    "recipientId": 3,
    "recipient": "Анна Орлова",
    "recipientPhone": "+79000001301",
    "address": "ул. Ленина, 12",
    "courierId": 5,
    "courier": "Иван Кузнецов",
    "declaredAmount": 12500.00,
    "status": "InTransit",
    "description": "Посылка",
    "returnReason": null,
    "deliveryConfirmation": null,
    "createdAt": "2026-11-20T09:15:00Z",
    "acceptedAt": "2026-11-20T11:40:00Z",
    "shippedAt": "2026-11-21T08:05:00Z",
    "deliveredAt": null,
    "returnedAt": null,
    "version": 3
  }
]
```

Для курьера массив содержит только отправления с его `courierId`.

### `GET /api/shipments/{id}`

Возвращает один `ShipmentInfo`, как в примере выше. Коды: 200, 401, 404. Чужое отправление курьера даёт 404.

### `POST /api/shipments`

```json
{
  "recipientId": 3,
  "address": "ул. Ленина, 12",
  "declaredAmount": 12500.00,
  "description": "Посылка",
  "courierId": null
}
```

Обязательны `recipientId` существующего получателя, `address` и `declaredAmount` больше нуля. Успех — 201:

```json
{ "id": 9, "number": "DS-0009", "status": "Created", "version": 1 }
```

Коды: 400 — некорректные поля или неизвестный получатель; 401; 403 — курьер.

### `PUT /api/shipments/{id}`

```json
{
  "recipientId": 3,
  "address": "ул. Ленина, 12",
  "declaredAmount": 12500.00,
  "description": "Посылка",
  "courierId": 5,
  "version": 2
}
```

Поле `version` обязательно. Успех — 204. Коды: 400 — ошибка валидации; 401; 403 — курьер; 404 — отправление отсутствует; 409 — версия устарела. Назначение `courierId` допускается только пользователем со значением `Courier`.

### `POST /api/shipments/{id}/transition`

```json
{
  "status": "Delivered",
  "reason": null,
  "deliveryConfirmation": "Петрова Мария Сергеевна",
  "deliveredAt": "2026-11-23T14:30:00Z",
  "version": 3
}
```

Правила сервера:

| Целевой статус | Обязательные данные запроса |
|---|---|
| `Accepted` | Последовательный переход из `Created`. |
| `InTransit` | Назначенный курьер в карточке отправления. |
| `Delivered` | `declaredAmount > 0`, непустое `deliveryConfirmation` (ФИО получателя), заполненное `deliveredAt`. |
| `Returned` | `reason` длиной не менее 5 символов. Разрешён из любого из четырёх основных статусов. |

Успех — 204, версия увеличивается. При нарушении условия — 400 с пояснением, статус не меняется. Устаревшая версия — 409. Чужая карточка курьера — 404.

Пример ошибки при переходе «В пути» без курьера:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "title": "Проверьте данные",
  "status": 400,
  "detail": "Перед переходом «В пути» назначьте курьера."
}
```

### `GET /api/shipments/export?format=`

Параметр `format` принимает значения `csv`, `json`, `xml` (регистронезависимо); допустимы также `status` и `search`. Доступен администратору и оператору. Успех — 200 с подходящим `Content-Type` и заголовком `Content-Disposition`. Недопустимый формат — 400, курьер — 403. Маршрут объявлен до `GET /api/shipments/{id}`, поэтому строка `export` не воспринимается как идентификатор. Форматы описаны в [artifacts.md](artifacts.md).

## Получатели

### `GET /api/recipients`

Ответ 200 — массив:

```json
[
  {
    "id": 3,
    "name": "Анна Орлова",
    "phone": "+79000001301",
    "address": "ул. Ленина, 12",
    "email": "anna@example.test"
  }
]
```

Курьеру доступен 403; данные получателя он видит в составе карточки отправления.

### `POST /api/recipients`

```json
{
  "name": "Игорь Соколов",
  "phone": "+79000001302",
  "address": "пр. Мира, 7",
  "email": "igor@example.test"
}
```

Успех — 201 с `id`. Обязательны `name`, `phone`, `address`; `email` необязателен, но заполненный проверяется. Коды: 400, 401, 403.

### `PUT /api/recipients/{id}`

Тело — те же поля плюс `version`. Успех — 204. Коды: 400, 401, 403, 404, 409.

### `POST /api/recipients/import`

Тело — `text/csv` в UTF-8 с заголовком `Name,Phone,Address,Email`. Успех — 200:

```json
{
  "total": 5,
  "imported": 5,
  "errors": []
}
```

При ошибках — 400 с массивом `errors`, содержащим `line` и `message`; сохранения нет. Файл больше 1 МиБ или с более чем 5 000 записей — 413. Подробности — в [artifacts.md](artifacts.md).

## Пользователи и статистика

### `GET /api/couriers`

Доступен администратору и оператору. Ответ 200:

```json
[
  { "id": 5, "login": "courier1", "displayName": "Иван Кузнецов" },
  { "id": 6, "login": "courier2", "displayName": "Павел Смирнов" }
]
```

### `GET /api/users`

Только администратор. Ответ 200:

```json
[
  { "id": 1, "login": "admin", "displayName": "Администратор", "role": "Admin" },
  { "id": 2, "login": "operator", "displayName": "Ольга Оператор", "role": "Operator" },
  { "id": 5, "login": "courier1", "displayName": "Иван Кузнецов", "role": "Courier" },
  { "id": 6, "login": "courier2", "displayName": "Павел Смирнов", "role": "Courier" }
]
```

Остальные роли получают 403.

### `GET /api/statistics`

Ответ 200 — не менее пяти показателей:

```json
{
  "total": 8,
  "byStatus": {
    "created": 1,
    "accepted": 2,
    "inTransit": 2,
    "delivered": 2,
    "returned": 1
  },
  "declaredAmountTotal": 78400.00,
  "declaredAmountAverage": 9800.00,
  "averageDeliveryDays": 2.4,
  "inTransitCount": 2,
  "deliveredCount": 2,
  "returnedCount": 1
}
```

`averageDeliveryDays` равен `null`, если доставленных отправлений нет. Для курьера все показатели рассчитываются только по его отправлениям.

## Журнал запросов

`GET /api/debug/requests` включён только в среде Development и только для администратора. Возвращает число обработанных запросов, что используется в проверке работоспособности журналирования. В среде Production маршрут возвращает 404.

## Коды ошибок

| Код | Причина | Что делает клиент |
|---:|---|---|
| 400 | Некорректное тело, фильтр, обязательное поле или нарушение правила перехода | Показывает `detail`, не меняет карточку |
| 401 | Нет токена, токен истёк или отозван, неверный логин или пароль | Возвращает к окну входа |
| 403 | Недостаточная роль | Показывает сообщение о запрете действия |
| 404 | Отправление отсутствует или принадлежит другому курьеру | Обновляет список |
| 409 | Устаревшая версия записи | Обновляет карточку и повторяет действие |
| 413 | CSV превышает установленный лимит | Уменьшает файл |
| 429 | Превышен лимит попыток входа | Повторяет вход через минуту |
| 500 | Внутренний сбой | Сообщает об ошибке, подробности только в журнале API |
| 503 | База недоступна для `GET /health` | Повторяет проверку позже |

Практические проверки этих кодов приведены в [api-tests.md](api-tests.md).

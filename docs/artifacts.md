# Артефакты и протоколы проекта

УП.02, вариант 5, работы 1–3.

В документе перечислены материальные результаты работ и способы взаимодействия между ними. Состав артефактов согласован с [архитектурой](architecture.md) и [спецификацией API](api-spec.md).

## Состав артефактов

| Артефакт | Расположение | Назначение |
|---|---|---|
| Решение `DeliveryService.slnx` | Корень рабочей папки | Объединяет шесть проектов .NET 10 |
| Исходный код Core, Data, Api, Desktop | Папки проектов | Реализация предметных правил, хранения, HTTP-сервера и WPF-клиента |
| Тесты `DeliveryService.Tests` | Папка проекта | Модульные проверки правил и интеграционные проверки API |
| Тесты `DeliveryService.UiTests` | Папка проекта | Автоматизация окон WPF через FlaUI |
| База SQLite и миграции | `DeliveryService.Data/Migrations/`, файл базы | Схема и начальные данные: не менее семи отправлений, два курьера |
| Демо-учётные записи | `Seed/DemoData.cs` | admin, operator, courier1, courier2 с паролями из руководства |
| Документы проекта | `docs/` | Требования, архитектура, спецификация и проверки API, тест-кейсы, чек-лист, руководство |
| Файл HTTP-запросов | `artifacts/requests.http` | Пошаговые проверки API на чистой базе |
| Пример CSV получателей | `artifacts/recipients-import.csv` | Корректный файл импорта |
| Пример CSV с ошибками | `artifacts/recipients-wrong.csv` | Проверка сообщений о номерах строк |
| Диаграммы состояний, модулей и классов | `artifacts/` (Draw.io / PNG) | Наглядное представление жизненного цикла и структуры |
| Журналы проверок и измерений | `artifacts/`, `output/` | Сводка тестов, покрытие, результаты Release-сборки |
| Автономный ZIP для Windows x64 | `output/` | Распространение готового решения вместе с базой и зависимостями |
| Итоговый отчёт с дневником | Корень рабочей папки | Сводка выполненных работ 1–29 |

## Протоколы и форматы

| Протокол или формат | Применение | Ключевые соглашения |
|---|---|---|
| HTTPS + REST | Обмен Desktop и API | JSON, camelCase, заголовок `Authorization: Bearer <token>` |
| JWT Bearer | Аутентификация и роли | Роли `Admin`, `Operator`, `Courier`; отзыв на сервере при выходе |
| `application/json` | Тела запросов и ответов | Числа денежных полей с точкой, даты в ISO 8601 UTC |
| `application/problem+json` | Ошибки | Поля `title`, `status`, `detail`; для импорта добавляется `errors` |
| `text/csv` | Импорт получателей | UTF-8, заголовок `Name,Phone,Address,Email` |
| `text/csv`, `application/json`, `application/xml` | Экспорт отправлений | Фильтры `status` и `search` сохраняются в выгрузке |
| SQLite | Хранение данных | EF Core, миграции, оптимистическая блокировка через `version` |

## Методы API

Список включает все маршруты, используемые клиентом и тестами.

| Метод и маршрут | Назначение | Доступ |
|---|---|---|
| `GET /health` | Проверка доступности сервера и базы | Без авторизации |
| `POST /api/auth/login` | Вход, выдача JWT | Без авторизации |
| `POST /api/auth/logout` | Отзыв токена | Все роли |
| `GET /api/shipments` | Журнал с фильтром `status` и поиском `search` | Admin, Operator, Courier (свои) |
| `GET /api/shipments/{id}` | Карточка отправления | Admin, Operator, Courier (своё) |
| `POST /api/shipments` | Создание отправления | Admin, Operator |
| `PUT /api/shipments/{id}` | Редактирование и назначение курьера | Admin, Operator |
| `POST /api/shipments/{id}/transition` | Смена статуса | Admin, Operator, Courier (своё) |
| `GET /api/recipients` | Справочник получателей | Admin, Operator |
| `POST /api/recipients` | Создание получателя | Admin, Operator |
| `PUT /api/recipients/{id}` | Редактирование получателя | Admin, Operator |
| `POST /api/recipients/import` | Импорт получателей из CSV | Admin, Operator |
| `GET /api/couriers` | Список курьеров для назначения | Admin, Operator |
| `GET /api/users` | Список учётных записей | Admin |
| `GET /api/statistics` | Показатели по отправлениям | Admin, Operator, Courier (свои) |
| `GET /api/shipments/export?format=csv\|json\|xml` | Выгрузка отправлений | Admin, Operator |
| `GET /api/debug/requests` | Счётчик запросов в Development | Admin |

## Пример запроса

Оператор переводит отправление № 8 в статус «В пути», назначив курьера ранее.

```http
POST /api/shipments/8/transition HTTP/1.1
Host: localhost:7130
Content-Type: application/json
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

{
  "status": "InTransit",
  "reason": null,
  "deliveryConfirmation": null,
  "deliveredAt": null,
  "version": 2
}
```

Успешный ответ — `204 No Content`, заголовок `ETag` содержит новую версию.

## Пример ошибки

Тот же запрос без назначенного курьера отклоняется правилом предметной области:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "title": "Проверьте данные",
  "status": 400,
  "detail": "Перед переходом «В пути» назначьте курьера."
}
```

Клиент показывает `detail` пользователю и оставляет карточку в прежнем статусе.

## Импорт получателей из CSV

Файл передаётся в `POST /api/recipients/import` с типом `text/csv` и кодировкой UTF-8 (BOM допустим). Первая строка — заголовок `Name,Phone,Address,Email`. Разделитель — запятая; кавычки внутри поля удваиваются. Имена колонок сравниваются без учёта регистра.

```csv
Name,Phone,Address,Email
Анна Орлова,+79000001301,ул. Ленина, 12,anna@example.test
Игорь Соколов,+79000001302,пр. Мира, 7,
```

Обязательные поля — `Name`, `Phone`, `Address`; `Email` может быть пустым, а заполненный адрес проверяется на формат. Ограничение файла — 1 МиБ и 5 000 записей. Превышение размера даёт 413.

Сохранение выполняется только после проверки всего файла. Если найдены ошибки, API возвращает 400 и перечисляет номера строк:

```json
{
  "title": "Ошибки импорта",
  "status": 400,
  "detail": "Исправьте помеченные строки и повторите загрузку.",
  "errors": [
    { "line": 2, "message": "Не указан адрес." },
    { "line": 4, "message": "Некорректный адрес электронной почты." }
  ]
}
```

Нумерация строк начинается с 1 и включает заголовок, поэтому первая запись данных имеет номер 2. Частичного импорта не происходит.

## Экспорт отправлений

Выгрузка выполняется методом `GET /api/shipments/export?format=csv` (значения `json` и `xml` используются аналогично). К запросу применяются те же фильтры `status` и `search`, что и к журналу.

**CSV.** Кодировка UTF-8 с BOM, разделитель — точка с запятой, значения заключаются в кавычки, внутренние кавычки удваиваются.

```text
Id;Number;RecipientId;Recipient;Phone;Address;CourierId;Courier;Status;DeclaredAmount;Description;ReturnReason;DeliveryConfirmation;CreatedAt;AcceptedAt;ShippedAt;DeliveredAt;ReturnedAt;Version
8;DS-0008;3;Анна Орлова;+79000001301;ул. Ленина, 12;5;Иван Кузнецов;InTransit;12500.00;Посылка;"";"";2026-11-20T09:15:00Z;2026-11-20T11:40:00Z;2026-11-21T08:05:00Z;;;;3
```

**JSON.** Массив объектов `ShipmentInfo` с теми же полями в camelCase; даты в UTC, сумма — числом.

```json
[
  {
    "id": 8,
    "number": "DS-0008",
    "recipient": "Анна Орлова",
    "address": "ул. Ленина, 12",
    "courier": "Иван Кузнецов",
    "status": "InTransit",
    "declaredAmount": 12500.00,
    "createdAt": "2026-11-20T09:15:00Z",
    "version": 3
  }
]
```

**XML.** Корневой элемент `Shipments`, дочерние элементы `Shipment`; названия элементов совпадают с именами полей DTO.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Shipments>
  <Shipment>
    <Id>8</Id>
    <Number>DS-0008</Number>
    <Status>InTransit</Status>
    <DeclaredAmount>12500.00</DeclaredAmount>
    <CreatedAt>2026-11-20T09:15:00Z</CreatedAt>
    <Version>3</Version>
  </Shipment>
</Shipments>
```

Все три формата формируются из одной выборки `Core`, поэтому идентификаторы, статусы и временные метки совпадают. Проверки экспорта описаны в [api-tests.md](api-tests.md) и [test-cases.md](test-cases.md).

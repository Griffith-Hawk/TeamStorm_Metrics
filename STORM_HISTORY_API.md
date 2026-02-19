# Storm History API — документация

Источник: логика из `example.py`, структура ответа Storm.

## Эндпоинт

```
GET https://storm.alabuga.space/history/api/v1/workspaces/{workspaceId}/workItems/{workitemId}/history
```

**Аутентификация:** Cookie `session` (STORM_SESSION_TOKEN). API-токен может не работать для History.

## Ответ

Массив событий изменения задачи. Каждое событие:

| Поле   | Тип   | Описание                                    |
|--------|-------|---------------------------------------------|
| `date` | string| ISO 8601 с Z (UTC), напр. `2026-02-02T13:48:55.569194Z` |
| `type` | string| Тип события, напр. `StatusUpdated`          |
| `data` | object| Данные изменения                            |

### Событие смены статуса (StatusUpdated)

Для расчёта «in progress» используются только события `type === "StatusUpdated"`.

Структура `data`:
```json
{
  "newValue": {
    "statusName": "IN PROGRESS"
  }
}
```

- `data.newValue.statusName` — новый статус (сравнивать без учёта регистра: `"in progress"`).
- Альтернативные поля: `data.newValue.name` или строка в `data.newValue`.

## Расчёт времени в статусе "in progress"

1. Фильтр: `type === "StatusUpdated"` (или `WorkitemStatusUpdated`).
2. Сортировка событий по `date`.
3. Для каждого отрезка, когда статус был "in progress", считать пересечение с рабочими окнами:
   - Пн–Пт, 08:00–17:00 МСК
   - Выходные не учитываются
4. Суммировать минуты.

## Варианты формата ответа

- Прямой массив: `[{...}, {...}]`
- Обёртка: `{ "items": [...] }`, `{ "data": [...] }`, `{ "entries": [...] }`
- Пагинация: `nextToken`, `nextPageToken`, `continuationToken` — доп. страницы с `?fromToken=...`

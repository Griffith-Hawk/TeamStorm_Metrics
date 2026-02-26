# TeamStorm Metrics (.NET)

ASP.NET Core MVC приложение для аналитики TeamStorm с расчётом:
- **TTL** по задачам/участникам,
- **Velocity** по ролям:
  - Dev: `Selected -> Ready to Test`
  - QA: `Ready to test -> Acceptance`
  - Analyst: `Open -> Handoff`
- **Capacity** (по оценкам задач),
- рисков и рекомендаций для спринтов (активный / не запущен / завершён).

## Что есть в UI
1. Главная страница — список workspace.
2. Страница проекта — агрегированная статистика по всем спринтам проекта + графики.
3. Страница спринта — детальная аналитика по каждому участнику (TTL/Velocity/Capacity), риски и рекомендации.

## Безопасное кеширование
- Аналитика кешируется в `App_Data/cache`.
- Кеш хранится **в зашифрованном виде** (AES-GCM).
- Ключ можно задать в `appsettings.json` как `Storm:CacheEncryptionKey` (base64 32-byte).

## Конфигурация
```json
"Storm": {
  "BaseUrl": "https://storm.alabuga.space",
  "ApiToken": "...",
  "SessionToken": "...",
  "CacheEncryptionKey": "<base64-32-byte-key-optional>"
}
```

## Запуск
```bash
dotnet restore
dotnet run
```

## API
- `GET /api/workspaces`
- `GET /api/workspaces/{workspaceId}/analytics`
- `GET /api/workspaces/{workspaceId}/folders/{folderId}/sprints/{sprintId}/analytics`
- `GET /api/workspaces/{workspaceId}/folders`
- `GET /api/workspaces/{workspaceId}/sprints`
- `GET /api/workspaces/{workspaceId}/folders/{folderId}/workitems`
- `POST /api/workspaces/{workspaceId}/workitems/fact`
- `GET /api/workspaces/{workspaceId}/workitems/{workitemId}/history/ready-to-test`
- `PATCH|PUT /api/workspaces/{workspaceId}/workitems/{workitemId}`

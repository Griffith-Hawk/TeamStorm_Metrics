# TeamStorm Metrics (.NET)

Проект перенесён на **ASP.NET Core MVC (C#)** с сохранением основного функционала прошлого приложения:
- выгрузка Workspace,
- переход на отдельную страницу проекта,
- выбор папки/спринта,
- таблица задач,
- расчёт метрик `Fact`, `Fact QA`, `Handoff QA`,
- просмотр истории пары `ready to test -> testing`.

## Структура
- `Controllers/` — web/API контроллеры.
- `Services/` — интеграция со Storm API и бизнес-логика метрик.
- `Models/` — request/response модели.
- `Views/` + `wwwroot/` — HTML/CSS/JS.
- `Options/` — конфигурация токенов и base url.

## Конфигурация
`appsettings.json`:
```json
"Storm": {
  "BaseUrl": "https://storm.alabuga.space",
  "ApiToken": "...",
  "SessionToken": "..."
}
```

## Запуск
```bash
dotnet restore
dotnet run
```

## Основные API
- `GET /api/workspaces`
- `GET /api/workspaces/{workspaceId}/folders`
- `GET /api/workspaces/{workspaceId}/sprints?folderId=...`
- `GET /api/workspaces/{workspaceId}/folders/{folderId}/workitems`
- `POST /api/workspaces/{workspaceId}/workitems/fact`
- `GET /api/workspaces/{workspaceId}/workitems/{workitemId}/history/ready-to-test`
- `PATCH|PUT /api/workspaces/{workspaceId}/workitems/{workitemId}`

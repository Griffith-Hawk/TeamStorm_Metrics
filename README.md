# TeamStorm Metrics (.NET)

Проект полностью мигрирован на **ASP.NET Core MVC (C#)**.

## Архитектура

- `Controllers/` — MVC и API контроллеры.
- `Services/` — доступ к Storm API и вычисление метрик (бизнес-логика).
- `Models/` — DTO для данных Storm.
- `Views/` + `wwwroot/` — Web UI (HTML + CSS + JS).
- `Options/` — конфигурация внешнего API.

## Быстрый старт

1. Установить .NET SDK 8.0+
2. Заполнить `appsettings.json` (секция `Storm`):
   - `ApiToken` (рекомендуется)
   - или `SessionToken`
3. Запустить:

```bash
dotnet restore
dotnet run
```

## API

- `GET /api/health`
- `GET /api/workspaces`
- `GET /api/workspaces/{workspaceId}/folders`
- `GET /api/workspaces/{workspaceId}/folders/{folderId}/workitems`
- `POST /api/workspaces/{workspaceId}/workitems/fact`

`fact` рассчитывается в C# сервисе по истории задач, учитывая рабочее время (08:00–17:00 МСК, Пн–Пт).

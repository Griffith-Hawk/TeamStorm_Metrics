# Обновление estimatedTime в Storm

## Поле «Время» в задаче

В Storm в карточке задачи поле **«Время»** (оценка) соответствует полю `originalEstimate` в API. Значение хранится в **секундах**.

## Как узнать правильный API

Документация Storm API по обновлению workitem ограничена. Чтобы найти точный endpoint:

1. Откройте Storm в браузере (Chrome/Firefox).
2. Откройте DevTools → вкладка **Network** (Сеть).
3. Измените значение поля «Время» в любой задаче и сохраните.
4. Найдите XHR/Fetch запрос, который ушёл при сохранении.
5. Скопируйте URL, метод (PATCH/PUT/POST) и тело запроса.

Пришлите эти данные — можно будет настроить правильный вызов.

## Что мы пробуем сейчас

- **Методы:** PATCH, PUT
- **Пути:**
  - `/workspaces/{id}/workitems/{id}`
  - `/workspaces/{id}/nodes/{id}`
  - `/workspaces/{id}/folders/{folderId}/workitems/{id}` (если передан folderId)
- **Тело:** `{ "originalEstimate": <секунды> }`

## Swagger Storm

Если в Storm есть Swagger/OpenAPI (например, `/swagger`, `/api/docs`), проверьте в нём endpoint обновления workitem/node.

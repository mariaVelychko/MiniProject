# Клієнт SportClub (ЛР4)

Веб-застосунок на HTML + JavaScript (ES-модулі), **без збірки й залежностей**. Працює лише через шлюз (`config.js`).

## Запуск

* Разом з усім стеком у Docker: `docker compose up --build` → **http://localhost:8080**
* Без Docker (потрібен Node.js): `node client/dev-server.mjs` → **http://localhost:3000** (шлюз має бути на `:5000`)
* Інша адреса шлюзу: змінити `apiBaseUrl` у `config.js` (в Docker — змінна `API_BASE_URL`).

Демо-дані (клієнт для входу + тренування): `powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1`

## Тести клієнтського шару

`cd client && npm test` (або `node --test`) — 15 тестів з підміненим HTTP: 401 і одне оновлення токена, 429 і пауза,
розбір помилок валідації, таймаут, Idempotency-Key.

## Структура

```
index.html, config.js, css/app.css
js/api/      errors.js · tokens.js · http.js (єдиний шар API) · services.js (методи API)
js/idempotency.js
js/screens/  login · catalog · details · book · bookings
js/ui/       dom · components (скелетони, помилка, порожньо, «Повторити») · toast · theme · format
tests/       api.test.mjs
```

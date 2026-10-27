# ЛР3 — показ у Postman (≈7 хв) і перевірка викладача

База: `http://localhost:5000` (змінну порта не використовуємо). Усі сервіси запущені (`start-all.ps1` або `docker compose up --build`).

## 0. Підготовка (1 раз)
1. `POST /api/v1/auth/login` → тіло `{"email":"admin@miniproject.local","password":"Admin123!"}`.
   Скопіюйте `accessToken` → у колекції вкладка **Authorization → Bearer Token**.
2. `GET /api/v1/catalog/items` → запам'ятайте `id` позиції (далі `{id}`).

## 1. Складений запит
| Запит | Очікується |
|---|---|
| `GET /api/v1/screens/training/{id}` з токеном | `200`, у тілі `item`, `sessions`, `me`, `partial:false`, `unavailable:[]` |
| те саме **без токена** | `401`, `application/problem+json` |
| `GET /api/v1/screens/training/99999` | `404` ProblemDetails |

## 2. Часткова відповідь і таймаути
1. Зупиніть ScheduleService (Ctrl+C у його вікні / `docker compose stop schedule`).
2. Повторіть запит екрана → `200`, `partial:true`, `unavailable:["sessions"]`, `sessions:null`, заголовок `X-Partial-Response: true`; `item` і `me` на місці.
3. Зупиніть Catalog → той самий запит → `503` ProblemDetails (ядро недоступне). Запустіть сервіси назад.
4. `GET /health` → `200 {"status":"ok"}`; при вимкненому сервісі — `503`, `dependencies` показує, який.

## 3. Автентифікація
1. `GET /api/v1/users/me` без токена → `401` ProblemDetails; з токеном → `200`.
2. Токен із зіпсованим символом / прострочений → `401`, до сервісу запит не доходить.
3. `GET /api/v1/catalog/items` без токена → `200` (читання відкрите, поведінка не змінилась); `PUT /api/v1/catalog/items/{id}` без токена → `401`.
4. У Docker: `http://localhost:5001/health` (Auth напряму) **не відповідає** — порти закриті; з `docker-compose.debug.yml` відповідає.

## 4. Кеш
1. `GET /api/v1/catalog/items/{id}` двічі → у відповідях заголовок `X-Cache`: `MISS`, потім `HIT`.
2. `GET /gateway/cache` → `hits`, `misses`, `hitRatio`.
3. `PUT /api/v1/catalog/items/{id}` (з токеном Admin, змінити назву) → `2xx`; знову `GET` → `X-Cache: MISS` і **нова** назва (інвалідизація). `invalidations` у статистиці зріс.
4. `GET /api/v1/users/me` — заголовка `X-Cache` немає (персональне не кешується).

## 5. Обмеження частоти
1. У Postman Runner: `POST /api/v1/auth/login` з хибним паролем 6 разів поспіль → 6-й `429`, заголовок `Retry-After`, ProblemDetails з `retryAfterSeconds`.
2. Через хвилину вхід знову працює.
3. Анонімні `GET /api/v1/catalog/items` — 101-й за хвилину → `429` (Runner, 110 ітерацій).

## 6. Документація
Swagger: `http://localhost:5000/swagger` → є `GET /api/v1/screens/training/{itemId}` із прикладами, кодами `200/401/404/429/503` і кнопкою **Authorize** (Bearer).

## 7. Тести
```powershell
dotnet test tests\Gateway.IntegrationTests
```
Очікується: усі зелені (≈28), без запущених сервісів і БД — тести самі піднімають фейковий бекенд.

---

# Алгоритм для викладача (відповідно до методички)
1. **Складений запит** (розд. 3.1): відкрити `TrainingScreen.cs` → `Task.WhenAll`; тест `Screen_CallsServicesInParallel_NotOneByOne`.
2. **Часткова відповідь**: тести `Screen_ScheduleDown_…`, `Screen_AuthDown_…`, `Screen_NoSessions_IsEmptyList_NotUnavailable`; демо §2.
3. **Таймаути на всіх викликах**: `Clients.cs` (2 с), `ProxyTimeoutMiddleware.cs` (5 с), health (1 с); тести `Screen_SlowService_…`, `Proxy_ServiceTooSlow_…`.
4. **JWT на шлюзі + X-User-Id/X-User-Roles**: `GatewayAuthentication.cs`, `IdentityHeadersMiddleware.cs`; тести `Screen_InvalidToken_…`, `UserIdHeader_FromClient_IsIgnored_…`.
5. **Закриті порти**: `docker-compose.yml` (лише `gateway` і `mysql` мають `ports:`), `docker-compose.debug.yml`; демо §3.4.
6. **Кеш лише спільних даних + інвалідизація**: `CatalogCache.cs`; тести `Catalog_*`, `Screen_CachesSharedCatalogData_ButNeverPersonalData`, `PersonalRoutes_AreNotCached`.
7. **Ліміти (429 + Retry-After + ProblemDetails)**: `RateLimiting.cs`; тести `RateLimit_*`.
8. **ProblemDetails для всіх помилок шлюзу**: `Problems.cs`, `GlobalExceptionHandler.cs`.
9. **OpenAPI**: `GatewaySwagger.cs`; тест `Swagger_DescribesCompositeEndpoint_WithBearerScheme`.
10. **Документи**: `docs/adr/0004-…md`, `docs/diagrams/05-composition-lr3.md`, `docs/lab3-questions.md`.
11. **Існуючі маршрути не змінились**: тест `PublicRoutes_StayAnonymous_…`, `ServiceOwnErrors_PassThroughUnchanged_…`; baseline ЛР2 (`run-scenarios.ps1`) — 0 розбіжностей.
12. **Секретів у репозиторії немає**: `Jwt:Key` порожній у `appsettings.json`; береться з `Jwt__Key` / `.env` (у `.gitignore`).

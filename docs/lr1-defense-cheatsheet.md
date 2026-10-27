# Шпаргалка для захисту ЛР1 (Auth + Catalog)

## 0. Запуск за 1 хвилину

1. Запусти MySQL (перевір, що Workbench підключається).
2. У PowerShell у корені проєкту:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\start-all.ps1
   ```
   Скрипт спитає пароль MySQL **один раз**, збере рішення й запустить 4 вікна (Auth → Catalog → Monolith → Gateway),
   чекаючи на `/health` кожного. Після цього відкриються Swagger-вкладки.
3. Якщо щось «зависло» через старі напівстворені бази:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\start-all.ps1 -ResetDb
   ```
   (видаляє й створює наново `miniproject_auth`, `miniproject_catalog`, `sportclub_cs`).

Облікові дані за замовчуванням:

| Де | Логін | Пароль |
|---|---|---|
| AuthService (роль Admin) | `admin@miniproject.local` | `Admin123!` |
| Моноліт / WPF | `admin` | `admin` |

## 1. Порти

| Застосунок | Порт | Swagger |
|---|---|---|
| ApiGateway | 5000 | — |
| AuthService | 5001 | http://localhost:5001/swagger |
| CatalogService | 5002 | http://localhost:5002/swagger |
| Monolith | 5100 | http://localhost:5100/swagger |

## 2. Сценарій показу (відповідає розділу 8 методички)

**Усі запити — через шлюз `http://localhost:5000`** (у Swagger можна, але для п. 9 покажи шлюз у curl/Postman).

1. `GET /health` на 5001, 5002, 5100, 5000 → 200.
2. `POST /api/v1/auth/register` → 201; те саме ще раз → **409**; пароль `123` → **400** з переліком полів.
3. MySQL Workbench: `SELECT Id, Email, PasswordHash FROM miniproject_auth.users;` → пароль збережено як хеш BCrypt (`$2a$...`).
4. `POST /api/v1/auth/login` → 200 і пара токенів; неправильний пароль → **401**.
5. Встав `accessToken` на https://jwt.io → покажи claims (`sub`, `email`, `role`, `exp`).
6. `GET /api/v1/users/me` без токена → **401**; з токеном → 200.
7. `GET /api/v1/catalog/items?page=1&pageSize=5&search=йога&sortBy=price` → сторінка + `totalCount`.
   Некоректне: `page=0` або `pageSize=10000` → **400**.
8. `POST /api/v1/catalog/items`: без токена → **401**; токен звичайного User → **403**; токен Admin → **201**.
9. Логи: запит до каталогу — у вікні **CatalogService** (`[CatalogService] GET /api/v1/catalog/items -> 200`),
   запит до `/api/workouts` чи `/api/auth/login` — у вікні **Monolith**. У вікні **ApiGateway** кожен рядок
   показує цільовий адрес (`-> http://localhost:5002/...` проти `-> http://localhost:5100/...`).
10. Refresh: `POST /api/v1/auth/refresh` → нова пара; старий refresh вдруге → **401**.
    `POST /api/v1/auth/logout` → **204**; refresh після logout → **401**.
    (Щоб показати спливання access-токена, тимчасово постав `Jwt:AccessTokenMinutes` = 1.)

Додатково (контракт з методички):
- `PUT /api/v1/catalog/items/{id}` → **204**; `DELETE` → **204** (м'яке видалення: позиція зникає зі списку,
  але в БД лишається з `IsActive = 0`).
- `GET /api/v1/users` та `PUT /api/v1/users/{id}/roles` — лише з токеном Admin.

## 3. Моноліт: що показати

Моноліт працює без змін у поведінці — через шлюз старі маршрути йдуть на нього:

- `POST http://localhost:5000/api/auth/login` `{ "email": "admin", "password": "admin" }` → 200
- `GET  http://localhost:5000/api/workouts` → список тренувань

Запуск окремо: `dotnet run --project src\Monolith` (порт 5100, Swagger відкриється сам).
Монолітові дані лежать у MySQL (`sportclub_cs`), а не в JSON.

## 4. Якщо щось не запускається

| Симптом у вікні сервісу | Причина | Що зробити |
|---|---|---|
| `Access denied for user 'root'` | неправильний пароль MySQL | запусти `start-all.ps1` і введи правильний пароль |
| `Unable to connect ... 3306` | MySQL не запущено | запусти службу MySQL |
| `Table '...' doesn't exist` / `Invalid default value` | база лишилась від невдалої спроби | `start-all.ps1 -ResetDb` |
| `Address already in use` | старий екземпляр ще працює | закрий старі вікна / `Get-Process dotnet \| Stop-Process` |
| `dotnet build` падає з "file is locked" | запущений старий процес | закрий усі вікна сервісів і WPF |
| Catalog відповідає 401 на токен від Auth | різний `Jwt__Key` | запускай обидва через `start-all.ps1` (він ставить однаковий ключ) |

## 5. EF-міграції (для повної оцінки)

Сервіси стартують через `EnsureCreated`, але методичка просить справжні міграції. Один раз виконай:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src\AuthService -s src\AuthService
dotnet ef migrations add InitialCreate -p src\CatalogService -s src\CatalogService
```
Покажи папки `Migrations` у проєктах. (`EnsureCreated` і міграції не змішують: якщо вирішиш застосовувати
через `dotnet ef database update`, спочатку запусти `start-all.ps1 -ResetDb`, щоб бази були порожніми.)

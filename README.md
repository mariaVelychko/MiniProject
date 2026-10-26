# MiniProject — SportClub: шлюз на Ocelot, Auth-, Catalog- і Schedule-сервіси (ЛР0–ЛР2)

Мініпроєкт до курсу з мікросервісів. Моноліт із минулого курсу лишається в роботі, перед ним стоїть
шлюз `ApiGateway` на Ocelot. **ЛР1:** із моноліту винесено два сервіси — `AuthService` (користувачі, ролі, JWT,
refresh-токени) і `CatalogService` (каталог позицій); маршрути `/api/v1/auth|users|catalog/**` шлюз
відправляє на них, решту — на моноліт. Кожен сервіс має власну базу MySQL.

**Швидкий старт і шпаргалка для захисту:** [docs/ЗАХИСТ.md](docs/ЗАХИСТ.md) —
`powershell -ExecutionPolicy Bypass -File .\start-all.ps1`.

## Структура репозиторію

```
MiniProject/
├── MiniProject.sln
├── docs/
│   ├── inventory.md                     інвентаризація моноліту (завдання 4.1)
│   ├── questions.md                     відповіді на контрольні питання
│   ├── adr/
│   │   ├── 0001-split-monolith-into-services.md
│   │   └── 0002-ocelot-as-gateway.md
│   └── diagrams/
│       ├── 01-composition.md            діаграма композиції (C4 Container)
│       ├── 02-sequence-current.md       послідовність: як працює зараз
│       └── 03-sequence-future.md        послідовність: після винесення сервісів
└── src/
    ├── SportClubLibrary/                бібліотека з минулого курсу (не змінювалася)
    ├── SportClub/                       десктопний SportClubWPF (не змінювався)
    ├── SportClub.Tests/                 тести бібліотеки (не змінювалися)
    ├── Shared/                          спільні дрібниці: JWT-перевірка, PagedResult, ServiceResult, обробка помилок
    ├── AuthService/                     Auth-сервіс (ЛР1), порт 5001, БД miniproject_auth
    ├── CatalogService/                  Catalog-сервіс (ЛР1), порт 5002, БД miniproject_catalog
    ├── Monolith/                        SportClub.Api — моноліт, порт 5100, БД sportclub_cs
    └── ApiGateway/                      шлюз на Ocelot, порт 5000
```

Моноліт `SportClub.Api` не містить власної бізнес-логіки: він публікує через HTTP ті самі
`JsonRepository<T>`, `AuthService` і моделі з `SportClubLibrary`, які використовує десктопний
клієнт. Сховище моноліту — MySQL (EF Core, Pomelo), база `sportclub_cs`; таблиці створюються
при першому запуску.

## Вимоги

.NET SDK 10.0 (`dotnet --version`) і запущений MySQL 8.

## Запуск

Найпростіше — одним скриптом (питає пароль MySQL, збирає рішення, запускає всі сервіси в порядку
Auth → Catalog → Monolith → Gateway і чекає на `/health` кожного):

```powershell
powershell -ExecutionPolicy Bypass -File .\start-all.ps1
```

Вручну — окремі термінали (пароль MySQL у `ConnectionStrings:*` в `appsettings.json` кожного сервісу або
в змінних `ConnectionStrings__AuthDb`, `ConnectionStrings__CatalogDb`, `ConnectionStrings__SportClub`;
`Jwt:Key`/`Issuer`/`Audience` мають бути однаковими в Auth і Catalog):

```bash
dotnet run --project src/AuthService
dotnet run --project src/CatalogService
```

Моноліт і шлюз:

```bash
dotnet run --project src/Monolith
```
Моноліт: `http://localhost:5100`, Swagger: `http://localhost:5100/swagger`.
Під час першого запуску створюється адміністратор моноліту `admin` / `admin`
(в AuthService — `admin@miniproject.local` / `Admin123!`).

```bash
dotnet run --project src/ApiGateway
```
Шлюз: `http://localhost:5000`.

## ЛР2: Schedule-сервіс (тренування + записи)

З моноліту винесено **ScheduleService** (порт 5102, БД `miniproject_schedule`): `/api/workouts/**` і `/api/bookings/**`.
Обґрунтування й межі — [docs/adr/0003-schedule-extraction.md](docs/adr/0003-schedule-extraction.md); порівняння поведінки —
[docs/baseline-diff.md](docs/baseline-diff.md); відповіді на питання — [docs/lab2-questions.md](docs/lab2-questions.md).

### Порядок виконання (важливо: baseline — ДО перемикання)

1. Запустіть **старий** стек (моноліт на :5100 ще обслуговує workouts/bookings — старі контролери в ньому збережено) і зніміть
   знімок: `cd tests\baseline; .\run-scenarios.ps1 -Base http://localhost:5100 -Out out`, закомітьте `tests/baseline/out`
   окремим комітом.
2. Згенеруйте міграцію (один раз) і закомітьте її:
   `dotnet tool install --global dotnet-ef` → `dotnet ef migrations add InitialCreate --project src/ScheduleService --output-dir Migrations`.
3. Запустіть усе: `powershell -ExecutionPolicy Bypass -File .\start-all.ps1` (піднімає й ScheduleService, схема створюється сама).
4. Перенесіть старі дані: виконайте `docs/migrations/workouts-to-schedule.sql` у MySQL Workbench.
5. Прогін через шлюз і порівняння: `.\run-scenarios.ps1 -Base http://localhost:5000 -Out out-after` →
   `.\compare.ps1 -Before out -After out-after -Markdown ..\..\docs\baseline-diff.md`; пояснення розбіжностей — у `docs/baseline-diff.md`.
6. Інтеграційні тести: створіть порожню БД `miniproject_schedule_test`, задайте `TEST_MYSQL_PASSWORD` (або `TEST_SCHEDULE_DB`) і
   виконайте `dotnet test tests/Schedule.IntegrationTests`.

Усе однією командою в контейнерах: скопіюйте `.env.example` у `.env`, заповніть і виконайте `docker compose up --build`.

### Секрети

У репозиторії немає паролів і `Jwt:Key`: рядки підключення й ключ задаються змінними середовища
(`ConnectionStrings__AuthDb`, `ConnectionStrings__CatalogDb`, `ConnectionStrings__SportClub`, `ConnectionStrings__ScheduleDb`,
`Jwt__Key`), user-secrets або `.env` — `start-all.ps1` робить це сам. Ключ, що раніше лежав у `appsettings.json`, вважайте
скомпрометованим (скрипт тепер генерує випадковий).

## Порти

| Застосунок | Порт | Призначення |
|---|---|---|
| ApiGateway | 5000 | єдина адреса для клієнта |
| SportClub.Api (моноліт) | 5100 | уся предметна логіка |
| AuthService | 5001 | ЛР1: автентифікація, користувачі, ролі |
| CatalogService | 5002 | ЛР1: каталог позицій |
| ScheduleService | 5102 | ЛР2: розклад і записи на тренування |

## Маршрути шлюзу

Ocelot перевіряє маршрути згори вниз і застосовує перший, що збігся, тому
маршрут-перехоплювач стоїть останнім.

| # | Upstream (клієнт) | Downstream (зараз, після ЛР1) | Далі |
|---|---|---|---|
| 1 | `/api/v1/auth/{everything}` | AuthService `:5001` | — |
| 2 | `/api/v1/users/{everything}` | AuthService `:5001` | — |
| 3 | `/api/v1/catalog/{everything}` | CatalogService `:5002` | — |
| 4 | `/api/auth/{everything}` | моноліт `:5100` | — |
| 5 | `/api/workouts` | ScheduleService `:5102` (ЛР2, було: моноліт) | — |
| 6 | `/api/workouts/{everything}` | ScheduleService `:5102` (ЛР2) | — |
| 6a | `/api/bookings`, `/api/bookings/{everything}` | ScheduleService `:5102` (ЛР2, раніше — перехоплювач) | — |
| 7 | `/api/{everything}` | моноліт `:5100` (перехоплювач, останній) | моноліт :5100 |

Окремий маршрут для `/api/workouts` потрібен тому, що заповнювач `{everything}` не збігається
з порожнім останнім сегментом: `/api/workouts/{everything}` не матчить `/api/workouts`.

`/health` обробляє сам шлюз — він зареєстрований **до** `UseOcelot()`, бо той термінальний.

## Конфігурація без зашитих адрес

Адреса моноліту береться з конфігурації, а не з коду. Значення за замовчуванням лежать у
`src/ApiGateway/ocelot.json` (поле `DownstreamHostAndPorts` кожного маршруту). Якщо задати
`Monolith:Host` і `Monolith:Port` — у `appsettings.json` або змінними середовища — вони
перекриють значення з `ocelot.json` для всіх маршрутів під час старту:

```bash
Monolith__Host=api-host Monolith__Port=8080 dotnet run --project src/ApiGateway
```

Дозволені origins для CORS налаштовуються ключем `Cors:Origins` (за замовчуванням `*`).

## Приклади запитів через шлюз

```bash
# здоров'я шлюзу
curl -i http://localhost:5000/health

# вхід (POST, явний маршрут /api/auth/**)
curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin","password":"admin"}'

# список користувачів (GET, маршрут-перехоплювач)
curl -s http://localhost:5000/api/users

# створення тренера (POST)
curl -s -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"role":"trainer","fullName":"Іван Ковальчук","email":"coach@club.ua","phone":"+38 (050) 111-22-33","password":"coach123","specialization":"Кросфіт"}'

# створення тренування (POST, явний маршрут /api/workouts)
curl -s -X POST http://localhost:5000/api/workouts \
  -H "Content-Type: application/json" \
  -d '{"type":"Кросфіт","time":"2026-09-15T18:00:00","durationMin":60,"hall":"Зал 1","maxParticipants":10,"coachId":2}'

# розклад (GET)
curl -s http://localhost:5000/api/workouts

# запис клієнта на тренування (POST)
curl -s -X POST http://localhost:5000/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"workoutId":1,"clientId":3}'

# скасування запису (DELETE)
curl -i -X DELETE http://localhost:5000/api/bookings/1/3

# власний кореляційний ідентифікатор
curl -i -H "X-Correlation-Id: demo-123" http://localhost:5000/api/workouts
```

Той самий запит напряму до моноліту (`http://localhost:5100/api/workouts`) повертає ту саму
відповідь — шлюз нічого не змінює.

## Логування

Кожен запит лишає в консолі шлюзу один рядок: метод, вхідний шлях, реальний отримувач, код
відповіді, тривалість і кореляційний ідентифікатор.

```
info: Gateway[0]
      GET /api/workouts -> http://localhost:5100/api/workouts | 200 | 34 ms | 8f2c...
```

## Наскрізний ідентифікатор запиту

Шлюз читає заголовок `X-Correlation-Id`. Якщо клієнт його не надіслав, шлюз генерує GUID,
додає до запиту, що йде на моноліт, і повертає клієнтові у відповіді. На ЛР5 це дозволить
зшити логи трьох процесів в один ланцюжок.

## Поведінка, коли моноліт вимкнено

Ocelot повертає **502 Bad Gateway** з порожнім тілом, шлюз при цьому продовжує працювати:
`/health` і далі відповідає 200. Рішення лишити цю поведінку зафіксовано в ADR-0002.

## Сценарій перевірки роботи

1. Запустити моноліт і шлюз; `GET http://localhost:5000/health` → `200`.
2. `GET http://localhost:5100/api/workouts` і `GET http://localhost:5000/api/workouts`
   повертають однакову відповідь.
3. У консолі шлюзу видно рядок з методом, шляхом, отримувачем, кодом і часом.
4. Працюють різні маршрути: `GET /api/workouts`, `POST /api/auth/login`,
   `POST /api/bookings`, `DELETE /api/bookings/{workoutId}/{clientId}`.
5. Явний маршрут спрацьовує раніше за перехоплювач: тимчасово змініть порт першого маршруту
   в `ocelot.json` на неіснуючий (наприклад, 5999) і перезапустіть шлюз. `POST /api/auth/login`
   віддасть 502, тоді як `GET /api/users` і далі працює через перехоплювач. Змінні середовища
   `Monolith__*` при цьому задавати не можна — вони перекривають порти всіх маршрутів.
6. Зупинити моноліт → шлюз повертає 502 і не падає.
7. `docs/adr/` містить два записи, `docs/diagrams/` — три діаграми.

## Документи

* [Інвентаризація моноліту](docs/inventory.md)
* [Відповіді на контрольні питання ЛР0](docs/questions.md)
* [ЛР1: шпаргалка для захисту](docs/ЗАХИСТ.md)
* [ЛР1: відповіді на контрольні питання](docs/lab1-questions.md)
* [ЛР1: перенесення даних каталогу з моноліту](docs/migrations/workouts-to-catalog-items.md)
* [ADR-0001. Поділ моноліту на сервіси](docs/adr/0001-split-monolith-into-services.md)
* [ADR-0002. Шлюз на Ocelot](docs/adr/0002-ocelot-as-gateway.md)
* [Діаграма композиції](docs/diagrams/01-composition.md)
* [Послідовність: як зараз](docs/diagrams/02-sequence-current.md)
* [Послідовність: після винесення сервісів](docs/diagrams/03-sequence-future.md)

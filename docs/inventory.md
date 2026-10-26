# Інвентаризація моноліту SportClub (завдання 4.1)

Моноліт — застосунок `SportClub.Api` (порт 5100), який публікує через HTTP бізнес-логіку
бібліотеки `SportClubLibrary` з минулого курсу. Сховище — не СУБД, а JSON-файли, з якими
працює узагальнений репозиторій `JsonRepository<T>`; він зареєстрований як **singleton**,
тому всі контролери працюють з одним і тим самим списком у пам'яті.

| Контролер | Маршрути | Сутності | «Таблиці» (файли-сховища) | Хто ще їх читає / пише |
|---|---|---|---|---|
| `AuthController` | `POST /api/auth/login`, `GET /api/auth/roles` | `Person`, `Client`, `Trainer`, `Admin` | `users.json` (читання) | `UsersController` (CRUD), `WorkoutsController` (читає тренера), `BookingsController` (читає клієнта), `ProfileController` (читає користувача) |
| `UsersController` | `GET/POST /api/users`, `GET/PUT/DELETE /api/users/{id}` | `Person`, `Client`, `Trainer`, `Admin` | `users.json` (читання й запис) | `AuthController` (перевірка пари email+хеш), `WorkoutsController` (тренер тренування), `BookingsController` (учасник), `ProfileController` |
| `WorkoutsController` | `GET/POST /api/workouts`, `GET/PUT/DELETE /api/workouts/{id}` | `Workout`, `Trainer` | `workouts.json` (читання й запис), `users.json` (читання: пошук тренера) | `BookingsController` (змінює `Participants` того самого тренування), `ProfileController` (читає розклад) |
| `BookingsController` | `POST /api/bookings`, `DELETE /api/bookings/{workoutId}/{clientId}`, `GET /api/bookings/client/{clientId}` | `Workout`, `Client` | `workouts.json` (читання й запис), `users.json` (читання: пошук клієнта) | `WorkoutsController` (перевіряє місткість під час редагування), `ProfileController` |
| `ProfileController` | `GET /api/profile/{userId}` | `Person`, `Workout` | `users.json` + `workouts.json` (читання) | — (лише споживач) |

## Спільні компоненти

| Компонент | Призначення | Хто використовує |
|---|---|---|
| `JsonRepository<T>` | CRUD над JSON-файлом, генерація `Id`, збереження після кожної зміни | усі контролери |
| `AuthService` | перевірка логіна й пароля | `AuthController` |
| `PasswordHasher` | SHA-256 хешування пароля | `AuthController` (через `AuthService`), `UsersController` |
| `FileLogger` | запис подій у `log.txt` | репозиторії, `AuthService` |

## Що видно з таблиці

1. `users.json` — найзатребуваніший ресурс: його читають усі контролери, а пише лише `UsersController`.
   Це природний кандидат на окремий сервіс з єдиним власником даних.
2. `workouts.json` пишуть два контролери — `WorkoutsController` і `BookingsController`.
   Розводити їх у різні сервіси не можна: обидва змінюють один і той самий об'єкт `Workout`.
3. `Workout` зберігає **вкладені об'єкти** `Trainer` і `List<Client>`, а не їхні ідентифікатори.
   Після поділу це перетвориться або на мережевий виклик, або на денормалізацію.
4. `ProfileController` — єдиний, хто поєднує обидва файли в одній відповіді; після поділу він
   стане місцем, де з'явиться перший міжсервісний виклик.

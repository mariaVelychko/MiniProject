# Schedule.IntegrationTests

Інтеграційні тести ScheduleService: `WebApplicationFactory<Program>` + реальна тестова MySQL-БД + стаби Catalog і довідника користувачів.

```powershell
# 1) порожня БД (схему тести створюють самі):  CREATE DATABASE miniproject_schedule_test;
# 2) пароль MySQL (не в репозиторій!):
$env:TEST_MYSQL_PASSWORD = "<пароль>"       # або $env:TEST_SCHEDULE_DB = "server=...;database=miniproject_schedule_test;..."
dotnet test tests/Schedule.IntegrationTests
```

* Назва БД **мусить закінчуватися на `_test`** — інакше фабрика відмовиться стартувати (тести стирають схему).
* Усі тести в одній колекції (послідовно); кожен створює власні дані й не залежить від порядку запуску.
* Очікування списано з поведінки моноліту (`tests/baseline`): статус **і** структура тіла перевіряються в кожному тесті.
* Перевірка «тест справді ловить регрес»: зламайте один рядок у `BookingService`/`WorkoutRepository` — відповідний тест має стати червоним.

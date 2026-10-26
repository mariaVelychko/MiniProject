# Діаграма послідовностей 2: той самий сценарій після винесення сервісів

Сценарій той самий — `GET /api/workouts` — але після ЛР1–ЛР2, коли існують Auth і Schedule.
Показано, де перевіряється JWT і які виклики стають мережевими.

```mermaid
---
title: "GET /api/workouts після винесення Auth і Schedule (ЛР1-ЛР2)"
---
sequenceDiagram
    autonumber
    participant C as Клієнт
    participant G as ApiGateway :5000
    participant A as Auth-сервіс :5101
    participant S as Schedule-сервіс :5102
    participant FA as users.json
    participant FS as workouts.json

    Note over C,A: крок 0 - вхід у систему
    C->>G: HTTP POST /api/auth/login
    G->>A: HTTP POST http://localhost:5101/api/auth/login
    A->>FA: файл I/O: пошук за email і хешем
    FA-->>A: Person
    A-->>G: HTTP 200, JWT
    G-->>C: HTTP 200, JWT

    Note over C,S: крок 1 - перегляд розкладу з токеном
    C->>G: HTTP GET /api/workouts, Authorization: Bearer JWT
    Note over G: перевірка підпису JWT на межі системи<br/>(AuthenticationOptions маршруту Ocelot);<br/>без токена - 401, далі не йде
    Note over G: додає X-Correlation-Id
    G->>S: HTTP GET http://localhost:5102/api/workouts<br/>+ Bearer JWT + X-Correlation-Id
    Note over S: повторна перевірка claims усередині сервісу<br/>(шлюз не єдиний рубіж)
    S->>FS: файл I/O: читання Workout (CoachId, ParticipantIds)
    FS-->>S: список тренувань без даних про людей
    S->>A: HTTP GET http://localhost:5101/api/users?ids=...<br/>мережевий виклик замість колишнього<br/>звертання до поля Coach
    A->>FA: файл I/O
    FA-->>A: імена тренерів і клієнтів
    A-->>S: HTTP 200, JSON
    Note over S: якщо Auth недоступний - віддати розклад<br/>з денормалізованим CoachName і деградацією
    S-->>G: HTTP 200, JSON
    G-->>C: HTTP 200, JSON + X-Correlation-Id
```

## Що змінилося порівняно з діаграмою 1

| Було (ЛР0) | Стало (ЛР1–ЛР2) |
|---|---|
| `workout.Coach` — поле об'єкта в пам'яті | `GET /api/users` — мережевий виклик Schedule → Auth |
| один процес, одна відмова | три процеси; Auth може бути недоступний, коли Schedule живий |
| авторизації немає | JWT перевіряється на шлюзі, а потім ще раз у сервісі |
| читання users.json і workouts.json в одній транзакції процесу | два незалежні сховища, узгодженість лише у часі |
| один запис у логах | ланцюжок записів, зшитий через `X-Correlation-Id` |

Саме порівняння двох діаграм і показує ціну поділу: один локальний доступ до поля
перетворюється на мережеву взаємодію двох застосунків з таймаутами й частковими відмовами.

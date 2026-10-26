# Діаграма послідовностей 1: як працює зараз (після ЛР0)

Сценарій: **клієнт переглядає розклад тренувань** — `GET /api/workouts`.

```mermaid
---
title: "GET /api/workouts через шлюз, стан після ЛР0"
---
sequenceDiagram
    autonumber
    participant C as Клієнт (curl / Swagger)
    participant G as ApiGateway :5000
    participant M as Моноліт SportClub.Api :5100
    participant F as workouts.json + users.json

    C->>G: HTTP GET /api/workouts
    Note over G: middleware: додає X-Correlation-Id,<br/>запускає секундомір
    Note over G: Ocelot шукає перший збіг:<br/>маршрут 2 "/api/workouts"
    G->>M: HTTP GET http://localhost:5100/api/workouts<br/>+ X-Correlation-Id
    Note over M: WorkoutsController, локальний виклик<br/>IAsyncRepository&lt;Workout&gt;
    M->>F: файл I/O: читання списку з пам'яті,<br/>підвантаженого з workouts.json
    F-->>M: List&lt;Workout&gt; з вкладеними Trainer і Client
    M-->>G: HTTP 200, JSON: масив WorkoutResponse
    G-->>C: HTTP 200, JSON + X-Correlation-Id
    Note over G: у консоль: GET /api/workouts -><br/>http://localhost:5100/api/workouts | 200 | N ms
```

## Легенда

* Усі стрілки — HTTP/REST з тілом JSON, окрім `M -> F`: це локальне читання файла в тому
  самому процесі.
* Порти підписані на учасниках: шлюз 5000, моноліт 5100.
* Сховище — JSON-файли `workouts.json` і `users.json`, а не СУБД.
* Ця діаграма відповідає тому, що реально працює наприкінці ЛР0.

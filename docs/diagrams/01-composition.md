# Діаграма композиції (рівень Container моделі C4)

Система SportClub: що працює зараз (суцільні лінії) і що з'явиться на ЛР1–ЛР2 (пунктир).

```mermaid
---
title: SportClub — контейнерна діаграма, стан після ЛР0 і цільовий стан
---
flowchart TB
    subgraph clients["Клієнти"]
        wpf["SportClubWPF<br/>десктоп, .NET 10"]
        tool["Swagger UI / curl / вебсторінка"]
    end

    gw["ApiGateway<br/>ASP.NET Core + Ocelot 25<br/>порт 5000<br/>(існує)"]

    subgraph today["Існує зараз"]
        mono["Моноліт SportClub.Api<br/>ASP.NET Core<br/>порт 5100"]
        usersdb[("users.json")]
        workoutsdb[("workouts.json")]
    end

    subgraph future["З'явиться на ЛР1-ЛР2"]
        auth["Auth-сервіс<br/>порт 5101"]
        authdb[("users.json<br/>власник: Auth")]
        sched["Schedule-сервіс<br/>порт 5102"]
        scheddb[("workouts.json<br/>власник: Schedule")]
    end

    tool -->|"HTTP/REST JSON, до :5000"| gw
    wpf -.->|"HTTP/REST JSON, до :5000 (майбутнє)"| gw
    gw -->|"HTTP/REST JSON, /api/** до :5100"| mono
    mono -->|"файл I/O, читання і запис"| usersdb
    mono -->|"файл I/O, читання і запис"| workoutsdb

    gw -.->|"HTTP/REST JSON, /api/auth/**, /api/users/** до :5101 (ЛР1)"| auth
    gw -.->|"HTTP/REST JSON, /api/workouts/**, /api/bookings/** до :5102 (ЛР2)"| sched
    auth -.->|"файл I/O"| authdb
    sched -.->|"файл I/O"| scheddb
    sched -.->|"HTTP/REST JSON, GET /api/users/id до :5101 (ЛР2)"| auth
    mono -.->|"HTTP/REST JSON, агрегація профілю (ЛР2)"| auth
    mono -.->|"HTTP/REST JSON, агрегація профілю (ЛР2)"| sched
```

## Легенда

| Позначення | Значення |
|---|---|
| Суцільна стрілка | зв'язок працює вже після ЛР0 |
| Пунктирна стрілка | зв'язок з'явиться на ЛР1–ЛР2 |
| Прямокутник | окремий процес (контейнер за C4) із зазначеним портом |
| Циліндр | сховище даних; у цьому проєкті це JSON-файли, а не СУБД |
| Підпис на стрілці | протокол, формат і порт отримувача |

Десктопний `SportClubWPF` наразі працює з бібліотекою напряму і з файлами локально; перехід
на HTTP через шлюз показано пунктиром як майбутній крок.

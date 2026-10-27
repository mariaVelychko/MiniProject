# Діаграма після ЛР3: складений запит, автентифікація, кеш, ліміти

```mermaid
flowchart TB
    client["Клієнт (Postman, SportClubWPF)"]

    subgraph gw["ApiGateway :5000 — єдина відкрита точка"]
        direction TB
        rl["1. Rate limiter<br/>вхід 5/хв IP · аноніми 100/хв IP · авторизовані token bucket за користувачем"]
        jwt["2. JWT-перевірка (підпис, строк, iss, aud)<br/>→ X-User-Id, X-User-Roles (клієнтські видаляються)"]
        screen["GET /api/v1/screens/training/{id}<br/>Task.WhenAll, таймаут 2 с на виклик"]
        cache["3. Кеш каталогу (GET /catalog/**)<br/>ключ = шлях+query, TTL 60 с, інвалідизація зміною"]
        proxy["4. Ocelot + дедлайн 5 с<br/>504 / 503 у ProblemDetails"]
    end

    auth["AuthService :5001"]
    catalog["CatalogService :5002"]
    mono["Моноліт :5100"]
    sched["ScheduleService :5102"]

    client -->|"єдиний вхід"| rl --> jwt
    jwt --> screen
    jwt --> cache --> proxy

    screen -->|"1 (обов'язково)"| catalog
    screen -->|"2 (необов'язково)"| sched
    screen -->|"3 (необов'язково)"| auth
    proxy --> auth & catalog & mono & sched
```

**Читати так.** Порти 5001/5002/5100/5102 у docker-compose назовні **не публікуються** — до сервісів можна дістатися лише через шлюз.

## Складений запит: послідовність і часткова відповідь

```mermaid
sequenceDiagram
    participant C as Клієнт
    participant G as Шлюз
    participant K as Catalog
    participant S as Schedule
    participant A as Auth

    C->>G: GET /screens/training/1 (Bearer)
    G->>G: перевірка JWT, ліміт
    par паралельно, до 2 с кожен
        G->>K: GET /catalog/items/1 (кеш)
        G->>S: GET /workouts?catalogItemId=1
        G->>A: GET /users/me
    end
    K-->>G: 200 позиція
    S--xG: 503 / таймаут
    A-->>G: 200 профіль
    G-->>C: 200 {item, sessions:null, me, partial:true, unavailable:["sessions"]}<br/>X-Partial-Response: true
```

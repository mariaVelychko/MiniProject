# Перенесення даних із моноліту в Catalog-сервіс

Разова копія (не спільний доступ до таблиці!) — виконати один раз після того, як
`CatalogService` уже створив свою схему (`EnsureCreatedAsync`/міграція), тобто таблиці
`categories` і `items` у базі `miniproject_catalog` уже існують.

## Як виконати

1. Відкрий MySQL Workbench, підключись до сервера, де лежать **обидві** бази —
   і моноліту (`sportclub` / `sportclub_cs` — перевір свою реальну назву в списку схем),
   і `miniproject_catalog`.
2. Постав курсор у вільній вкладці SQL (не всередині жодної конкретної схеми — скрипт сам
   звертається до баз через `<база>.<таблиця>`).
3. У скрипті нижче заміни `sportclub_cs` на свою реальну назву бази моноліту, якщо відрізняється.
4. Виконай (⚡ Execute).

```sql
-- 1) Кожен унікальний Workouts.Type моноліту стає окремою категорією каталогу.
--    (Категорії "Йога"/"Кросфіт"/"Бокс" уже посіяні сервісом при старті — дублікатів не буде.)
INSERT INTO miniproject_catalog.categories (Name)
SELECT DISTINCT w.Type
FROM sportclub_cs.workouts w
WHERE NOT EXISTS (
    SELECT 1 FROM miniproject_catalog.categories c WHERE c.Name = w.Type
);

-- 2) Кожен запис Workouts стає позицією каталогу (Item).
--    Price свідомо 0 — у моноліті тренування не мали вартості; онови вручну за потреби.
INSERT INTO miniproject_catalog.items
    (Title, Description, Price, DurationMin, Capacity, IsActive, CreatedAt, CategoryId)
SELECT
    w.Type                                   AS Title,
    CONCAT('Перенесено з моноліту. Зал: ', w.Hall) AS Description,
    0                                        AS Price,
    w.DurationMin                            AS DurationMin,
    w.MaxParticipants                        AS Capacity,
    1                                        AS IsActive,
    NOW()                                    AS CreatedAt,
    c.Id                                     AS CategoryId
FROM sportclub_cs.workouts w
JOIN miniproject_catalog.categories c ON c.Name = w.Type;
```

## Після перенесення

За правилом з методички (п. 1.1): щойно шлюз перемкнено на `CatalogService`
(маршрут `/api/v1/catalog/**` в `ocelot.json` — уже перемкнено), таблиця `workouts` у
моноліті вважається застарілою для читання каталогу і **більше не змінюється** з цією
метою. Бронювання (`BookingsController`, записи учасників) тимчасово лишається в моноліті —
це піде в наступну лабораторну (Booking-сервіс).

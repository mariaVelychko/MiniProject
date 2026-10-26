-- ЛР2: разове перенесення даних із моноліту в БД Schedule-сервісу (копія, а не спільний доступ!).
--
-- Передумови:
--   1) Schedule-сервіс хоча б раз запущено, тож схема miniproject_schedule вже створена (міграції / EnsureCreated);
--   2) обидві бази лежать на одному сервері MySQL; база моноліту називається sportclub_cs (змініть, якщо інакше);
--   3) таблиці Schedule порожні (скрипт запускається один раз).
-- Імена таблиць нижче — у нижньому регістрі (MySQL на Windows; на Linux замініть на Workouts/Bookings/Persons/WorkoutParticipants).
-- ПЕРЕВІРТЕ перед запуском: DESCRIBE sportclub_cs.workoutparticipants;  -- очікувані колонки: WorkoutId, ParticipantsId

START TRANSACTION;

-- 1) Тренування. Id зберігаємо ті самі, що були в моноліті — клієнти й посилання не помітять переносу.
--    CoachName — знімок імені тренера на момент перенесення (FK на persons у новій БД немає).
INSERT INTO miniproject_schedule.workouts
    (Id, CatalogItemId, Type, PriceSnapshot, Time, DurationMin, Hall, CoachId, CoachName, MaxParticipants, CreatedAt, UpdatedAt)
SELECT w.Id, w.CatalogItemId, w.Type, NULL, w.Time, w.DurationMin, w.Hall,
       w.CoachId, p.FullName, w.MaxParticipants, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
FROM sportclub_cs.workouts w
LEFT JOIN sportclub_cs.persons p ON p.Id = w.CoachId;

-- 2) Записи клієнтів (були таблицею-зв'язкою WorkoutParticipants). Дані клієнта — знімок.
INSERT INTO miniproject_schedule.bookings
    (WorkoutId, ClientId, ClientFullName, ClientEmail, ClientPhone, CreatedAt)
SELECT wp.WorkoutId, wp.ParticipantsId, p.FullName, p.Email, p.Phone, UTC_TIMESTAMP(6)
FROM sportclub_cs.workoutparticipants wp
JOIN sportclub_cs.persons p ON p.Id = wp.ParticipantsId
ORDER BY wp.WorkoutId, wp.ParticipantsId;

COMMIT;

-- 3) Підняти AUTO_INCREMENT вище за максимальний старий Id, щоб нові тренування не перетиналися зі старими id.
--    (Id тренувань ми скопіювали явно, тож без цього кроку лічильник міг би почати з 1 і зіткнутися з ними.)
SET @next := (SELECT COALESCE(MAX(Id), 0) + 1 FROM miniproject_schedule.workouts);
SET @sql := CONCAT('ALTER TABLE miniproject_schedule.workouts AUTO_INCREMENT = ', @next);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 4) Перевірка: кількості мають збігтися.
SELECT 'workouts: monolith vs schedule' AS check_name,
       (SELECT COUNT(*) FROM sportclub_cs.workouts)            AS monolith,
       (SELECT COUNT(*) FROM miniproject_schedule.workouts)    AS schedule
UNION ALL
SELECT 'bookings: monolith vs schedule',
       (SELECT COUNT(*) FROM sportclub_cs.workoutparticipants),
       (SELECT COUNT(*) FROM miniproject_schedule.bookings);

SELECT AUTO_INCREMENT AS next_workout_id
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = 'miniproject_schedule' AND LOWER(TABLE_NAME) = 'workouts';

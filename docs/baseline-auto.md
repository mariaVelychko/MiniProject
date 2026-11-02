<!-- згенеровано tests/baseline/compare.ps1; ручні рядки з розділу 'Свідомі відмінності' нижче збережіть -->

## Автоматичне порівняння знімків (до/після)

| Сценарій | Було (моноліт) | Стало (через шлюз) | Чи є різниця | Чому |
|---|---|---|---|---|
| 01-create-workout-valid | 201 | 201 | ні | — |
| 02-create-workout-unknown-catalog-item | 400 | 400 | ні | — |
| 03-create-workout-unknown-coach | 400 | 400 | ні | — |
| 04-create-workout-blank-hall | 400 | 400 | ні | — |
| 05-create-workout-missing-hall | 400 | 400 | ні | — |
| 06-create-workout-zero-capacity | 400 | 400 | ні | — |
| 07-get-workout | 200 | 200 | ні | — |
| 08-get-workout-unknown | 404 | 404 | ні | — |
| 09-book-valid | 200 | 200 | ні | — |
| 10-book-duplicate | 409 | 409 | ні | — |
| 11-book-second-client | 200 | 200 | ні | — |
| 12-book-when-full | 409 | 409 | ні | — |
| 13-book-unknown-workout | 404 | 404 | ні | — |
| 14-book-unknown-client | 404 | 404 | ні | — |
| 15-book-trainer-as-client | 404 | 404 | ні | — |
| 16-book-with-garbage-bearer-token | 200 | 200 | ні | — |
| 17-get-participants | 200 | 200 | ні | — |
| 18-get-participants-unknown-workout | 404 | 404 | ні | — |
| 19-get-client-bookings | 200 | 200 | ні | — |
| 20-list-filter-coach-and-item | 200 | 200 | ні | — |
| 21-profile-unknown-user | 404 | 404 | ні | — |
| 22-update-capacity-below-booked | 409 | 409 | ні | — |
| 23-update-valid | 200 | 200 | ні | — |
| 24-update-unknown-workout | 404 | 404 | ні | — |
| 25-cancel-booking | 204 | 204 | ні | — |
| 26-cancel-booking-repeat | 404 | 404 | ні | — |
| 27-cancel-unknown-workout | 404 | 404 | ні | — |
| 28-delete-workout | 204 | 204 | ні | — |
| 29-delete-workout-repeat | 404 | 404 | ні | — |

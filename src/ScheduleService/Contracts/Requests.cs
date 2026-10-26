namespace ScheduleService.Contracts;

/// <summary>Запит на створення/редагування тренування. Контракт збігається з монолітом.</summary>
/// <param name="CatalogItemId">Id позиції каталогу (тип тренування).</param>
/// <param name="Time">Дата й час початку (без часового поясу, як у моноліті).</param>
/// <param name="Hall">Зал. Обов'язковий, не порожній.</param>
/// <param name="CoachId">Id тренера (користувач із роллю trainer).</param>
/// <param name="DurationMin">Тривалість, хв. Якщо не задано — береться з каталогу.</param>
/// <param name="MaxParticipants">Місткість. Якщо не задано — береться з каталогу.</param>
public record WorkoutRequest(
    int CatalogItemId,
    DateTime Time,
    string Hall,
    int CoachId,
    int? DurationMin,
    int? MaxParticipants);

/// <summary>Запит на запис клієнта на тренування.</summary>
/// <param name="WorkoutId">Id тренування.</param>
/// <param name="ClientId">Id клієнта (користувач із роллю client).</param>
public record BookingRequest(int WorkoutId, int ClientId);

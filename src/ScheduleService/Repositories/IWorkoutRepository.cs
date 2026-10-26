using ScheduleService.Models;

namespace ScheduleService.Repositories;

public sealed record WorkoutFilter(int? CoachId = null, int? CatalogItemId = null, int? ClientId = null);

public enum AddBookingResult { Added, WorkoutNotFound, Duplicate, Full }
public enum RemoveBookingResult { Removed, WorkoutNotFound, NotBooked }
public enum UpdateWorkoutResult { Updated, NotFound, CapacityBelowBooked }

public sealed record AddBookingOutcome(AddBookingResult Result, Workout? Workout);
public sealed record UpdateWorkoutOutcome(UpdateWorkoutResult Result, Workout? Workout);

/// <summary>Сховище агрегату «тренування + записи». Усі методи асинхронні й приймають CancellationToken.</summary>
public interface IWorkoutRepository
{
    Task<IReadOnlyList<Workout>> ListAsync(WorkoutFilter filter, CancellationToken ct);
    Task<Workout?> GetAsync(int id, CancellationToken ct);
    Task AddAsync(Workout workout, CancellationToken ct);

    /// <summary>Оновлює скалярні поля; атомарно перевіряє, що нова місткість не менша за кількість записів.</summary>
    Task<UpdateWorkoutOutcome> UpdateAsync(Workout incoming, CancellationToken ct);

    Task<bool> DeleteAsync(int id, CancellationToken ct);

    /// <summary>Атомарно (з блокуванням рядка тренування) перевіряє дубль і місткість та додає запис.</summary>
    Task<AddBookingOutcome> TryAddBookingAsync(int workoutId, Booking booking, CancellationToken ct);

    Task<RemoveBookingResult> RemoveBookingAsync(int workoutId, int clientId, CancellationToken ct);
}

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct);
    Task SaveAsync(IdempotencyRecord record, CancellationToken ct);
}

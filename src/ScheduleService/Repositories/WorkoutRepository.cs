using Microsoft.EntityFrameworkCore;
using ScheduleService.Data;
using ScheduleService.Models;

namespace ScheduleService.Repositories;

public sealed class WorkoutRepository(ScheduleDbContext db, TimeProvider clock) : IWorkoutRepository
{
    public async Task<IReadOnlyList<Workout>> ListAsync(WorkoutFilter filter, CancellationToken ct)
    {
        IQueryable<Workout> query = db.Workouts.AsNoTracking().Include(w => w.Bookings);

        if (filter.CoachId is { } coachId)
        {
            query = query.Where(w => w.CoachId == coachId);
        }

        if (filter.CatalogItemId is { } itemId)
        {
            query = query.Where(w => w.CatalogItemId == itemId);
        }

        if (filter.ClientId is { } clientId)
        {
            query = query.Where(w => w.Bookings.Any(b => b.ClientId == clientId));
        }

        return await query.OrderBy(w => w.Time).ThenBy(w => w.Id).ToListAsync(ct);
    }

    public Task<Workout?> GetAsync(int id, CancellationToken ct) =>
        db.Workouts.AsNoTracking().Include(w => w.Bookings).FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task AddAsync(Workout workout, CancellationToken ct)
    {
        db.Workouts.Add(workout);
        await db.SaveChangesAsync(ct);
    }

    public async Task<UpdateWorkoutOutcome> UpdateAsync(Workout incoming, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkoutAsync(incoming.Id, ct);

        var existing = await db.Workouts.Include(w => w.Bookings).FirstOrDefaultAsync(w => w.Id == incoming.Id, ct);

        if (existing is null)
        {
            return new(UpdateWorkoutResult.NotFound, null);
        }

        if (incoming.MaxParticipants < existing.Bookings.Count)
        {
            return new(UpdateWorkoutResult.CapacityBelowBooked, null);
        }

        existing.CatalogItemId = incoming.CatalogItemId;
        existing.Type = incoming.Type;
        existing.PriceSnapshot = incoming.PriceSnapshot;
        existing.Time = incoming.Time;
        existing.DurationMin = incoming.DurationMin;
        existing.Hall = incoming.Hall;
        existing.CoachId = incoming.CoachId;
        existing.CoachName = incoming.CoachName;
        existing.MaxParticipants = incoming.MaxParticipants;
        existing.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(UpdateWorkoutResult.Updated, existing);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        // Записи видаляються каскадно на рівні БД (FK Bookings -> Workouts, ON DELETE CASCADE).
        var deleted = await db.Workouts.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<AddBookingOutcome> TryAddBookingAsync(int workoutId, Booking booking, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkoutAsync(workoutId, ct);

        var workout = await db.Workouts.Include(w => w.Bookings).FirstOrDefaultAsync(w => w.Id == workoutId, ct);

        if (workout is null)
        {
            return new(AddBookingResult.WorkoutNotFound, null);
        }

        if (workout.Bookings.Any(b => b.ClientId == booking.ClientId))
        {
            return new(AddBookingResult.Duplicate, null);
        }

        if (workout.Bookings.Count >= workout.MaxParticipants)
        {
            return new(AddBookingResult.Full, null);
        }

        booking.WorkoutId = workoutId;
        booking.CreatedAt = clock.GetUtcNow().UtcDateTime;
        workout.Bookings.Add(booking);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(AddBookingResult.Added, workout);
    }

    public async Task<RemoveBookingResult> RemoveBookingAsync(int workoutId, int clientId, CancellationToken ct)
    {
        if (!await db.Workouts.AnyAsync(w => w.Id == workoutId, ct))
        {
            return RemoveBookingResult.WorkoutNotFound;
        }

        var removed = await db.Bookings
            .Where(b => b.WorkoutId == workoutId && b.ClientId == clientId)
            .ExecuteDeleteAsync(ct);

        return removed > 0 ? RemoveBookingResult.Removed : RemoveBookingResult.NotBooked;
    }

    /// <summary>SELECT ... FOR UPDATE у поточній транзакції: серіалізує записи/зміни одного тренування.</summary>
    private Task LockWorkoutAsync(int workoutId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT `Id` FROM `Workouts` WHERE `Id` = {workoutId} FOR UPDATE", ct);
}

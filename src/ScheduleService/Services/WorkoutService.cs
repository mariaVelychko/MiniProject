using ScheduleService.Clients;
using ScheduleService.Contracts;
using ScheduleService.Models;
using ScheduleService.Repositories;

namespace ScheduleService.Services;

public interface IWorkoutService
{
    Task<Outcome<IReadOnlyList<WorkoutResponse>>> ListAsync(int? coachId, int? catalogItemId, CancellationToken ct);
    Task<Outcome<WorkoutResponse>> GetAsync(int id, CancellationToken ct);
    Task<Outcome<WorkoutResponse>> CreateAsync(WorkoutRequest request, CancellationToken ct);
    Task<Outcome<WorkoutResponse>> UpdateAsync(int id, WorkoutRequest request, CancellationToken ct);
    Task<Outcome<bool>> DeleteAsync(int id, CancellationToken ct);
}

/// <summary>Логіка перенесена з WorkoutsController моноліту: ті самі перевірки, ті самі повідомлення.</summary>
public sealed class WorkoutService(
    IWorkoutRepository workouts,
    ICatalogClient catalog,
    IUserDirectoryClient users,
    TimeProvider clock) : IWorkoutService
{
    public async Task<Outcome<IReadOnlyList<WorkoutResponse>>> ListAsync(int? coachId, int? catalogItemId, CancellationToken ct)
    {
        var list = await workouts.ListAsync(new WorkoutFilter(coachId, catalogItemId), ct);
        return Outcome<IReadOnlyList<WorkoutResponse>>.Ok(list.Select(w => w.ToResponse()).ToList());
    }

    public async Task<Outcome<WorkoutResponse>> GetAsync(int id, CancellationToken ct)
    {
        var workout = await workouts.GetAsync(id, ct);

        return workout is null
            ? WorkoutNotFound(id).To<WorkoutResponse>()
            : Outcome<WorkoutResponse>.Ok(workout.ToResponse());
    }

    public async Task<Outcome<WorkoutResponse>> CreateAsync(WorkoutRequest request, CancellationToken ct)
    {
        var (workout, failure) = await BuildAsync(request, ct);

        if (failure is not null)
        {
            return failure.To<WorkoutResponse>();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        workout!.CreatedAt = now;
        workout.UpdatedAt = now;

        await workouts.AddAsync(workout, ct);

        return Outcome<WorkoutResponse>.Created(workout.ToResponse());
    }

    public async Task<Outcome<WorkoutResponse>> UpdateAsync(int id, WorkoutRequest request, CancellationToken ct)
    {
        if (await workouts.GetAsync(id, ct) is null)
        {
            return WorkoutNotFound(id).To<WorkoutResponse>();
        }

        var (incoming, failure) = await BuildAsync(request, ct);

        if (failure is not null)
        {
            return failure.To<WorkoutResponse>();
        }

        incoming!.Id = id;
        var result = await workouts.UpdateAsync(incoming, ct);

        return result.Result switch
        {
            UpdateWorkoutResult.Updated => Outcome<WorkoutResponse>.Ok(result.Workout!.ToResponse()),
            UpdateWorkoutResult.NotFound => WorkoutNotFound(id).To<WorkoutResponse>(),
            _ => Outcome<WorkoutResponse>.Fail(OutcomeKind.Conflict, "Нова місткість менша за кількість записаних клієнтів"),
        };
    }

    public async Task<Outcome<bool>> DeleteAsync(int id, CancellationToken ct) =>
        await workouts.DeleteAsync(id, ct)
            ? Outcome<bool>.NoContent()
            : WorkoutNotFound(id).To<bool>();

    // Порядок перевірок, як у моноліті: тренер → каталог → валідація.
    private async Task<(Workout? Workout, Failure? Failure)> BuildAsync(WorkoutRequest request, CancellationToken ct)
    {
        DirectoryUser? coach;

        try
        {
            coach = await users.GetUserAsync(request.CoachId, ct);
        }
        catch (UsersUnavailableException)
        {
            return (null, new Failure(OutcomeKind.Unavailable, "Сервіс користувачів недоступний. Спробуйте пізніше."));
        }

        if (coach is null || !string.Equals(coach.Role, "trainer", StringComparison.OrdinalIgnoreCase))
        {
            return (null, new Failure(OutcomeKind.BadRequest, $"Тренера {request.CoachId} не знайдено"));
        }

        CatalogItemInfo? item;

        try
        {
            item = await catalog.GetItemAsync(request.CatalogItemId, ct);
        }
        catch (CatalogUnavailableException)
        {
            return (null, new Failure(OutcomeKind.Unavailable, "Каталог типів тренувань недоступний. Спробуйте пізніше."));
        }

        if (item is null)
        {
            return (null, new Failure(OutcomeKind.BadRequest, $"Тип тренування {request.CatalogItemId} не знайдено в каталозі"));
        }

        if (!item.IsActive)
        {
            return (null, new Failure(OutcomeKind.BadRequest, $"Тип тренування \"{item.Title}\" вилучено з каталогу"));
        }

        var duration = request.DurationMin ?? item.DurationMin;
        var maxParticipants = request.MaxParticipants ?? item.Capacity;

        if (string.IsNullOrWhiteSpace(request.Hall))
        {
            return (null, new Failure(OutcomeKind.BadRequest, "Вкажіть зал (hall)"));
        }

        if (duration <= 0 || maxParticipants <= 0)
        {
            return (null, new Failure(OutcomeKind.BadRequest, "Тривалість і місткість мають бути додатними"));
        }

        return (new Workout
        {
            CatalogItemId = item.Id,
            Type = item.Title,
            PriceSnapshot = item.Price,
            Time = request.Time,
            DurationMin = duration,
            Hall = request.Hall,
            MaxParticipants = maxParticipants,
            CoachId = coach.Id,
            CoachName = coach.FullName,
        }, null);
    }

    private static Failure WorkoutNotFound(int id) => new(OutcomeKind.NotFound, $"Тренування {id} не знайдено");
}

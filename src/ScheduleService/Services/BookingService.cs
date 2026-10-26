using ScheduleService.Clients;
using ScheduleService.Contracts;
using ScheduleService.Models;
using ScheduleService.Repositories;

namespace ScheduleService.Services;

public interface IBookingService
{
    Task<Outcome<IReadOnlyList<WorkoutResponse>>> GetByClientAsync(int clientId, CancellationToken ct);
    Task<Outcome<IReadOnlyList<UserResponse>>> GetParticipantsAsync(int workoutId, CancellationToken ct);
    Task<Outcome<WorkoutResponse>> BookAsync(BookingRequest request, CancellationToken ct);
    Task<Outcome<bool>> CancelAsync(int workoutId, int clientId, CancellationToken ct);
}

/// <summary>Логіка перенесена з BookingsController моноліту. Перевірка клієнта — виклик до довідника користувачів.</summary>
public sealed class BookingService(IWorkoutRepository workouts, IUserDirectoryClient users) : IBookingService
{
    public async Task<Outcome<IReadOnlyList<WorkoutResponse>>> GetByClientAsync(int clientId, CancellationToken ct)
    {
        var list = await workouts.ListAsync(new WorkoutFilter(ClientId: clientId), ct);
        return Outcome<IReadOnlyList<WorkoutResponse>>.Ok(list.Select(w => w.ToResponse()).ToList());
    }

    public async Task<Outcome<IReadOnlyList<UserResponse>>> GetParticipantsAsync(int workoutId, CancellationToken ct)
    {
        var workout = await workouts.GetAsync(workoutId, ct);

        return workout is null
            ? Outcome<IReadOnlyList<UserResponse>>.Fail(OutcomeKind.NotFound, $"Тренування {workoutId} не знайдено")
            : Outcome<IReadOnlyList<UserResponse>>.Ok(
                workout.Bookings.OrderBy(b => b.Id).Select(b => b.ToParticipant()).ToList());
    }

    public async Task<Outcome<WorkoutResponse>> BookAsync(BookingRequest request, CancellationToken ct)
    {
        var notFound = Outcome<WorkoutResponse>.Fail(OutcomeKind.NotFound, $"Тренування {request.WorkoutId} не знайдено");

        if (await workouts.GetAsync(request.WorkoutId, ct) is null)
        {
            return notFound;
        }

        DirectoryUser? client;

        try
        {
            client = await users.GetUserAsync(request.ClientId, ct);
        }
        catch (UsersUnavailableException)
        {
            return Outcome<WorkoutResponse>.Fail(OutcomeKind.Unavailable, "Сервіс користувачів недоступний. Спробуйте пізніше.");
        }

        if (client is null || !string.Equals(client.Role, "client", StringComparison.OrdinalIgnoreCase))
        {
            return Outcome<WorkoutResponse>.Fail(OutcomeKind.NotFound, $"Клієнта {request.ClientId} не знайдено");
        }

        var result = await workouts.TryAddBookingAsync(request.WorkoutId, new Booking
        {
            ClientId = client.Id,
            ClientFullName = client.FullName,
            ClientEmail = client.Email,
            ClientPhone = client.Phone,
        }, ct);

        return result.Result switch
        {
            AddBookingResult.Added => Outcome<WorkoutResponse>.Ok(result.Workout!.ToResponse()),
            AddBookingResult.WorkoutNotFound => notFound,
            AddBookingResult.Duplicate => Outcome<WorkoutResponse>.Fail(OutcomeKind.Conflict, "Клієнт уже записаний на це тренування"),
            _ => Outcome<WorkoutResponse>.Fail(OutcomeKind.Conflict, "Вільних місць немає"),
        };
    }

    public async Task<Outcome<bool>> CancelAsync(int workoutId, int clientId, CancellationToken ct)
    {
        var result = await workouts.RemoveBookingAsync(workoutId, clientId, ct);

        return result switch
        {
            RemoveBookingResult.Removed => Outcome<bool>.NoContent(),
            RemoveBookingResult.WorkoutNotFound => Outcome<bool>.Fail(OutcomeKind.NotFound, $"Тренування {workoutId} не знайдено"),
            _ => Outcome<bool>.Fail(OutcomeKind.NotFound, "Клієнт не записаний на це тренування"),
        };
    }
}

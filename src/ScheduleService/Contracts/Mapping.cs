using ScheduleService.Models;

namespace ScheduleService.Contracts;

public static class Mapping
{
    public const string NoCoachName = "Не призначено";

    public static WorkoutResponse ToResponse(this Workout w) => new(
        w.Id,
        w.CatalogItemId,
        w.Type,
        w.Time,
        w.DurationMin,
        w.Hall,
        w.CoachId,
        w.CoachName ?? NoCoachName,
        w.Bookings.Count,
        w.MaxParticipants,
        w.Bookings.OrderBy(b => b.Id).Select(b => b.ClientId).ToList());

    public static UserResponse ToParticipant(this Booking b) => new(
        b.ClientId, b.ClientFullName, b.ClientEmail, b.ClientPhone, "client", "Клієнт клубу", null);
}

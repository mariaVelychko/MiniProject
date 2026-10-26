using SportClubLibrary.Models;

namespace SportClub.Api.Contracts;

public static class Mapping
{
    public static string RoleKey(this Person person) => person switch
    {
        Trainer => "trainer",
        Admin => "admin",
        _ => "client",
    };

    public static UserResponse ToResponse(this Person person) => new(
        person.Id,
        person.FullName,
        person.Email,
        person.Phone,
        person.RoleKey(),
        person.GetRoleInfo(),
        (person as Trainer)?.Specialization);

    public static WorkoutResponse ToResponse(this Workout workout) => new(
        workout.Id,
        workout.CatalogItemId,
        workout.Type,
        workout.Time,
        workout.DurationMin,
        workout.Hall,
        workout.Coach?.Id,
        workout.CoachName,
        workout.Participants.Count,
        workout.MaxParticipants,
        workout.Participants.Select(p => p.Id).ToList());
}

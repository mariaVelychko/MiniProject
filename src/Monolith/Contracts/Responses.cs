namespace SportClub.Api.Contracts;

public record UserResponse(
    int Id,
    string FullName,
    string Email,
    string Phone,
    string Role,
    string RoleInfo,
    string? Specialization);

public record WorkoutResponse(
    int Id,
    int? CatalogItemId,
    string Type,
    DateTime Time,
    int DurationMin,
    string Hall,
    int? CoachId,
    string CoachName,
    int Participants,
    int MaxParticipants,
    IReadOnlyList<int> ParticipantIds);

public record ProfileResponse(UserResponse User, IReadOnlyList<WorkoutResponse> Workouts);

public record ErrorResponse(string Message);

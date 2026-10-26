namespace SportClub.Api.Contracts;

public record LoginRequest(string Email, string Password);

public record CreateUserRequest(
    string Role,
    string FullName,
    string Email,
    string Phone,
    string Password,
    string? Specialization);

public record UpdateUserRequest(
    string FullName,
    string Email,
    string Phone,
    string? Password,
    string? Specialization);


public record WorkoutRequest(
    int CatalogItemId,
    DateTime Time,
    string Hall,
    int CoachId,
    int? DurationMin,
    int? MaxParticipants);

public record BookingRequest(int WorkoutId, int ClientId);

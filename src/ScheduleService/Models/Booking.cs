namespace ScheduleService.Models;

/// <summary>Запис клієнта на тренування. Дані клієнта — знімок на момент запису (без FK на чужу БД).</summary>
public class Booking
{
    public int Id { get; set; }
    public int WorkoutId { get; set; }
    public int ClientId { get; set; }
    public string ClientFullName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

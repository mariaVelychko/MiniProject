namespace ScheduleService.Models;

/// <summary>
/// Тренування в розкладі — агрегат, яким володіє Schedule-сервіс. Записи клієнтів (<see cref="Bookings"/>)
/// живуть у тій самій БД, тому перевірка місткості лишається атомарною (ADR-0001, варіант 3).
/// </summary>
public class Workout
{
    public int Id { get; set; }

    /// <summary>Посилання на позицію каталогу (без FK: каталог в іншій БД).</summary>
    public int? CatalogItemId { get; set; }

    /// <summary>Знімок назви позиції каталогу на момент створення/редагування.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Знімок ціни позиції каталогу на момент створення/редагування (в API не віддається).</summary>
    public decimal? PriceSnapshot { get; set; }

    public DateTime Time { get; set; }
    public int DurationMin { get; set; }
    public string Hall { get; set; } = string.Empty;

    /// <summary>Id тренера в моноліті (без FK: користувачі в іншій БД).</summary>
    public int? CoachId { get; set; }

    /// <summary>Знімок імені тренера; null — тренера не призначено.</summary>
    public string? CoachName { get; set; }

    public int MaxParticipants { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Booking> Bookings { get; set; } = new();
}

namespace ScheduleService.Models;

/// <summary>Збережена відповідь на запит із заголовком Idempotency-Key.</summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
}

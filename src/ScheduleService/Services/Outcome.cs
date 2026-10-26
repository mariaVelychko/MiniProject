namespace ScheduleService.Services;

public enum OutcomeKind { Ok, Created, NoContent, NotFound, BadRequest, Conflict, Unavailable }

/// <summary>Результат бізнес-операції. Контролер лише перекладає його в HTTP-відповідь.</summary>
public sealed record Outcome<T>(OutcomeKind Kind, T? Value, string? Message)
{
    public static Outcome<T> Ok(T value) => new(OutcomeKind.Ok, value, null);
    public static Outcome<T> Created(T value) => new(OutcomeKind.Created, value, null);
    public static Outcome<T> NoContent() => new(OutcomeKind.NoContent, default, null);
    public static Outcome<T> Fail(OutcomeKind kind, string message) => new(kind, default, message);
}

/// <summary>Помилка без значення; перетворюється на <see cref="Outcome{T}"/> потрібного типу.</summary>
public sealed record Failure(OutcomeKind Kind, string Message)
{
    public Outcome<T> To<T>() => Outcome<T>.Fail(Kind, Message);
}

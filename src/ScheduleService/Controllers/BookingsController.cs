using Microsoft.AspNetCore.Mvc;
using ScheduleService.Contracts;
using ScheduleService.Infrastructure;
using ScheduleService.Services;

namespace ScheduleService.Controllers;

/// <summary>Записи клієнтів на тренування. Маршрути й відповіді збігаються з моноліту.</summary>
[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
public class BookingsController(IBookingService bookings) : ApiControllerBase
{
    /// <summary>Тренування, на які записаний клієнт (за часом).</summary>
    [HttpGet("client/{clientId:int}")]
    [ProducesResponseType(typeof(IEnumerable<WorkoutResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<WorkoutResponse>>> GetByClient(int clientId, CancellationToken ct) =>
        Map(await bookings.GetByClientAsync(clientId, ct));

    /// <summary>Учасники тренування.</summary>
    [HttpGet("workout/{workoutId:int}")]
    [ProducesResponseType(typeof(IEnumerable<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetByWorkout(int workoutId, CancellationToken ct) =>
        Map(await bookings.GetParticipantsAsync(workoutId, ct));

    /// <summary>Записати клієнта на тренування. Підтримує заголовок Idempotency-Key.</summary>
    /// <param name="request">Тренування й клієнт.</param>
    /// <param name="idempotencyKey">Унікальний ключ запиту; повтор із тим самим ключем не створює другого запису.</param>
    [HttpPost]
    [ServiceFilter(typeof(IdempotencyFilter))]
    [ProducesResponseType(typeof(WorkoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WorkoutResponse>> Book(
        BookingRequest request,
        [FromHeader(Name = IdempotencyFilter.HeaderName)] string? idempotencyKey,
        CancellationToken ct) =>
        Map(await bookings.BookAsync(request, ct));

    /// <summary>Скасувати запис клієнта (запис видаляється, як і в моноліті).</summary>
    [HttpDelete("{workoutId:int}/{clientId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int workoutId, int clientId, CancellationToken ct) =>
        Map(await bookings.CancelAsync(workoutId, clientId, ct));
}

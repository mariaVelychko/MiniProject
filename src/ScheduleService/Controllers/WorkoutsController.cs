using Microsoft.AspNetCore.Mvc;
using ScheduleService.Contracts;
using ScheduleService.Infrastructure;
using ScheduleService.Services;

namespace ScheduleService.Controllers;

/// <summary>Розклад тренувань. Маршрути й відповіді збігаються з моноліту.</summary>
[ApiController]
[Route("api/workouts")]
public class WorkoutsController(IWorkoutService workouts) : ApiControllerBase
{
    /// <summary>Список тренувань, відсортований за часом.</summary>
    /// <param name="coachId">Лише тренування цього тренера.</param>
    /// <param name="catalogItemId">Лише тренування цього типу (позиція каталогу).</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<WorkoutResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<WorkoutResponse>>> GetAll(
        [FromQuery] int? coachId, [FromQuery] int? catalogItemId, CancellationToken ct) =>
        Map(await workouts.ListAsync(coachId, catalogItemId, ct));

    /// <summary>Одне тренування.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WorkoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkoutResponse>> GetById(int id, CancellationToken ct) =>
        Map(await workouts.GetAsync(id, ct));

    /// <summary>Створити тренування. Підтримує заголовок Idempotency-Key.</summary>
    /// <param name="request">Дані тренування.</param>
    /// <param name="idempotencyKey">Унікальний ключ запиту; повторний запит із тим самим ключем поверне першу відповідь.</param>
    [HttpPost]
    [ServiceFilter(typeof(IdempotencyFilter))]
    [ProducesResponseType(typeof(WorkoutResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WorkoutResponse>> Create(
        WorkoutRequest request,
        [FromHeader(Name = IdempotencyFilter.HeaderName)] string? idempotencyKey,
        CancellationToken ct)
    {
        var outcome = await workouts.CreateAsync(request, ct);

        return outcome.Kind == OutcomeKind.Created
            ? CreatedAtAction(nameof(GetById), new { id = outcome.Value!.Id }, outcome.Value)
            : Map(outcome);
    }

    /// <summary>Змінити тренування.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(WorkoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WorkoutResponse>> Update(int id, WorkoutRequest request, CancellationToken ct) =>
        Map(await workouts.UpdateAsync(id, request, ct));

    /// <summary>Видалити тренування разом із записами на нього.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Map(await workouts.DeleteAsync(id, ct));
}

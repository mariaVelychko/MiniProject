using Microsoft.AspNetCore.Mvc;
using ScheduleService.Contracts;
using ScheduleService.Services;

namespace ScheduleService.Controllers;

/// <summary>Переклад <see cref="Outcome{T}"/> у HTTP-відповідь у форматі моноліту ({"message": "..."}).</summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult Map<T>(Outcome<T> outcome) => outcome.Kind switch
    {
        OutcomeKind.Ok or OutcomeKind.Created => Ok(outcome.Value),
        OutcomeKind.NoContent => NoContent(),
        OutcomeKind.NotFound => NotFound(new ErrorResponse(outcome.Message!)),
        OutcomeKind.BadRequest => BadRequest(new ErrorResponse(outcome.Message!)),
        OutcomeKind.Conflict => Conflict(new ErrorResponse(outcome.Message!)),
        _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse(outcome.Message!)),
    };
}

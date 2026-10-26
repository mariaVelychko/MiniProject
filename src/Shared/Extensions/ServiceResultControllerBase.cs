using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Common;

namespace Shared.Extensions
{
    public abstract class ServiceResultControllerBase : ControllerBase
    {
        protected ActionResult<T> FromResult<T>(ServiceResult<T> result, Func<T, ActionResult<T>>? onSuccess = null)
        {
            if (result.IsSuccess)
            {
                return onSuccess is not null ? onSuccess(result.Value!) : Ok(result.Value);
            }

            return ToErrorResult(result.ErrorType!.Value, result.Error!);
        }

        protected IActionResult FromResult(ServiceResult result, Func<IActionResult>? onSuccess = null)
        {
            if (result.IsSuccess)
            {
                return onSuccess is not null ? onSuccess() : NoContent();
            }

            return ToErrorResult(result.ErrorType!.Value, result.Error!);
        }

        private ActionResult ToErrorResult(ServiceErrorType type, string error)
        {
            var problem = new ProblemDetails { Detail = error };

            return type switch
            {
                ServiceErrorType.Validation => BadRequest(problem),
                ServiceErrorType.NotFound => NotFound(problem),
                ServiceErrorType.Conflict => Conflict(problem),
                ServiceErrorType.Unauthorized => Unauthorized(problem),
                ServiceErrorType.Forbidden => new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden },
                _ => StatusCode(StatusCodes.Status500InternalServerError, problem),
            };
        }
    }
}

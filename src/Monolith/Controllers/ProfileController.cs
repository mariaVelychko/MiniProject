using Microsoft.AspNetCore.Mvc;
using SportClub.Api.Contracts;
using SportClub.Api.Services;
using SportClubLibrary.Models;

namespace SportClub.Api.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly IAsyncRepository<Person> _users;
    private readonly IScheduleClient _schedule;

    public ProfileController(IAsyncRepository<Person> users, IScheduleClient schedule)
    {
        _users = users;
        _schedule = schedule;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ProfileResponse>> Get(int userId, CancellationToken cancellationToken)
    {
        var person = await _users.GetByIdAsync(userId);

        if (person is null)
        {
            return NotFound(new ErrorResponse($"Користувача {userId} не знайдено"));
        }

        // Після ЛР2 розклад і записи належать Schedule-сервісу: профіль збирається мережевим викликом.
        try
        {
            var own = person is Trainer trainer
                ? await _schedule.GetByCoachAsync(trainer.Id, cancellationToken)
                : await _schedule.GetByClientAsync(person.Id, cancellationToken);

            return Ok(new ProfileResponse(person.ToResponse(), own.ToList()));
        }
        catch (ScheduleUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("Сервіс розкладу недоступний. Спробуйте пізніше."));
        }
    }
}

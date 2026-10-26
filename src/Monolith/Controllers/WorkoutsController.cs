using Microsoft.AspNetCore.Mvc;
using SportClub.Api.Contracts;
using SportClub.Api.Services;
using SportClubLibrary.Models;

namespace SportClub.Api.Controllers;

[ApiController]
[Route("api/workouts")]
public class WorkoutsController : ControllerBase
{
    private readonly IAsyncRepository<Workout> _workouts;
    private readonly IAsyncRepository<Person> _users;
    private readonly ICatalogClient _catalog;

    public WorkoutsController(IAsyncRepository<Workout> workouts, IAsyncRepository<Person> users, ICatalogClient catalog)
    {
        _workouts = workouts;
        _users = users;
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkoutResponse>>> GetAll([FromQuery] int? coachId, [FromQuery] int? catalogItemId)
    {
        var workouts = await _workouts.GetAllAsync();

        if (coachId is not null)
        {
            workouts = workouts.Where(w => w.Coach != null && w.Coach.Id == coachId);
        }

        if (catalogItemId is not null)
        {
            workouts = workouts.Where(w => w.CatalogItemId == catalogItemId);
        }

        return Ok(workouts.OrderBy(w => w.Time).Select(w => w.ToResponse()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkoutResponse>> GetById(int id)
    {
        var workout = await _workouts.GetByIdAsync(id);

        return workout is null
            ? NotFound(new ErrorResponse($"Тренування {id} не знайдено"))
            : Ok(workout.ToResponse());
    }

 
    [HttpPost]
    public async Task<ActionResult<WorkoutResponse>> Create(WorkoutRequest request)
    {
        var coach = await FindCoachAsync(request.CoachId);

        if (coach is null)
        {
            return BadRequest(new ErrorResponse($"Тренера {request.CoachId} не знайдено"));
        }

        var (item, error) = await ResolveCatalogItemAsync(request.CatalogItemId);

        if (error is not null)
        {
            return error;
        }

        var invalid = Validate(request, item!, out var duration, out var maxParticipants);

        if (invalid is not null)
        {
            return invalid;
        }

        var workout = new Workout
        {
            CatalogItemId = item!.Id,
            Type = item.Title,
            Time = request.Time,
            DurationMin = duration,
            Hall = request.Hall,
            MaxParticipants = maxParticipants,
            Coach = coach,
        };

        await _workouts.AddAsync(workout);

        return CreatedAtAction(nameof(GetById), new { id = workout.Id }, workout.ToResponse());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkoutResponse>> Update(int id, WorkoutRequest request)
    {
        var workout = await _workouts.GetByIdAsync(id);

        if (workout is null)
        {
            return NotFound(new ErrorResponse($"Тренування {id} не знайдено"));
        }

        var coach = await FindCoachAsync(request.CoachId);

        if (coach is null)
        {
            return BadRequest(new ErrorResponse($"Тренера {request.CoachId} не знайдено"));
        }

        var (item, error) = await ResolveCatalogItemAsync(request.CatalogItemId);

        if (error is not null)
        {
            return error;
        }

        var invalid = Validate(request, item!, out var duration, out var maxParticipants);

        if (invalid is not null)
        {
            return invalid;
        }

        if (maxParticipants < workout.Participants.Count)
        {
            return Conflict(new ErrorResponse("Нова місткість менша за кількість записаних клієнтів"));
        }

        workout.CatalogItemId = item!.Id;
        workout.Type = item.Title;
        workout.Time = request.Time;
        workout.DurationMin = duration;
        workout.Hall = request.Hall;
        workout.MaxParticipants = maxParticipants;
        workout.Coach = coach;

        await _workouts.UpdateAsync(workout);

        return Ok(workout.ToResponse());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var workout = await _workouts.GetByIdAsync(id);

        if (workout is null)
        {
            return NotFound(new ErrorResponse($"Тренування {id} не знайдено"));
        }

        await _workouts.DeleteAsync(id);

        return NoContent();
    }

    private async Task<(CatalogItemInfo? Item, ActionResult? Error)> ResolveCatalogItemAsync(int catalogItemId)
    {
        try
        {
            var item = await _catalog.GetItemAsync(catalogItemId);

            if (item is null)
            {
                return (null, BadRequest(new ErrorResponse($"Тип тренування {catalogItemId} не знайдено в каталозі")));
            }

            if (!item.IsActive)
            {
                return (null, BadRequest(new ErrorResponse($"Тип тренування \"{item.Title}\" вилучено з каталогу")));
            }

            return (item, null);
        }
        catch (CatalogUnavailableException)
        {
            return (null, StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("Каталог типів тренувань недоступний. Спробуйте пізніше.")));
        }
    }

    private ActionResult? Validate(WorkoutRequest request, CatalogItemInfo item, out int duration, out int maxParticipants)
    {
        duration = request.DurationMin ?? item.DurationMin;
        maxParticipants = request.MaxParticipants ?? item.Capacity;

        if (string.IsNullOrWhiteSpace(request.Hall))
        {
            return BadRequest(new ErrorResponse("Вкажіть зал (hall)"));
        }

        if (duration <= 0 || maxParticipants <= 0)
        {
            return BadRequest(new ErrorResponse("Тривалість і місткість мають бути додатними"));
        }

        return null;
    }

    private async Task<Trainer?> FindCoachAsync(int coachId)
    {
        var people = await _users.GetAllAsync();

        return people.OfType<Trainer>().FirstOrDefault(t => t.Id == coachId);
    }
}

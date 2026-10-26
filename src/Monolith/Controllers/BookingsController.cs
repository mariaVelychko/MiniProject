using Microsoft.AspNetCore.Mvc;
using SportClub.Api.Contracts;
using SportClubLibrary.Models;

namespace SportClub.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly IAsyncRepository<Workout> _workouts;
    private readonly IAsyncRepository<Person> _users;

    public BookingsController(IAsyncRepository<Workout> workouts, IAsyncRepository<Person> users)
    {
        _workouts = workouts;
        _users = users;
    }

    [HttpGet("client/{clientId:int}")]
    public async Task<ActionResult<IEnumerable<WorkoutResponse>>> GetByClient(int clientId)
    {
        var workouts = await _workouts.GetAllAsync();

        return Ok(workouts
            .Where(w => w.Participants.Any(p => p.Id == clientId))
            .OrderBy(w => w.Time)
            .Select(w => w.ToResponse()));
    }

    [HttpGet("workout/{workoutId:int}")]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetByWorkout(int workoutId)
    {
        var workout = await _workouts.GetByIdAsync(workoutId);

        return workout is null
            ? NotFound(new ErrorResponse($"Тренування {workoutId} не знайдено"))
            : Ok(workout.Participants.Select(p => p.ToResponse()));
    }

    [HttpPost]
    public async Task<ActionResult<WorkoutResponse>> Book(BookingRequest request)
    {
        var workout = await _workouts.GetByIdAsync(request.WorkoutId);

        if (workout is null)
        {
            return NotFound(new ErrorResponse($"Тренування {request.WorkoutId} не знайдено"));
        }

        var people = await _users.GetAllAsync();
        var client = people.OfType<Client>().FirstOrDefault(c => c.Id == request.ClientId);

        if (client is null)
        {
            return NotFound(new ErrorResponse($"Клієнта {request.ClientId} не знайдено"));
        }

        if (workout.Participants.Any(p => p.Id == client.Id))
        {
            return Conflict(new ErrorResponse("Клієнт уже записаний на це тренування"));
        }

        if (workout.Participants.Count >= workout.MaxParticipants)
        {
            return Conflict(new ErrorResponse("Вільних місць немає"));
        }

        workout.Participants.Add(client);
        await _workouts.UpdateAsync(workout);

        return Ok(workout.ToResponse());
    }

    [HttpDelete("{workoutId:int}/{clientId:int}")]
    public async Task<IActionResult> Cancel(int workoutId, int clientId)
    {
        var workout = await _workouts.GetByIdAsync(workoutId);

        if (workout is null)
        {
            return NotFound(new ErrorResponse($"Тренування {workoutId} не знайдено"));
        }

        var participant = workout.Participants.FirstOrDefault(p => p.Id == clientId);

        if (participant is null)
        {
            return NotFound(new ErrorResponse("Клієнт не записаний на це тренування"));
        }

        workout.Participants.Remove(participant);
        await _workouts.UpdateAsync(workout);

        return NoContent();
    }
}

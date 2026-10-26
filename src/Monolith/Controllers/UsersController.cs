using Microsoft.AspNetCore.Mvc;
using SportClub.Api.Contracts;
using SportClubLibrary.Models;
using SportClubLibrary.Utilities;

namespace SportClub.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IAsyncRepository<Person> _users;

    public UsersController(IAsyncRepository<Person> users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetAll([FromQuery] string? role)
    {
        var people = await _users.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(role))
        {
            people = people.Where(p => p.RoleKey() == role.ToLowerInvariant());
        }

        return Ok(people.Select(p => p.ToResponse()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> GetById(int id)
    {
        var person = await _users.GetByIdAsync(id);

        return person is null
            ? NotFound(new ErrorResponse($"Користувача {id} не знайдено"))
            : Ok(person.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        Person? person = request.Role?.ToLowerInvariant() switch
        {
            "client" => new Client(),
            "trainer" => new Trainer { Specialization = request.Specialization ?? string.Empty },
            "admin" => new Admin(),
            _ => null,
        };

        if (person is null)
        {
            return BadRequest(new ErrorResponse("Роль має бути client, trainer або admin"));
        }

        var people = await _users.GetAllAsync();

        if (people.Any(p => p.Email == request.Email))
        {
            return Conflict(new ErrorResponse("Користувач з таким email вже існує"));
        }

        person.FullName = request.FullName;
        person.Email = request.Email;
        person.Phone = request.Phone;
        person.Password = PasswordHasher.Hash(request.Password);

        await _users.AddAsync(person);

        return CreatedAtAction(nameof(GetById), new { id = person.Id }, person.ToResponse());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserResponse>> Update(int id, UpdateUserRequest request)
    {
        var person = await _users.GetByIdAsync(id);

        if (person is null)
        {
            return NotFound(new ErrorResponse($"Користувача {id} не знайдено"));
        }

        person.FullName = request.FullName;
        person.Email = request.Email;
        person.Phone = request.Phone;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            person.Password = PasswordHasher.Hash(request.Password);
        }

        if (person is Trainer trainer && request.Specialization is not null)
        {
            trainer.Specialization = request.Specialization;
        }

        await _users.UpdateAsync(person);

        return Ok(person.ToResponse());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var person = await _users.GetByIdAsync(id);

        if (person is null)
        {
            return NotFound(new ErrorResponse($"Користувача {id} не знайдено"));
        }

        await _users.DeleteAsync(id);

        return NoContent();
    }
}

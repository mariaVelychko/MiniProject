using Microsoft.AspNetCore.Mvc;
using SportClub.Api.Contracts;
using SportClubLibrary.Services;

namespace SportClub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService) => _authService = authService;

    [HttpPost("login")]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new ErrorResponse("Введіть логін та пароль"));
        }

        var user = await _authService.LoginAsync(request.Email, request.Password);

        return user is null
            ? Unauthorized(new ErrorResponse("Невірний логін або пароль"))
            : Ok(user.ToResponse());
    }

    [HttpGet("roles")]
    public ActionResult<IEnumerable<string>> Roles() => Ok(new[] { "client", "trainer", "admin" });
}

using AuthService.Dtos;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Extensions;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public class AuthController : ServiceResultControllerBase
    {
        private readonly AuthenticationService _auth;

        public AuthController(AuthenticationService auth) => _auth = auth;

        [HttpPost("register")]
        [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request)
        {
            var result = await _auth.RegisterAsync(request);
            return FromResult(result, value => StatusCode(StatusCodes.Status201Created, value));
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenResponse>> Login(LoginRequest request)
        {
            var result = await _auth.LoginAsync(request);
            return FromResult(result);
        }

        [HttpPost("refresh")]
        [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request)
        {
            var result = await _auth.RefreshAsync(request.RefreshToken);
            return FromResult(result);
        }

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Logout(LogoutRequest request)
        {
            var result = await _auth.LogoutAsync(request.RefreshToken);
            return FromResult(result);
        }
    }
}

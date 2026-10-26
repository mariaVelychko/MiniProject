using System.Security.Claims;
using AuthService.Dtos;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Dtos;
using Shared.Extensions;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/v1/users")]
    [Authorize]
    public class UsersController : ServiceResultControllerBase
    {
        private readonly UserService _userService;

        public UsersController(UserService userService) => _userService = userService;

        [HttpGet("me")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UserProfileDto>> GetMe()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _userService.GetMeAsync(userId);
            return FromResult(result);
        }

        [HttpGet]
        [Authorize(Roles = Role.Admin)]
        [ProducesResponseType(typeof(PagedResult<UserListItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PagedResult<UserListItemDto>>> GetAll(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _userService.GetUsersPagedAsync(page, pageSize);
            return FromResult(result);
        }

        [HttpPut("{id:int}/roles")]
        [Authorize(Roles = Role.Admin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateRoles(int id, UpdateRolesRequest request)
        {
            var result = await _userService.UpdateRolesAsync(id, request.Roles);
            return FromResult(result);
        }
    }
}

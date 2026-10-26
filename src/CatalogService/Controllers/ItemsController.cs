using CatalogService.Dtos;
using CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Dtos;
using Shared.Extensions;

namespace CatalogService.Controllers
{
    [ApiController]
    [Route("api/v1/catalog/items")]
    public class ItemsController : ServiceResultControllerBase
    {
        private readonly CatalogItemService _catalog;

        public ItemsController(CatalogItemService catalog) => _catalog = catalog;

        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(PagedResult<ItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PagedResult<ItemDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] bool desc = false)
        {
            var result = await _catalog.GetPagedAsync(page, pageSize, search, categoryId, sortBy, desc);
            return FromResult(result);
        }

        [HttpGet("{id:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ItemDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ItemDetailsDto>> GetById(int id)
        {
            var result = await _catalog.GetByIdAsync(id);
            return FromResult(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ItemDetailsDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<ItemDetailsDto>> Create(CreateItemRequest request)
        {
            var result = await _catalog.CreateAsync(request);
            return FromResult(result, value => CreatedAtAction(nameof(GetById), new { id = value.Id }, value));
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(int id, UpdateItemRequest request)
        {
            var result = await _catalog.UpdateAsync(id, request);
            return FromResult(result);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _catalog.DeleteAsync(id);
            return FromResult(result);
        }
    }
}

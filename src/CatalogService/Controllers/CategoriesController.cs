using CatalogService.Dtos;
using CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Controllers
{
    [ApiController]
    [Route("api/v1/catalog/categories")]
    [AllowAnonymous]
    public class CategoriesController : ControllerBase
    {
        private readonly CatalogItemService _catalog;

        public CategoriesController(CatalogItemService catalog) => _catalog = catalog;

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll() =>
            Ok(await _catalog.GetCategoriesAsync());
    }
}

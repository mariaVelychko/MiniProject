using CatalogService.Dtos;
using CatalogService.Models;
using CatalogService.Repositories;
using Shared.Common;
using Shared.Dtos;

namespace CatalogService.Services
{
    public class CatalogItemService
    {
        private const int MaxPageSize = 100;

        private readonly IItemRepository _items;
        private readonly ICategoryRepository _categories;
        private readonly ILogger<CatalogItemService> _logger;

        public CatalogItemService(IItemRepository items, ICategoryRepository categories, ILogger<CatalogItemService> logger)
        {
            _items = items;
            _categories = categories;
            _logger = logger;
        }

        public async Task<ServiceResult<PagedResult<ItemDto>>> GetPagedAsync(
            int page, int pageSize, string? search, int? categoryId, string? sortBy, bool desc)
        {
            if (page < 1)
            {
                return ServiceResult<PagedResult<ItemDto>>.Failure(
                    ServiceErrorType.Validation, "page має бути не менше 1.");
            }

            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                return ServiceResult<PagedResult<ItemDto>>.Failure(
                    ServiceErrorType.Validation, $"pageSize має бути від 1 до {MaxPageSize}.");
            }

            var (entities, total) = await _items.GetPagedAsync(new ItemQuery(page, pageSize, search, categoryId, sortBy, desc));

            var dto = new PagedResult<ItemDto>
            {
                Items = entities.Select(ToDto).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
            };

            return ServiceResult<PagedResult<ItemDto>>.Success(dto);
        }

        public async Task<ServiceResult<ItemDetailsDto>> GetByIdAsync(int id)
        {
            var item = await _items.GetByIdWithCategoryAsync(id);

            return item is null
                ? ServiceResult<ItemDetailsDto>.Failure(ServiceErrorType.NotFound, $"Позицію {id} не знайдено.")
                : ServiceResult<ItemDetailsDto>.Success(ToDetailsDto(item));
        }

        public async Task<ServiceResult<ItemDetailsDto>> CreateAsync(CreateItemRequest request)
        {
            if (!await _categories.ExistsAsync(request.CategoryId))
            {
                return ServiceResult<ItemDetailsDto>.Failure(
                    ServiceErrorType.Validation, $"Категорію {request.CategoryId} не знайдено.");
            }

            var item = new Item
            {
                Title = request.Title,
                Description = request.Description,
                Price = request.Price,
                DurationMin = request.DurationMin,
                Capacity = request.Capacity,
                CategoryId = request.CategoryId,
            };

            await _items.AddAsync(item);

            _logger.LogInformation("Створено позицію каталогу {ItemId} \"{Title}\" (ціна {Price})", item.Id, item.Title, item.Price);

            var withCategory = await _items.GetByIdWithCategoryAsync(item.Id) ?? item;
            return ServiceResult<ItemDetailsDto>.Success(ToDetailsDto(withCategory));
        }

        public async Task<ServiceResult> UpdateAsync(int id, UpdateItemRequest request)
        {
            var item = await _items.GetByIdWithCategoryAsync(id);

            if (item is null)
            {
                return ServiceResult.Failure(ServiceErrorType.NotFound, $"Позицію {id} не знайдено.");
            }

            if (!await _categories.ExistsAsync(request.CategoryId))
            {
                return ServiceResult.Failure(
                    ServiceErrorType.Validation, $"Категорію {request.CategoryId} не знайдено.");
            }

            var oldPrice = item.Price;

            item.Title = request.Title;
            item.Description = request.Description;
            item.Price = request.Price;
            item.DurationMin = request.DurationMin;
            item.Capacity = request.Capacity;
            item.CategoryId = request.CategoryId;
            item.IsActive = request.IsActive;

            await _items.UpdateAsync(item);

            _logger.LogInformation("Оновлено позицію каталогу {ItemId}; ціна {OldPrice} -> {NewPrice}", id, oldPrice, item.Price);

            return ServiceResult.Success();
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var item = await _items.GetByIdAsync(id);

            if (item is null)
            {
                return ServiceResult.Failure(ServiceErrorType.NotFound, $"Позицію {id} не знайдено.");
            }

            item.IsActive = false;
            await _items.UpdateAsync(item);

            _logger.LogInformation("М'яко видалено позицію каталогу {ItemId} (IsActive = false)", id);

            return ServiceResult.Success();
        }

        public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync() =>
            (await _categories.GetAllAsync()).Select(c => new CategoryDto(c.Id, c.Name)).ToList();

        private static ItemDto ToDto(Item item) =>
            new(item.Id, item.Title, item.Price, item.DurationMin, item.Category.Name, item.IsActive);

        private static ItemDetailsDto ToDetailsDto(Item item) => new(
            item.Id, item.Title, item.Description, item.Price, item.DurationMin, item.Capacity,
            item.IsActive, item.CreatedAt, item.CategoryId, item.Category.Name);
    }
}

using System.ComponentModel.DataAnnotations;

namespace CatalogService.Dtos
{
    public record ItemDto(int Id, string Title, decimal Price, int DurationMin, string CategoryName, bool IsActive);

    public record ItemDetailsDto(
        int Id, string Title, string Description, decimal Price,
        int DurationMin, int Capacity, bool IsActive, DateTime CreatedAt,
        int CategoryId, string CategoryName);

    public record CategoryDto(int Id, string Name);

    public class CreateItemRequest
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Range(0, 1_000_000)]
        public decimal Price { get; set; }

        [Range(1, 24 * 60)]
        public int DurationMin { get; set; }

        [Range(1, 1000)]
        public int Capacity { get; set; }

        [Required]
        public int CategoryId { get; set; }
    }

    public class UpdateItemRequest
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Range(0, 1_000_000)]
        public decimal Price { get; set; }

        [Range(1, 24 * 60)]
        public int DurationMin { get; set; }

        [Range(1, 1000)]
        public int Capacity { get; set; }

        [Required]
        public int CategoryId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}

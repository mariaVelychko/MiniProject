namespace CatalogService.Models
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public List<Item> Items { get; set; } = new();
    }
}

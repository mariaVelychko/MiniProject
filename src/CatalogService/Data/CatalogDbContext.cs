using CatalogService.Models;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Data
{
    public class CatalogDbContext : DbContext
    {
        private readonly string _connectionString;

        public CatalogDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        public DbSet<Item> Items => Set<Item>();
        public DbSet<Category> Categories => Set<Category>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var serverVersion = new MySqlServerVersion(new Version(8, 0, 39));
                optionsBuilder.UseMySql(_connectionString, serverVersion);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Category>(entity =>
            {
                entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
                entity.HasIndex(c => c.Name).IsUnique();

                entity.HasData(
                    new Category { Id = 1, Name = "Йога" },
                    new Category { Id = 2, Name = "Кросфіт" },
                    new Category { Id = 3, Name = "Бокс" });
            });

            modelBuilder.Entity<Item>(entity =>
            {
                entity.Property(i => i.Title).IsRequired().HasMaxLength(200);
                entity.HasIndex(i => i.Title);

                entity.Property(i => i.Description).HasMaxLength(2000);
                entity.Property(i => i.Price).HasPrecision(10, 2);

                entity.HasOne(i => i.Category)
                    .WithMany(c => c.Items)
                    .HasForeignKey(i => i.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}

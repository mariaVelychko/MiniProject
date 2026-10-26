using Microsoft.EntityFrameworkCore;
using ScheduleService.Models;

namespace ScheduleService.Data;

public class ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : DbContext(options)
{
    public DbSet<Workout> Workouts => Set<Workout>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Workout>(e =>
        {
            e.ToTable("Workouts");
            e.HasKey(w => w.Id);
            e.Property(w => w.Type).IsRequired().HasMaxLength(200);
            e.Property(w => w.Hall).IsRequired().HasMaxLength(200);
            e.Property(w => w.CoachName).HasMaxLength(200);
            e.Property(w => w.PriceSnapshot).HasPrecision(10, 2);
            e.HasIndex(w => w.Time);
            e.HasIndex(w => w.CoachId);
            e.HasIndex(w => w.CatalogItemId);

            // Єдиний FK — усередині власної БД. Жодних зв'язків із users/catalog.
            e.HasMany(w => w.Bookings)
                .WithOne()
                .HasForeignKey(x => x.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Booking>(e =>
        {
            e.ToTable("Bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientFullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.ClientEmail).IsRequired().HasMaxLength(256);
            e.Property(x => x.ClientPhone).IsRequired().HasMaxLength(64);
            e.HasIndex(x => new { x.WorkoutId, x.ClientId }).IsUnique();
            e.HasIndex(x => x.ClientId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Fingerprint).IsRequired().HasMaxLength(64);
            e.Property(x => x.Location).HasMaxLength(512);
            e.Property(x => x.ResponseBody).HasColumnType("longtext");
        });
    }
}

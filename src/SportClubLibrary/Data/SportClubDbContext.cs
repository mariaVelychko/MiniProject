using Microsoft.EntityFrameworkCore;
using SportClubLibrary.Models;

namespace SportClubLibrary.Data
{
    public class SportClubDbContext : DbContext
    {
        private readonly string _connectionString;

        public SportClubDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        public DbSet<Person> Persons => Set<Person>();
        public DbSet<Workout> Workouts => Set<Workout>();

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
            modelBuilder.Entity<Person>(entity =>
            {
                entity.ToTable("Persons");

                entity.HasDiscriminator<string>("Role")
                    .HasValue<Client>("client")
                    .HasValue<Trainer>("trainer")
                    .HasValue<Admin>("admin");

                entity.Property(p => p.FullName).IsRequired();
                entity.Property(p => p.Email).IsRequired().HasMaxLength(256);
                entity.HasIndex(p => p.Email).IsUnique();
            });

            modelBuilder.Entity<Workout>(entity =>
            {
                entity.ToTable("Workouts");

                entity.HasOne(w => w.Coach)
                    .WithMany()
                    .HasForeignKey("CoachId")
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(w => w.Participants)
                    .WithMany()
                    .UsingEntity(j => j.ToTable("WorkoutParticipants"));
            });
        }
    }
}

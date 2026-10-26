using Microsoft.EntityFrameworkCore;
using SportClubLibrary.Data;
using SportClubLibrary.Interfaces;
using SportClubLibrary.Models;
using SportClubLibrary.Utilities;

namespace SportClubLibrary.Repositories
{
   
    public static class RepositoryFactory
    {
        public static IAsyncRepository<Person> CreatePersonRepository(
            Func<SportClubDbContext> contextFactory, ILogger logger)
        {
            return new DbRepository<Person>(contextFactory, logger);
        }

        public static IAsyncRepository<Workout> CreateWorkoutRepository(
            Func<SportClubDbContext> contextFactory, ILogger logger)
        {
            return new DbRepository<Workout>(
                contextFactory,
                logger,
                include: q => q.Include(w => w.Coach).Include(w => w.Participants),
                attachRelated: (db, workout) =>
                {
                    if (workout.Coach != null)
                    {
                        db.Attach(workout.Coach);
                    }

                    foreach (var participant in workout.Participants)
                    {
                        db.Attach(participant);
                    }
                },
                applyUpdate: async (db, existing, incoming) =>
                {
                    existing.Type = incoming.Type;
                    existing.CatalogItemId = incoming.CatalogItemId;
                    existing.Time = incoming.Time;
                    existing.DurationMin = incoming.DurationMin;
                    existing.Hall = incoming.Hall;
                    existing.MaxParticipants = incoming.MaxParticipants;

                    existing.Coach = incoming.Coach != null
                        ? await db.Set<Person>().OfType<Trainer>()
                            .FirstOrDefaultAsync(t => t.Id == incoming.Coach.Id)
                        : null;

                    existing.Participants.Clear();

                    foreach (var participant in incoming.Participants)
                    {
                        var tracked = await db.Set<Person>().OfType<Client>()
                            .FirstOrDefaultAsync(c => c.Id == participant.Id);

                        if (tracked != null)
                        {
                            existing.Participants.Add(tracked);
                        }
                    }
                });
        }
    }
}

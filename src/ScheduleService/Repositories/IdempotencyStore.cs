using Microsoft.EntityFrameworkCore;
using ScheduleService.Data;
using ScheduleService.Models;

namespace ScheduleService.Repositories;

public sealed class IdempotencyStore(ScheduleDbContext db) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct) =>
        db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(record);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Паралельний запит із тим самим ключем уже зберіг відповідь — це нормально.
            db.Entry(record).State = EntityState.Detached;
        }
    }
}

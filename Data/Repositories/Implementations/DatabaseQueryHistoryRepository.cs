using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class DatabaseQueryHistoryRepository : IDatabaseQueryHistoryRepository
{
    private readonly PgDbContext _context;

    public DatabaseQueryHistoryRepository(PgDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(DatabaseQueryHistory entry)
    {
        await _context.DatabaseQueryHistory.AddAsync(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<DatabaseQueryHistory>> GetRecentByUserAndInstanceAsync(Guid userId, Guid instanceId, int limit)
    {
        return await _context.DatabaseQueryHistory
            .Include(q => q.Instance)
            .Where(q => q.UserId == userId && q.InstanceId == instanceId)
            .OrderByDescending(q => q.ExecutedAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }
}


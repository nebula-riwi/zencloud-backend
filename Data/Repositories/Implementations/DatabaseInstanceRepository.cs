using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class DatabaseInstanceRepository : Repository<DatabaseInstance>, IDatabaseInstanceRepository 
{
    public DatabaseInstanceRepository(PgDbContext context) : base(context)
    {
    }
    
    public async Task<IEnumerable<DatabaseInstance>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(db => db.UserId == userId && db.Status == DatabaseInstanceStatus.Active)
            .Include(db => db.Engine)
            .ToListAsync();
    }

    public async Task<int> CountByUserAndEngineAsync(Guid userId, Guid engineId)
    {
        return await _dbSet
            .Where(db => db.UserId == userId 
                         && db.EngineId == engineId 
                         && db.Status == DatabaseInstanceStatus.Active)
            .CountAsync();
    }

    public async Task<bool> DatabaseNameExistsAsync(string databaseName)
    {
        return await _dbSet
            .AnyAsync(db => db.DatabaseName == databaseName 
                            && db.Status == DatabaseInstanceStatus.Active);
    }
}
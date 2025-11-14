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
    
    //Gets all databases by user Id (active and inactive, but not deleted)
    public async Task<IEnumerable<DatabaseInstance>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(db => db.UserId == userId && db.Status != DatabaseInstanceStatus.Deleted)
            .Include(db => db.Engine)
            .OrderByDescending(db => db.CreatedAt)
            .ToListAsync();
    }
    
    //Gets how many db's a user has
    public async Task<int> CountByUserAndEngineAsync(Guid userId, Guid engineId)
    {
        return await _dbSet
            .Where(db => db.UserId == userId 
                         && db.EngineId == engineId 
                         && db.Status == DatabaseInstanceStatus.Active)
            .CountAsync();
    }

    public async Task<int> CountActiveByUserAsync(Guid userId)
    {
        return await _dbSet
            .Where(db => db.UserId == userId && db.Status == DatabaseInstanceStatus.Active)
            .CountAsync();
    }

    //Looks for a database by its name
    public async Task<bool> DatabaseNameExistsAsync(string databaseName)
    {
        return await _dbSet
            .AnyAsync(db => db.DatabaseName == databaseName 
                            && db.Status == DatabaseInstanceStatus.Active);
    }
    
    //Gets instance by id along with engine
    public async Task<DatabaseInstance?> GetByIdWithEngineAsync(Guid instanceId)
    {
        return await _dbSet
            .Include(instance => instance.Engine)
            .FirstOrDefaultAsync(instance => instance.InstanceId == instanceId);
    }
}
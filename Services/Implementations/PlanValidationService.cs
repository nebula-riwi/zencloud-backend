using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class PlanValidationService : IPlanValidationService
{
    private readonly IDatabaseInstanceRepository _databaseRepository;
    private readonly PgDbContext _context;

    PlanValidationService(
        IDatabaseInstanceRepository databaseRepository, 
        PgDbContext dbContext)
    {
        _databaseRepository = databaseRepository;
        _context = dbContext;
    }

    public async Task<bool> CanCreateDatabaseAsync(Guid userId, Guid engineId)
    {
        var maxDatabases = await GetMaxDatabasesPerEngineAsync(userId);

        var currentCount = await _databaseRepository.CountByUserAndEngineAsync(userId, engineId);
        
        return currentCount < maxDatabases;
        
    }

    public async Task<int> GetMaxDatabasesPerEngineAsync(Guid userId)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (subscription != null)
        {
            return subscription.Plan.MaxDatabasesPerEngine;
        }

        return 2;
    }
}
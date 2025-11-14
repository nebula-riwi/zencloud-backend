using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class PlanValidationService : IPlanValidationService
{
    private readonly IDatabaseInstanceRepository _databaseRepository;
    private readonly PgDbContext _context;
    private const int FreePlanPerEngineLimit = 2;
    private const int FreePlanGlobalActiveLimit = 5;

    public PlanValidationService(
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
        
        if (currentCount >= maxDatabases)
        {
            return false;
        }

        var hasActiveSubscription = await _context.Subscriptions
            .AnyAsync(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow);

        if (!hasActiveSubscription)
        {
            var totalActive = await _databaseRepository.CountActiveByUserAsync(userId);
            if (totalActive >= FreePlanGlobalActiveLimit)
            {
                return false;
            }
        }

        return true;
        
    }

    public async Task<int> GetMaxDatabasesPerEngineAsync(Guid userId)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();

        if (subscription != null)
        {
            return subscription.Plan.MaxDatabasesPerEngine;
        }

        return FreePlanPerEngineLimit;
    }
}
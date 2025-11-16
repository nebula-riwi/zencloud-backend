using System.Linq;
using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
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
    
    public async Task<(bool CanCreate, string? ErrorMessage, int CurrentCount, int MaxCount)> CanCreateDatabaseWithDetailsAsync(Guid userId, Guid engineId)
    {
        var maxDatabases = await GetMaxDatabasesPerEngineAsync(userId);
        var currentCount = await _databaseRepository.CountByUserAndEngineAsync(userId, engineId);
        
        if (currentCount >= maxDatabases)
        {
            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .Where(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();
            
            var planName = subscription?.Plan.PlanName.ToString() ?? "Gratuito";
            return (false, $"Has alcanzado el límite de {maxDatabases} bases de datos por motor para tu plan {planName}. Tienes {currentCount}/{maxDatabases} bases activas en este motor.", currentCount, maxDatabases);
        }

        var hasActiveSubscription = await _context.Subscriptions
            .AnyAsync(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow);

        if (!hasActiveSubscription)
        {
            var totalActive = await _databaseRepository.CountActiveByUserAsync(userId);
            if (totalActive >= FreePlanGlobalActiveLimit)
            {
                return (false, $"Has alcanzado el límite global de {FreePlanGlobalActiveLimit} bases de datos activas para el plan gratuito.", totalActive, FreePlanGlobalActiveLimit);
            }
        }

        return (true, null, currentCount, maxDatabases);
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
    
    public async Task EnforcePlanLimitsAsync(Guid userId)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.UserId == userId && s.IsActive && s.EndDate > DateTime.UtcNow)
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync();
        
        int maxPerEngine = subscription?.Plan.MaxDatabasesPerEngine ?? FreePlanPerEngineLimit;
        int maxGlobal = subscription != null ? int.MaxValue : FreePlanGlobalActiveLimit;
        
        var allDatabases = await _databaseRepository.GetByUserIdAsync(userId);
        var activeDatabases = allDatabases.Where(db => db.Status == DatabaseInstanceStatus.Active).ToList();
        
        if (!activeDatabases.Any())
        {
            return; // No hay bases activas, no hay nada que desactivar
        }
        
        // Agrupar por motor y desactivar excedentes
        var groupedByEngine = activeDatabases.GroupBy(db => db.EngineId).ToList();
        var databasesToDeactivate = new List<Data.Entities.DatabaseInstance>();
        
        foreach (var engineGroup in groupedByEngine)
        {
            var engineDatabases = engineGroup.OrderByDescending(db => db.CreatedAt).ToList();
            if (engineDatabases.Count > maxPerEngine)
            {
                databasesToDeactivate.AddRange(engineDatabases.Skip(maxPerEngine));
            }
        }
        
        // Si es plan gratuito, también verificar límite global
        if (subscription == null)
        {
            var remainingActive = activeDatabases.Except(databasesToDeactivate).ToList();
            if (remainingActive.Count > maxGlobal)
            {
                var excessGlobal = remainingActive.OrderByDescending(db => db.CreatedAt).Skip(maxGlobal);
                databasesToDeactivate.AddRange(excessGlobal);
            }
        }
        
        // Desactivar bases excedentes
        foreach (var database in databasesToDeactivate)
        {
            database.Status = Data.Entities.DatabaseInstanceStatus.Inactive;
            database.UpdatedAt = DateTime.UtcNow;
            await _databaseRepository.UpdateAsync(database);
        }
    }
}
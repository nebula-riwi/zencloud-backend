using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class PlanRepository : Repository<Plan>, IPlanRepository
{
    private readonly PgDbContext _context;

    public PlanRepository(PgDbContext context) : base(context)
    {
        _context = context;
    }

    public new async Task<Plan?> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<IEnumerable<Plan>> GetAllAsync()
    {
        return await _context.Plans
            .OrderBy(p => p.PlanId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Plan>> GetActivePlansAsync()
    {
        return await _dbSet
            .Where(p => p.IsActive)
            .OrderBy(p => p.PlanId)
            .ToListAsync();
    }
    
    public async Task<Plan?> GetByPlanTypeAsync(PlanType planType)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.PlanName == planType && p.IsActive);
    }

    public async Task AddAsync(Plan plan)
    {
        await _context.Plans.AddAsync(plan);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Plan plan)
    {
        _context.Plans.Update(plan);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(int planId)
    {
        var plan = await GetByIdAsync(planId);
        if (plan == null) return false;

        _context.Plans.Remove(plan);
        await _context.SaveChangesAsync();
        return true;
    }
}
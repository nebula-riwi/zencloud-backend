using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface IPlanRepository : IRepository<Plan>
{
    Task<Plan?> GetByIdAsync(int planId);
    Task<IEnumerable<Plan>> GetAllAsync();
    Task<IEnumerable<Plan>> GetActivePlansAsync();
    Task<Plan?> GetByPlanTypeAsync(PlanType planType);
    Task AddAsync(Plan plan);
    Task UpdateAsync(Plan plan);
    Task<bool> DeleteAsync(int planId);
}
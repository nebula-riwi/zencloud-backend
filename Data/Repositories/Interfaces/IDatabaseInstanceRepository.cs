using ZenCloud.Data.Entities;
namespace ZenCloud.Data.Repositories.Interfaces;

public interface IDatabaseInstanceRepository : IRepository<DatabaseInstance>
{
    Task<IEnumerable<DatabaseInstance>> GetByUserIdAsync(Guid userId);
    Task<int> CountByUserAndEngineAsync(Guid userId, Guid engineId);
    Task<bool> DatabaseNameExistsAsync(string databaseName);
}
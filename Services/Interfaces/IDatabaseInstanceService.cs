using ZenCloud.Data.Entities;

namespace ZenCloud.Services.Interfaces;

public interface IDatabaseInstanceService
{
    Task<IEnumerable<DatabaseInstance>> GetUserDatabasesAsync(Guid userId);
    Task<DatabaseInstance?> GetDatabaseByIdAsync(Guid instanceId);
    Task<DatabaseInstance> CreateDatabaseInstanceAsync(Guid userId, Guid engineId);
    Task DeleteDatabaseInstanceAsync(Guid instanceId, Guid userId);
}
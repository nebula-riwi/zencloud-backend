using ZenCloud.Data.Entities;

namespace ZenCloud.Services.Interfaces;

public interface IDatabaseInstanceService
{
    Task<IEnumerable<DatabaseInstance>> GetUserDatabasesAsync(Guid userId);
    Task<DatabaseInstance?> GetDatabaseByIdAsync(Guid instanceId);
    Task<DatabaseInstance> CreateDatabaseInstanceAsync(Guid userId, Guid engineId, string? databaseName = null);
    Task DeleteDatabaseInstanceAsync(Guid instanceId, Guid userId);
    Task<(DatabaseInstance database, string newPassword)> RotateCredentialsAsync(Guid instanceId, Guid userId);
    Task<DatabaseInstance> ActivateDatabaseAsync(Guid instanceId, Guid userId);
    Task<DatabaseInstance> DeactivateDatabaseAsync(Guid instanceId, Guid userId);
    Task<IEnumerable<DatabaseEngine>> GetActiveEnginesAsync();
}
using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface IDatabaseQueryHistoryRepository
{
    Task AddAsync(DatabaseQueryHistory entry);
    Task<IReadOnlyList<DatabaseQueryHistory>> GetRecentByUserAndInstanceAsync(Guid userId, Guid instanceId, int limit);
}


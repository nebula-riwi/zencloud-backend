using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface IAuditLogRepository
{
    Task<List<AuditLog>> GetUserAuditLogsAsync(Guid userId, int pageSize = 50, int page = 1);
    Task<List<AuditLog>> GetDatabaseAuditLogsAsync(Guid userId, Guid? instanceId = null, int pageSize = 50, int page = 1);
    Task<int> GetUserAuditLogsCountAsync(Guid userId);
    Task<int> GetDatabaseAuditLogsCountAsync(Guid userId, Guid? instanceId = null);
}

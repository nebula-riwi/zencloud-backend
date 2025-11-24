using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;

namespace ZenCloud.Data.Repositories.Implementations;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly PgDbContext _context;

    public AuditLogRepository(PgDbContext context)
    {
        _context = context;
    }

    public async Task<List<AuditLog>> GetUserAuditLogsAsync(Guid userId, int pageSize = 50, int page = 1)
    {
        return await _context.AuditLogs
            .Where(log => log.UserId == userId && log.EntityType == AuditEntityType.User)
            .OrderByDescending(log => log.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(log => log.User)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetDatabaseAuditLogsAsync(Guid userId, Guid? instanceId = null, int pageSize = 50, int page = 1)
    {
        var query = _context.AuditLogs
            .Where(log => log.UserId == userId && log.EntityType == AuditEntityType.Database);

        if (instanceId.HasValue)
        {
            query = query.Where(log => log.EntityId == instanceId.Value);
        }

        return await query
            .OrderByDescending(log => log.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(log => log.User)
            .ToListAsync();
    }

    public async Task<int> GetUserAuditLogsCountAsync(Guid userId)
    {
        return await _context.AuditLogs
            .CountAsync(log => log.UserId == userId && log.EntityType == AuditEntityType.User);
    }

    public async Task<int> GetDatabaseAuditLogsCountAsync(Guid userId, Guid? instanceId = null)
    {
        var query = _context.AuditLogs
            .Where(log => log.UserId == userId && log.EntityType == AuditEntityType.Database);

        if (instanceId.HasValue)
        {
            query = query.Where(log => log.EntityId == instanceId.Value);
        }

        return await query.CountAsync();
    }
}

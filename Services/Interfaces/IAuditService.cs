using ZenCloud.Data.Entities;
using System;

namespace ZenCloud.Services.Interfaces
{
    public interface IAuditService
    {
        Task LogSecurityEventAsync(Guid userId, AuditAction action, string details);
        Task LogDatabaseEventAsync(Guid userId, Guid instanceId, AuditAction action, string details);
        Task LogSystemEventAsync(AuditAction action, string details);
    }
}
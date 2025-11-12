using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations
{
    public class AuditService : IAuditService
    {
        private readonly PgDbContext _context;
        private readonly ILogger<AuditService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(
            PgDbContext context,
            ILogger<AuditService> logger,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _context = context;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogSecurityEventAsync(Guid userId, AuditAction action, string details)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    AuditId = Guid.NewGuid(),
                    UserId = userId,
                    Action = action, // Asignación directa del enum AuditAction
                    EntityType = AuditEntityType.User, // Asignación directa del enum AuditEntityType
                    EntityId = userId, // EntityId es Guid, así que asignamos el userId directamente
                    IpAddress = GetClientIpAddress(),
                    UserAgent = GetUserAgent(),
                    CreatedAt = DateTime.UtcNow,
                    OldValue = details,
                    NewValue = null
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Security audit logged: {Action} for user {UserId}", action, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging security event for user {UserId}", userId);
            }
        }

        public async Task LogDatabaseEventAsync(Guid userId, Guid instanceId, AuditAction action, string details)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    AuditId = Guid.NewGuid(),
                    UserId = userId,
                    Action = action,
                    EntityType = AuditEntityType.Database,
                    EntityId = instanceId, // EntityId es Guid, asignamos instanceId
                    IpAddress = GetClientIpAddress(),
                    UserAgent = GetUserAgent(),
                    CreatedAt = DateTime.UtcNow,
                    OldValue = details,
                    NewValue = null
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Database audit logged: {Action} for instance {InstanceId}", action, instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging database event for instance {InstanceId}", instanceId);
            }
        }

        public async Task LogSystemEventAsync(AuditAction action, string details)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    AuditId = Guid.NewGuid(),
                    // UserId es nullable, así que para eventos del sistema no lo establecemos
                    UserId = null,
                    Action = action,
                    EntityType = AuditEntityType.System,
                    // EntityId es Guid, pero para sistema no tenemos un Guid específico, ¿qué asignamos?
                    // Podemos asignar un Guid vacío o crear uno constante, pero ten cuidado con la base de datos.
                    // Otra opción es cambiar el tipo de EntityId a string? en tu entidad, pero como es Guid, debemos asignar uno.
                    // Dado que no tenemos un EntityId para sistema, podríamos usar Guid.Empty o un Guid constante.
                    // Sin embargo, si en tu base de datos no se permite Guid.Empty, necesitamos una solución.
                    // Como no conozco tu modelo exacto, voy a asignar Guid.Empty. Si no funciona, tendrás que ajustarlo.
                    EntityId = Guid.Empty,
                    IpAddress = "System",
                    UserAgent = "System",
                    CreatedAt = DateTime.UtcNow,
                    OldValue = details,
                    NewValue = null
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("System audit logged: {Action}", action);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging system event");
            }
        }

        private string GetClientIpAddress()
        {
            try
            {
                return _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetUserAgent()
        {
            try
            {
                return _httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
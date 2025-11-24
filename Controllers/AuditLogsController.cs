using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.DTOs;

namespace ZenCloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(
        IAuditLogRepository auditLogRepository,
        ILogger<AuditLogsController> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }

    /// <summary>
    /// Obtiene los logs de auditoría de la cuenta del usuario (login, logout, cambios de contraseña, etc.)
    /// </summary>
    [HttpGet("account")]
    public async Task<ActionResult<AuditLogsPagedResponseDto>> GetAccountLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var userId = GetCurrentUserId();

            if (pageSize > 100)
                pageSize = 100;

            var logs = await _auditLogRepository.GetUserAuditLogsAsync(userId, pageSize, page);
            var totalCount = await _auditLogRepository.GetUserAuditLogsCountAsync(userId);

            var response = new AuditLogsPagedResponseDto
            {
                Logs = logs.Select(log => new AuditLogResponseDto
                {
                    AuditId = log.AuditId,
                    UserId = log.UserId,
                    Action = log.Action.ToString(),
                    EntityType = log.EntityType.ToString(),
                    EntityId = log.EntityId,
                    OldValue = log.OldValue,
                    NewValue = log.NewValue,
                    IpAddress = log.IpAddress,
                    UserAgent = log.UserAgent,
                    CreatedAt = log.CreatedAt,
                    UserEmail = log.User?.Email
                }).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account audit logs");
            return StatusCode(500, new { message = "Error al obtener los logs de auditoría de la cuenta" });
        }
    }

    /// <summary>
    /// Obtiene los logs de auditoría de las bases de datos del usuario
    /// </summary>
    [HttpGet("databases")]
    public async Task<ActionResult<AuditLogsPagedResponseDto>> GetDatabaseLogs(
        [FromQuery] Guid? instanceId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var userId = GetCurrentUserId();

            if (pageSize > 100)
                pageSize = 100;

            var logs = await _auditLogRepository.GetDatabaseAuditLogsAsync(userId, instanceId, pageSize, page);
            var totalCount = await _auditLogRepository.GetDatabaseAuditLogsCountAsync(userId, instanceId);

            var response = new AuditLogsPagedResponseDto
            {
                Logs = logs.Select(log => new AuditLogResponseDto
                {
                    AuditId = log.AuditId,
                    UserId = log.UserId,
                    Action = log.Action.ToString(),
                    EntityType = log.EntityType.ToString(),
                    EntityId = log.EntityId,
                    OldValue = log.OldValue,
                    NewValue = log.NewValue,
                    IpAddress = log.IpAddress,
                    UserAgent = log.UserAgent,
                    CreatedAt = log.CreatedAt,
                    UserEmail = log.User?.Email
                }).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving database audit logs");
            return StatusCode(500, new { message = "Error al obtener los logs de auditoría de bases de datos" });
        }
    }
}

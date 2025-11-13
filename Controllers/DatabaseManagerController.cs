using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ZenCloud.Data.Entities;
using ZenCloud.DTOs.DatabaseManagement;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/databases/{instanceId}/[controller]")]
    public class DatabaseManagerController : ControllerBase
    {
        private readonly IDatabaseManagementService _dbService;
        private readonly IAuditService _auditService;
        private readonly ILogger<DatabaseManagerController> _logger;

        public DatabaseManagerController(
            IDatabaseManagementService dbService,
            IAuditService auditService,
            ILogger<DatabaseManagerController> logger)
        {
            _dbService = dbService;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(Guid instanceId)
        {
            Guid userId;
            try
            {
                userId = GetCurrentUserId();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve current user id");
                return Unauthorized(new { error = "Invalid auth token" });
            }

            try
            {
                var tables = await _dbService.GetTablesAsync(instanceId, userId);
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseCreated, "User retrieved database tables");
                return Ok(tables);
            }
            catch (ArgumentException ex)
            {
                // problema con credenciales / validación → 400
                _logger.LogWarning(ex, "Bad request when fetching tables for instance {InstanceId} (user {UserId})", instanceId, userId);
                // no revelar secretos en la respuesta
                return BadRequest(new { error = "Invalid database credentials or configuration. Check instance settings." });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access to instance {InstanceId} by {UserId}", instanceId, userId);
                await _auditService.LogSecurityEventAsync(userId, AuditAction.UserLogin, $"Unauthorized access attempt to instance {instanceId}");
                return Unauthorized(new { error = "You don't have access to this database instance" });
            }
            catch (Exception ex)
            {
                // Registrar stack completo para diagnóstico y devolver mensaje genérico
                _logger.LogError(ex, "Unexpected error retrieving tables for instance {InstanceId} (user {UserId})", instanceId, userId);
                return StatusCode(500, new { error = "Server error while fetching tables. See server logs for details." });
            }
        }

        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteQuery(Guid instanceId, [FromBody] ExecuteQueryRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _dbService.ExecuteQueryAsync(instanceId, userId, request.Query);
                
                // ✅ Usa DatabaseUpdated para ejecución de queries
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseUpdated, $"Query executed: {request.Query[..50]}...");
                
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                var userId = GetCurrentUserId();
                await _auditService.LogSecurityEventAsync(userId, AuditAction.UserLogin, $"Unauthorized query execution on instance {instanceId}");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing query");
                return StatusCode(500, new { error = "An error occurred while executing the query" });
            }
        }

        [HttpGet("tables/{tableName}/schema")]
        public async Task<IActionResult> GetTableSchema(Guid instanceId, string tableName)
        {
            try
            {
                var userId = GetCurrentUserId();
                var schema = await _dbService.GetTableSchemaAsync(instanceId, userId, tableName);
                
                // ✅ Usa DatabaseCreated para lectura de esquema
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseCreated, $"Retrieved schema for table: {tableName}");
                
                return Ok(schema);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving table schema");
                return StatusCode(500, new { error = "An error occurred while retrieving the table schema" });
            }
        }

        [HttpGet("tables/{tableName}/data")]
        public async Task<IActionResult> GetTableData(Guid instanceId, string tableName, [FromQuery] int limit = 100)
        {
            try
            {
                var userId = GetCurrentUserId();
                var data = await _dbService.GetTableDataAsync(instanceId, userId, tableName, limit);
                
                // ✅ Usa DatabaseCreated para lectura de datos
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseCreated, $"Retrieved data from table: {tableName} (limit: {limit})");
                
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving table data");
                return StatusCode(500, new { error = "An error occurred while retrieving the table data" });
            }
        }

        [HttpPost("test-connection")]
        public async Task<IActionResult> TestConnection(Guid instanceId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var success = await _dbService.TestConnectionAsync(instanceId, userId);
                
                // ✅ Usa DatabaseStatusChanged para test de conexión
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseStatusChanged, $"Connection test: {(success ? "SUCCESS" : "FAILED")}");
                
                return Ok(new { connected = success });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection");
                return StatusCode(500, new { error = "An error occurred while testing the connection" });
            }
        }
        
           
        [HttpGet("~/api/current-user-id")]
        public IActionResult GetCurrentUserIdEndpoint()
        {
            try
            {
                var userId = GetCurrentUserId();
                return Ok(new { UserId = userId });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user id");
                return StatusCode(500, new { error = "An error occurred getting current user id" });
            }
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim))
                throw new UnauthorizedAccessException("User ID not found in token");
            
            if (Guid.TryParse(userIdClaim, out var userId))
                return userId;
            
            throw new UnauthorizedAccessException("Invalid user ID format in token");
        }
    }
}

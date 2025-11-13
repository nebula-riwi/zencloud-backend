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
            try
            {
                var userId = GetCurrentUserId();
                var tables = await _dbService.GetTablesAsync(instanceId, userId);
                
                // ✅ Usa DatabaseCreated como acción genérica de lectura de BD
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseCreated, "User retrieved database tables");
                
                return Ok(tables);
            }
            catch (UnauthorizedAccessException ex)
            {
                var userId = GetCurrentUserId();
                _logger.LogWarning(ex, "Unauthorized access to instance {InstanceId}", instanceId);
                await _auditService.LogSecurityEventAsync(userId, AuditAction.UserLogin, $"Unauthorized access attempt to instance {instanceId}");
                return Unauthorized(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Instance not found: {InstanceId}", instanceId);
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                var userId = GetCurrentUserId();
                _logger.LogError(ex, "Invalid password for instance {InstanceId}", instanceId);
                await _auditService.LogDatabaseEventAsync(userId, instanceId, AuditAction.DatabaseStatusChanged, "Invalid database password - connection failed");
                return BadRequest(new { error = "Invalid database password. Please reconfigure the connection." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tables for instance {InstanceId}", instanceId);
                return StatusCode(500, new { error = "An unexpected error occurred" });
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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public DatabaseManagerController(
            IDatabaseManagementService dbService,
            IAuditService auditService)
        {
            _dbService = dbService;
            _auditService = auditService;
        }

        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteQuery(Guid instanceId, [FromBody] ExecuteQueryRequest request)
        {
            var userId = GetCurrentUserId();
            var result = await _dbService.ExecuteQueryAsync(instanceId, userId, request.Query);
            return Ok(result);
        }

        [HttpGet("tables")]
        public async Task<IActionResult> GetTables(Guid instanceId)
        {
            try
            {
                var userId = GetCurrentUserId();
                
                // ✅ Llama al servicio y captura el error de embedded nulls específicamente
                try
                {
                    var tables = await _dbService.GetTablesAsync(instanceId, userId);
                    await _auditService.LogActionAsync(userId, "VIEW_TABLES", $"Instance: {instanceId}", true);
                    return Ok(tables);
                }
                catch (ArgumentException ex) when (ex.Message.Contains("embedded nulls"))
                {
                    // ✅ Erro de contraseña con caracteres nulos → retorna 400 con mensaje claro
                    _logger.LogError(ex, "Password contains embedded nulls for instance {InstanceId}", instanceId);
                    await _auditService.LogActionAsync(userId, "VIEW_TABLES_FAILED", 
                        $"Instance: {instanceId}, Reason: Invalid password (embedded nulls)", false);
                    return BadRequest(new { error = "Invalid database password. Please reconfigure the connection." });
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access attempt to instance {InstanceId}", instanceId);
                return Unauthorized(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Instance not found: {InstanceId}", instanceId);
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument for instance {InstanceId}", instanceId);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving tables for instance {InstanceId}", instanceId);
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner exception details");
                }
                return StatusCode(500, new { error = "An unexpected error occurred", details = ex.Message });
            }
        }

        [HttpGet("tables/{tableName}/schema")]
        public async Task<IActionResult> GetTableSchema(Guid instanceId, string tableName)
        {
            var userId = GetCurrentUserId();
            var schema = await _dbService.GetTableSchemaAsync(instanceId, userId, tableName);
            return Ok(schema);
        }

        [HttpGet("tables/{tableName}/data")]
        public async Task<IActionResult> GetTableData(Guid instanceId, string tableName, [FromQuery] int limit = 100)
        {
            var userId = GetCurrentUserId();
            var data = await _dbService.GetTableDataAsync(instanceId, userId, tableName, limit);
            return Ok(data);
        }

        [HttpPost("test-connection")]
        public async Task<IActionResult> TestConnection(Guid instanceId)
        {
            var userId = GetCurrentUserId();
            var result = await _dbService.TestConnectionAsync(instanceId, userId);
            return Ok(new { success = result });
        }

        [HttpGet("info")]
        public async Task<IActionResult> GetDatabaseInfo(Guid instanceId)
        {
            var userId = GetCurrentUserId();
            var info = await _dbService.GetDatabaseInfoAsync(instanceId, userId);
            return Ok(info);
        }

        [HttpGet("processes")]
        public async Task<IActionResult> GetProcessList(Guid instanceId)
        {
            var userId = GetCurrentUserId();
            var processes = await _dbService.GetProcessListAsync(instanceId, userId);
            return Ok(processes);
        }
        
        

        [HttpPost("processes/{processId}/kill")]
        public async Task<IActionResult> KillProcess(Guid instanceId, int processId)
        {
            var userId = GetCurrentUserId();
            var result = await _dbService.KillProcessAsync(instanceId, userId, processId);
            return Ok(new { success = result });
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
                return Unauthorized(ex.Message);
            }
        }
        
        private Guid GetCurrentUserId()
        {
       
            var userIdClaim = User.FindFirst("userId")?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }

            
            userIdClaim = User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userIdClaim, out userId))
            {
                return userId;
            }

            throw new UnauthorizedAccessException("Invalid user ID");
        }
    }
}

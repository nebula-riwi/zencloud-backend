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
                var tables = await _dbService.GetTablesAsync(instanceId, userId);
                return Ok(tables);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener las tablas: " + ex.Message });
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

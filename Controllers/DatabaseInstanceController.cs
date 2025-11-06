using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabaseInstanceController : ControllerBase
{
    private readonly IDatabaseInstanceService _databaseInstanceService;

    public DatabaseInstanceController(IDatabaseInstanceService databaseInstanceService)
    {
        _databaseInstanceService = databaseInstanceService;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserDatabases(Guid userId)
    {
        try
        {
            var databases = await _databaseInstanceService.GetUserDatabasesAsync(userId);

            var response = databases.Select(db => new DatabaseInstanceResponseDto
            {
                InstanceId = db.InstanceId,
                DatabaseName = db.DatabaseName,
                DatabaseUser = db.DatabaseUser,
                AssignedPort = db.AssignedPort,
                ConnectionString = db.ConnectionString,
                Status = db.Status.ToString(),
                EngineName = db.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = db.CreatedAt
            });
            
            return Ok(new  { data = response });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{InstanceId}")]
    public async Task<IActionResult> GetDatabaseById(Guid instanceId)
    {
        try
        {
            var database = await _databaseInstanceService.GetDatabaseByIdAsync(instanceId);

            if (database == null)
            {
                return NotFound( new { message = "Base de datos no encontrada" });
            }
            
            var response = new DatabaseInstanceResponseDto
            {
                InstanceId = database.InstanceId,
                DatabaseName = database.DatabaseName,
                DatabaseUser = database.DatabaseUser,
                AssignedPort = database.AssignedPort,
                ConnectionString = database.ConnectionString,
                Status = database.Status.ToString(),
                EngineName = database.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = database.CreatedAt
            };
            
            return Ok(new { data = response });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateDatabase([FromBody] CreateDatabaseRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var database = await _databaseInstanceService.CreateDatabaseInstanceAsync(request.UserId, request.EngineId);
            
            var response = new DatabaseInstanceResponseDto
            {
                InstanceId = database.InstanceId,
                DatabaseName = database.DatabaseName,
                DatabaseUser = database.DatabaseUser,
                AssignedPort = database.AssignedPort,
                ConnectionString = database.ConnectionString,
                Status = database.Status.ToString(),
                EngineName = database.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = database.CreatedAt
            };

            return Ok(new
            {
                message = "Base de datos creada exitosamente",
                data = response
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
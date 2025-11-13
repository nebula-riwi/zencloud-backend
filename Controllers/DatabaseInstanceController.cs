using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using System.IdentityModel.Tokens.Jwt;
using ZenCloud.Services.Interfaces;
using ZenCloud.Exceptions;
using Swashbuckle.AspNetCore.Annotations;

namespace ZenCloud.Controllers;

/// <summary>
/// Controlador para gestión de instancias de bases de datos
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[SwaggerTag("Endpoints para crear, listar, obtener y eliminar bases de datos")]
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

    [HttpGet("{instanceId}")]
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
    
    /// <summary>
    /// Crea una nueva instancia de base de datos
    /// </summary>
    /// <param name="request">Datos para crear la base de datos</param>
    /// <returns>Información de la base de datos creada</returns>
    /// <response code="200">Base de datos creada exitosamente</response>
    /// <response code="400">Solicitud inválida o error en la creación</response>
    /// <response code="401">No autenticado</response>
    /// <response code="409">Límite de bases de datos alcanzado para el plan</response>
    /// <response code="422">Errores de validación</response>
    [HttpPost]
    [SwaggerOperation(
        Summary = "Crear base de datos",
        Description = "Crea una nueva instancia de base de datos para el usuario. Valida el plan y los límites antes de crear."
    )]
    [SwaggerResponse(200, "Base de datos creada exitosamente")]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(409, "Límite de bases de datos alcanzado")]
    [SwaggerResponse(422, "Errores de validación")]
    public async Task<IActionResult> CreateDatabase([FromBody] CreateDatabaseRequestDto request)
    {
        var database = await _databaseInstanceService.CreateDatabaseInstanceAsync(request.UserId, request.EngineId, request.DatabaseName);
        
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
    
    
    /// <summary>
    /// Elimina una instancia de base de datos
    /// </summary>
    /// <param name="instanceId">ID de la instancia a eliminar</param>
    /// <param name="userId">ID del usuario propietario</param>
    /// <returns>Confirmación de eliminación</returns>
    /// <response code="200">Base de datos eliminada exitosamente</response>
    /// <response code="400">Error al eliminar la base de datos</response>
    /// <response code="404">Base de datos no encontrada</response>
    [HttpDelete("{instanceId}")]
    [SwaggerOperation(
        Summary = "Eliminar base de datos",
        Description = "Elimina una instancia de base de datos. También elimina la base de datos física del servidor."
    )]
    [SwaggerResponse(200, "Base de datos eliminada exitosamente")]
    [SwaggerResponse(400, "Error al eliminar")]
    [SwaggerResponse(404, "Base de datos no encontrada")]
    public async Task<IActionResult> DeleteDatabase(Guid instanceId, [FromQuery] Guid userId)
    {
        await _databaseInstanceService.DeleteDatabaseInstanceAsync(instanceId, userId);
        return Ok(new { message = "Base de datos eliminada exitosamente" });
    }

    [HttpGet("my-databases")]
    [SwaggerOperation(Summary = "Obtener mis bases de datos", Description = "Retorna todas las bases de datos del usuario autenticado")]
    [SwaggerResponse(200, "Lista de bases de datos")]
    [SwaggerResponse(401, "No autenticado")]
    public async Task<IActionResult> GetMyDatabases()
    {
        try
        { 
            // Buscar userId en múltiples claims (igual que otros controladores)
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                             User.FindFirst("sub")?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

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
                CreatedAt = db.CreatedAt,
                ServerIpAddress = db.ServerIpAddress
            });
        
            return Ok(new { data = response });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Error al obtener las bases de datos" });
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using System.IdentityModel.Tokens.Jwt;
using ZenCloud.Services.Interfaces;
using ZenCloud.Exceptions;
using Swashbuckle.AspNetCore.Annotations;
using System.Linq;

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
                CreatedAt = db.CreatedAt,
                ServerIpAddress = db.ServerIpAddress
            });
            
            return Ok(new  { data = response });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("engines")]
    public async Task<IActionResult> GetDatabaseEngines()
    {
        var engines = await _databaseInstanceService.GetActiveEnginesAsync();

        var response = engines.Select(engine => new
        {
            id = engine.EngineId,
            name = engine.EngineName.ToString(),
            slug = engine.EngineName.ToString().ToLowerInvariant(),
            defaultPort = engine.DefaultPort,
            description = engine.Description,
            isActive = engine.IsActive
        });

        return Ok(new { data = response });
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
                CreatedAt = database.CreatedAt,
                ServerIpAddress = database.ServerIpAddress
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
        try
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
                CreatedAt = database.CreatedAt,
                ServerIpAddress = database.ServerIpAddress
            };

            return Ok(new
            {
                message = "Base de datos creada exitosamente",
                data = response
            });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (BadRequestException ex)
        {
            return BadRequest(new { message = ex.Message, errorCode = ex.ErrorCode, details = ex.Details });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message, errorCode = ex.ErrorCode });
        }
        catch (Exception)
        {
            // Dejar que el middleware global maneje otras excepciones
            throw;
        }
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
        Description = "Elimina una instancia de base de datos. También elimina la base de datos física del servidor. Solo se pueden eliminar bases de datos desactivadas."
    )]
    [SwaggerResponse(200, "Base de datos eliminada exitosamente")]
    [SwaggerResponse(400, "Error al eliminar")]
    [SwaggerResponse(404, "Base de datos no encontrada")]
    public async Task<IActionResult> DeleteDatabase(Guid instanceId)
    {
        try
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

            await _databaseInstanceService.DeleteDatabaseInstanceAsync(instanceId, userId);
            return Ok(new { message = "Base de datos eliminada exitosamente" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Rota las credenciales de una base de datos (usuario y contraseña)
    /// </summary>
    /// <param name="instanceId">ID de la instancia de base de datos</param>
    /// <returns>Información actualizada de la base de datos</returns>
    /// <response code="200">Credenciales rotadas exitosamente</response>
    /// <response code="400">Error al rotar las credenciales</response>
    /// <response code="404">Base de datos no encontrada</response>
    [HttpPost("{instanceId}/rotate-credentials")]
    [SwaggerOperation(
        Summary = "Rotar credenciales",
        Description = "Genera nuevas credenciales (usuario y contraseña) para la base de datos y las actualiza automáticamente en la base de datos física. Las nuevas credenciales se envían por correo electrónico."
    )]
    [SwaggerResponse(200, "Credenciales rotadas exitosamente")]
    [SwaggerResponse(400, "Error al rotar credenciales")]
    [SwaggerResponse(404, "Base de datos no encontrada")]
    public async Task<IActionResult> RotateCredentials(Guid instanceId)
    {
        try
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

            var (database, newPassword) = await _databaseInstanceService.RotateCredentialsAsync(instanceId, userId);
            
            var response = new RotateCredentialsResponseDto
            {
                InstanceId = database.InstanceId,
                DatabaseName = database.DatabaseName,
                DatabaseUser = database.DatabaseUser,
                DatabasePassword = newPassword, // Incluir la contraseña en la respuesta
                AssignedPort = database.AssignedPort,
                ConnectionString = database.ConnectionString,
                Status = database.Status.ToString(),
                EngineName = database.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = database.CreatedAt,
                ServerIpAddress = database.ServerIpAddress
            };
            
            return Ok(new
            {
                message = "Credenciales rotadas exitosamente. Las nuevas credenciales se han enviado por correo electrónico.",
                data = response
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Activa una base de datos desactivada
    /// </summary>
    /// <param name="instanceId">ID de la instancia de base de datos</param>
    /// <returns>Información actualizada de la base de datos</returns>
    /// <response code="200">Base de datos activada exitosamente</response>
    /// <response code="400">Error al activar la base de datos</response>
    /// <response code="404">Base de datos no encontrada</response>
    /// <response code="409">Límite de bases activas alcanzado</response>
    [HttpPost("{instanceId}/activate")]
    [SwaggerOperation(
        Summary = "Activar base de datos",
        Description = "Activa una base de datos que estaba desactivada. Valida que el usuario no haya alcanzado el límite de bases activas según su plan."
    )]
    [SwaggerResponse(200, "Base de datos activada exitosamente")]
    [SwaggerResponse(400, "Error al activar la base de datos")]
    [SwaggerResponse(404, "Base de datos no encontrada")]
    [SwaggerResponse(409, "Límite de bases activas alcanzado")]
    public async Task<IActionResult> ActivateDatabase(Guid instanceId)
    {
        try
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

            var database = await _databaseInstanceService.ActivateDatabaseAsync(instanceId, userId);
            
            var response = new DatabaseInstanceResponseDto
            {
                InstanceId = database.InstanceId,
                DatabaseName = database.DatabaseName,
                DatabaseUser = database.DatabaseUser,
                AssignedPort = database.AssignedPort,
                ConnectionString = database.ConnectionString,
                Status = database.Status.ToString(),
                EngineName = database.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = database.CreatedAt,
                ServerIpAddress = database.ServerIpAddress
            };
            
            return Ok(new
            {
                message = "Base de datos activada exitosamente",
                data = response
            });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Desactiva una base de datos activa
    /// </summary>
    /// <param name="instanceId">ID de la instancia de base de datos</param>
    /// <returns>Información actualizada de la base de datos</returns>
    /// <response code="200">Base de datos desactivada exitosamente</response>
    /// <response code="400">Error al desactivar la base de datos</response>
    /// <response code="404">Base de datos no encontrada</response>
    [HttpPost("{instanceId}/deactivate")]
    [SwaggerOperation(
        Summary = "Desactivar base de datos",
        Description = "Desactiva una base de datos activa. La base de datos permanecerá en el sistema pero no estará disponible para uso."
    )]
    [SwaggerResponse(200, "Base de datos desactivada exitosamente")]
    [SwaggerResponse(400, "Error al desactivar la base de datos")]
    [SwaggerResponse(404, "Base de datos no encontrada")]
    public async Task<IActionResult> DeactivateDatabase(Guid instanceId)
    {
        try
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

            var database = await _databaseInstanceService.DeactivateDatabaseAsync(instanceId, userId);
            
            var response = new DatabaseInstanceResponseDto
            {
                InstanceId = database.InstanceId,
                DatabaseName = database.DatabaseName,
                DatabaseUser = database.DatabaseUser,
                AssignedPort = database.AssignedPort,
                ConnectionString = database.ConnectionString,
                Status = database.Status.ToString(),
                EngineName = database.Engine?.EngineName.ToString() ?? "Unknown",
                CreatedAt = database.CreatedAt,
                ServerIpAddress = database.ServerIpAddress
            };
            
            return Ok(new
            {
                message = "Base de datos desactivada exitosamente",
                data = response
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

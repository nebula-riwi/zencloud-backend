using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using ZenCloud.Data.Repositories.Interfaces;
using System.Threading.Tasks;
using ZenCloud.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.Annotations;
using ZenCloud.Exceptions;

namespace ZenCloud.Controllers;

/// <summary>
/// Controlador para autenticación y gestión de usuarios
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[SwaggerTag("Endpoints para registro, login, verificación de email y recuperación de contraseña")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Registra un nuevo usuario en el sistema
    /// </summary>
    /// <param name="model">Datos del nuevo usuario</param>
    /// <returns>Mensaje de confirmación</returns>
    /// <response code="200">Registro exitoso. Se envió un email de verificación</response>
    /// <response code="400">Datos de registro inválidos</response>
    /// <response code="409">El email ya está registrado</response>
    /// <response code="422">Errores de validación</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Registrar nuevo usuario",
        Description = "Crea una nueva cuenta de usuario. Se enviará un email de verificación al correo proporcionado."
    )]
    [SwaggerResponse(200, "Registro exitoso", typeof(string))]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(409, "El email ya está registrado")]
    [SwaggerResponse(422, "Errores de validación")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        bool result = await _authService.RegisterAsync(model);

        if (!result)
        {
            throw new ConflictException("El registro falló. El email puede estar ya registrado.");
        }

        return Ok(new { message = "Registro exitoso. Por favor, revise su correo electrónico para verificar su cuenta." });
    }

    /// <summary>
    /// Verifica el email de un usuario mediante token
    /// </summary>
    /// <param name="email">Email del usuario</param>
    /// <param name="token">Token de verificación</param>
    /// <returns>Mensaje de confirmación</returns>
    /// <response code="200">Email verificado exitosamente</response>
    /// <response code="400">Token o email inválido</response>
    [HttpGet("verify")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Verificar email",
        Description = "Verifica el email del usuario usando el token enviado por correo electrónico."
    )]
    [SwaggerResponse(200, "Email verificado exitosamente")]
    [SwaggerResponse(400, "Token o email inválido")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            throw new BadRequestException("Solicitud de verificación inválida. Email y token son requeridos.");
        }

        bool result = await _authService.VerifyEmailAsync(email, token);

        if (!result)
        {
            throw new BadRequestException("La verificación del email falló. El email o token son inválidos.");
        }

        return Ok(new { message = "Email verificado exitosamente. Ya puede iniciar sesión." });
    }

    /// <summary>
    /// Inicia sesión y obtiene un token JWT
    /// </summary>
    /// <param name="model">Credenciales de acceso</param>
    /// <returns>Token JWT para autenticación</returns>
    /// <response code="200">Login exitoso. Retorna el token JWT</response>
    /// <response code="401">Credenciales inválidas</response>
    /// <response code="422">Errores de validación</response>
    /// <response code="429">Límite de intentos excedido (5 por minuto)</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Iniciar sesión",
        Description = "Autentica un usuario y retorna un token JWT que debe usarse en las solicitudes posteriores."
    )]
    [SwaggerResponse(200, "Login exitoso", typeof(object))]
    [SwaggerResponse(401, "Credenciales inválidas")]
    [SwaggerResponse(422, "Errores de validación")]
    [SwaggerResponse(429, "Límite de intentos excedido")]
    public async Task<IActionResult> Login([FromBody] LoginRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string? token = await _authService.LoginAsync(model);

        if (token == null)
        {
            throw new UnauthorizedException("Credenciales inválidas. Verifique su email y contraseña.");
        }

        return Ok(new { Token = token });
    }

    /// <summary>
    /// Solicita el restablecimiento de contraseña
    /// </summary>
    /// <param name="model">Email del usuario</param>
    /// <returns>Mensaje de confirmación</returns>
    /// <response code="200">Si el email existe, se enviarán instrucciones de restablecimiento</response>
    /// <response code="422">Errores de validación</response>
    /// <response code="429">Límite de solicitudes excedido (3 por hora)</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Solicitar restablecimiento de contraseña",
        Description = "Envía un email con instrucciones para restablecer la contraseña. Por seguridad, siempre retorna éxito aunque el email no exista."
    )]
    [SwaggerResponse(200, "Solicitud procesada")]
    [SwaggerResponse(422, "Errores de validación")]
    [SwaggerResponse(429, "Límite de solicitudes excedido")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool result = await _authService.RequestPasswordResetAsync(model.Email);

        return Ok(new { message = "If the email exists, password reset instructions will be sent." });
    }

    /// <summary>
    /// Restablece la contraseña usando un token
    /// </summary>
    /// <param name="model">Datos para restablecer la contraseña</param>
    /// <returns>Mensaje de confirmación</returns>
    /// <response code="200">Contraseña restablecida exitosamente</response>
    /// <response code="400">Token inválido o expirado</response>
    /// <response code="422">Errores de validación</response>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Restablecer contraseña",
        Description = "Restablece la contraseña del usuario usando el token recibido por email."
    )]
    [SwaggerResponse(200, "Contraseña restablecida exitosamente")]
    [SwaggerResponse(400, "Token inválido o expirado")]
    [SwaggerResponse(422, "Errores de validación")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        bool result = await _authService.ResetPasswordAsync(model.Email, model.Token, model.NewPassword);

        if (!result)
        {
            throw new BadRequestException("No se pudo restablecer la contraseña. El token es inválido o ha expirado.");
        }

        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }
}
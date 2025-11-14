using Microsoft.AspNetCore.Mvc;
using ZenCloud.DTOs;
using ZenCloud.Data.Repositories.Interfaces;
using System.Threading.Tasks;
using ZenCloud.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.Annotations;
using ZenCloud.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

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
    private readonly ILogger<AuthController> _logger;
    private static readonly Lazy<string> VerificationResultTemplate = new(() =>
    {
        var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "EmailVerificationResultTemplate.html");
        if (System.IO.File.Exists(templatePath))
        {
            return System.IO.File.ReadAllText(templatePath);
        }

        return @"<!DOCTYPE html><html lang=""es""><head><meta charset=""UTF-8""><title>ZenCloud · Verificación</title></head><body style=""font-family:Arial, sans-serif;background:#0f1014;color:#fff;display:flex;align-items:center;justify-content:center;height:100vh;""><div style=""max-width:480px;padding:32px;background:#161821;border-radius:24px;text-align:center;border:1px solid rgba(255,255,255,0.1);""><h1 style=""margin-bottom:12px;"">{{TITLE}}</h1><p style=""margin-bottom:24px;color:#bbb;"">{{MESSAGE}}</p><a href=""{{CTA_URL}}"" style=""display:inline-block;padding:12px 28px;border-radius:999px;background:#e78a53;color:#0f1014;text-decoration:none;font-weight:600;"">{{CTA_LABEL}}</a></div></body></html>";
    });

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
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
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return RenderVerificationResult(new VerificationViewModel
            {
                IsSuccess = false,
                Title = "Solicitud incompleta",
                Message = "El enlace de verificación no contiene la información necesaria. Solicita un nuevo correo desde ZenCloud.",
                StatusText = "Error de verificación",
                CtaLabel = "Solicitar nuevo enlace",
                CtaUrl = "https://nebula.andrescortes.dev/?action=resend"
            });
        }

        VerificationViewModel viewModel;

        try
        {
            bool verified = await _authService.VerifyEmailAsync(email, token);

            viewModel = verified
                ? new VerificationViewModel
                {
                    IsSuccess = true,
                    Title = "¡Tu correo ha sido verificado!",
                    Message = "Ya puedes iniciar sesión en ZenCloud para gestionar tus bases de datos.",
                    StatusText = "Cuenta verificada",
                    CtaLabel = "Ir al inicio de sesión",
                    CtaUrl = "https://nebula.andrescortes.dev/?action=login"
                }
                : new VerificationViewModel
                {
                    IsSuccess = false,
                    Title = "El enlace no es válido o expiró",
                    Message = "Solicita un nuevo correo de verificación para completar el proceso.",
                    StatusText = "Error de verificación",
                    CtaLabel = "Solicitar nuevo enlace",
                    CtaUrl = "https://nebula.andrescortes.dev/?action=resend"
                };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verificando correo para {Email}", email);
            viewModel = new VerificationViewModel
            {
                IsSuccess = false,
                Title = "No pudimos validar tu cuenta",
                Message = "Se produjo un error interno. Intenta nuevamente más tarde o solicita un nuevo enlace.",
                StatusText = "Error de verificación",
                CtaLabel = "Volver al inicio",
                CtaUrl = "https://nebula.andrescortes.dev"
            };
        }

        return RenderVerificationResult(viewModel);
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

    private ContentResult RenderVerificationResult(VerificationViewModel model)
    {
        var template = VerificationResultTemplate.Value;
        var html = template
            .Replace("{{STATUS_CLASS}}", model.StatusClass)
            .Replace("{{STATUS_TEXT}}", model.StatusText)
            .Replace("{{TITLE}}", model.Title)
            .Replace("{{MESSAGE}}", model.Message)
            .Replace("{{CTA_LABEL}}", model.CtaLabel)
            .Replace("{{CTA_URL}}", model.CtaUrl);

        return Content(html, "text/html; charset=utf-8");
    }
}

record VerificationViewModel
{
    public bool IsSuccess { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string CtaLabel { get; init; } = string.Empty;
    public string CtaUrl { get; init; } = string.Empty;
    public string StatusClass => IsSuccess ? "success" : "error";
}

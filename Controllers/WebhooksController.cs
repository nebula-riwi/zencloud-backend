using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using ZenCloud.DTOs;
using ZenCloud.Services.Interfaces;
using ZenCloud.Exceptions;
using Swashbuckle.AspNetCore.Annotations;

namespace ZenCloud.Controllers;

/// <summary>
/// Controlador para gestión de webhooks
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[SwaggerTag("Endpoints para crear, listar, actualizar y eliminar webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IWebhookService webhookService, ILogger<WebhooksController> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Usuario no autenticado");
        }
        return userId;
    }

    /// <summary>
    /// Obtiene todos los webhooks del usuario actual
    /// </summary>
    /// <returns>Lista de webhooks</returns>
    [HttpGet]
    [SwaggerOperation(Summary = "Obtener webhooks del usuario", Description = "Retorna todos los webhooks configurados por el usuario autenticado")]
    [SwaggerResponse(200, "Lista de webhooks")]
    [SwaggerResponse(401, "No autenticado")]
    public async Task<IActionResult> GetWebhooks()
    {
        // Webhooks deshabilitados temporalmente
        return Ok(new { data = new List<WebhookResponse>() });
    }

    /// <summary>
    /// Crea un nuevo webhook
    /// </summary>
    /// <param name="request">Datos del webhook</param>
    /// <returns>Webhook creado</returns>
    [HttpPost]
    [SwaggerOperation(Summary = "Crear webhook", Description = "Crea un nuevo webhook para recibir notificaciones de eventos")]
    [SwaggerResponse(200, "Webhook creado exitosamente")]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(422, "Errores de validación")]
    public async Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest request)
    {
        // Webhooks deshabilitados temporalmente
        return Ok(new { message = "Webhooks temporalmente deshabilitados", data = (object?)null });
    }

    /// <summary>
    /// Actualiza un webhook existente
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <param name="request">Datos a actualizar</param>
    /// <returns>Webhook actualizado</returns>
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar webhook", Description = "Actualiza la configuración de un webhook existente")]
    [SwaggerResponse(200, "Webhook actualizado exitosamente")]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> UpdateWebhook(Guid id, [FromBody] UpdateWebhookRequest request)
    {
        // Webhooks deshabilitados temporalmente
        return Ok(new { message = "Webhooks temporalmente deshabilitados", data = (object?)null });
    }

    /// <summary>
    /// Elimina un webhook
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar webhook", Description = "Elimina un webhook del usuario")]
    [SwaggerResponse(200, "Webhook eliminado exitosamente")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> DeleteWebhook(Guid id)
    {
        // Webhooks deshabilitados temporalmente
        return Ok(new { message = "Webhooks temporalmente deshabilitados" });
    }

    /// <summary>
    /// Prueba un webhook enviando un evento de prueba
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <returns>Resultado de la prueba</returns>
    [HttpPost("{id}/test")]
    [SwaggerOperation(Summary = "Probar webhook", Description = "Envía un evento de prueba al webhook")]
    [SwaggerResponse(200, "Prueba enviada exitosamente")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> TestWebhook(Guid id)
    {
        // Webhooks deshabilitados temporalmente
        return Ok(new { message = "Webhooks temporalmente deshabilitados" });
    }
}
